using System.Runtime.InteropServices;
using System.Text;
using Gpui.Interop;

namespace Gpui;

/// <summary>
/// Owns reusable unmanaged render buffers allocated and resized by managed code.
/// Rust borrows completed output only while synchronously decoding an owned snapshot.
/// Authoring, validation, reset, and explicit disposal must run on the creating thread.
/// </summary>
public sealed unsafe class RenderArenaOwner : IDisposable
{
    private RenderArena* _arena;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private int _accessCount;
    private bool _formatting;

    internal RenderArena* NativeArena
    {
        get { AssertThread(); return _arena; }
    }

    private void AssertThread()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Render arenas can only be used on their rendering thread.");
    }

    internal RenderArena* GetArena(uint generation)
    {
        AssertThread();
        ObjectDisposedException.ThrowIf(_arena == null, this);
        if (_arena->Generation != generation)
            throw new InvalidOperationException("Element or context escaped its render generation.");
        return _arena;
    }

    internal AccessScope Access(uint generation)
    {
        var arena = GetArena(generation);
        if (_formatting)
            throw new InvalidOperationException("Render arena access cannot reenter an active formatter.");
        _accessCount++;
        return new AccessScope(this, arena);
    }

    internal AccessScope Access()
    {
        AssertThread();
        ObjectDisposedException.ThrowIf(_arena == null, this);
        return Access(_arena->Generation);
    }

    internal FormattingScope EnterFormatter()
    {
        AssertThread();
        if (_accessCount == 0 || _formatting)
            throw new InvalidOperationException("Formatting requires an exclusive active arena write.");
        _formatting = true;
        return new FormattingScope(this);
    }

    internal readonly struct FormattingScope(RenderArenaOwner owner) : IDisposable
    {
        public void Dispose() => owner._formatting = false;
    }

    // Keep the owner rooted through the last pointer use, including calls into formatters.
    // Explicit reset/disposal cannot invalidate memory while an access is active.
    internal readonly struct AccessScope(RenderArenaOwner owner, RenderArena* arena) : IDisposable
    {
        internal RenderArena* Arena => arena;
        public void Dispose()
        {
            owner._accessCount--;
            GC.KeepAlive(owner);
        }
    }

    /// <summary>
    /// Publishes a descriptor, not a second copy of the buffers. The receiver must finish
    /// decoding before this owner is reset, written again, or disposed.
    /// </summary>
    internal void PublishTo(RenderArena* output, Element root)
    {
        using var access = Access();
        if (output == null || output == _arena)
        {
            throw new ArgumentException("Output must be a separate writable descriptor.", nameof(output));
        }
        if (root.Arena != _arena || root.Generation != _arena->Generation
            || root.Node >= (uint)_arena->NodeLength)
        {
            throw new InvalidOperationException("Cannot publish a foreign or stale render root.");
        }

        *output = *_arena;
    }

    public RenderArenaOwner(
        int nodeCapacity = 256,
        int opCapacity = 2048,
        int childCapacity = 512,
        int utf8Capacity = 16 * 1024
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(opCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(childCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(utf8Capacity);

        _arena = (RenderArena*)NativeMemory.AllocZeroed((nuint)sizeof(RenderArena));
        if (_arena == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            _arena->Nodes = Allocate<NodeRecord>(nodeCapacity);
            _arena->NodeCapacity = nodeCapacity;

            _arena->Ops = Allocate<OpRecord>(opCapacity);
            _arena->OpCapacity = opCapacity;

            _arena->Children = Allocate<ChildRecord>(childCapacity);
            _arena->ChildCapacity = childCapacity;

            _arena->Utf8 = Allocate<byte>(utf8Capacity);
            _arena->Utf8Capacity = utf8Capacity;

            _arena->Generation = 1;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public RenderContext BeginRender()
    {
        BeginRenderCore();
        return new RenderContext(this);
    }

    internal RenderContext BeginRender(IViewRenderer views, ViewBase owner, GpuiTheme? theme = null)
    {
        BeginRenderCore();
        return new RenderContext(this, views, owner, theme ?? GpuiTheme.Default);
    }

    private void BeginRenderCore()
    {
        AssertThread();
        ObjectDisposedException.ThrowIf(_arena == null, this);
        if (_accessCount != 0)
            throw new InvalidOperationException("Cannot reset an arena during an active write or validation.");

        // Never let a stale Element become current again through generation wraparound.
        var generation = checked(_arena->Generation + 1);

        _arena->NodeLength = 0;
        _arena->OpLength = 0;
        _arena->ChildLength = 0;
        _arena->Utf8Length = 0;

        _arena->Generation = generation;
    }

    public ArenaStats GetStats()
    {
        using var access = Access();
        return new ArenaStats(
            _arena->NodeLength,
            _arena->OpLength,
            _arena->ChildLength,
            _arena->Utf8Length,
            _arena->Generation
        );
    }

    public void Validate(Element root)
    {
        using var access = Access();
        ManagedValidator.Validate(_arena, root);
    }

    public string Dump(Element root)
    {
        using var access = Access();
        ManagedValidator.Validate(_arena, root);

        var sb = new StringBuilder();
        sb.AppendLine($"root={root.Node} generation={_arena->Generation}");

        for (var i = 0; i < _arena->NodeLength; i++)
        {
            ref readonly var node = ref _arena->Nodes[i];
            sb.Append("node ").Append(i).Append(" component=").Append((ComponentId)node.Component);

            if (node.DataLength != 0)
            {
                var bytes = new ReadOnlySpan<byte>(
                    _arena->Utf8 + node.DataOffset,
                    checked((int)node.DataLength)
                );
                sb.Append(" data=\"").Append(Encoding.UTF8.GetString(bytes)).Append('"');
            }

            sb.AppendLine();
        }

        for (var i = 0; i < _arena->OpLength; i++)
        {
            ref readonly var op = ref _arena->Ops[i];
            sb.Append("op node=")
                .Append(op.Node)
                .Append(" code=")
                .Append((OpCode)op.Code)
                .Append(" kind=")
                .Append((ValueKind)op.ValueKind)
                .Append(" a=")
                .Append(op.A)
                .AppendLine();
        }

        for (var i = 0; i < _arena->ChildLength; i++)
        {
            ref readonly var child = ref _arena->Children[i];
            sb.Append("child parent=")
                .Append(child.Parent)
                .Append(" child=")
                .Append(child.Child)
                .AppendLine();
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        AssertThread();
        if (_accessCount != 0)
            throw new InvalidOperationException("Cannot dispose an arena during an active write or validation.");
        Free();
        GC.SuppressFinalize(this);
    }

    private void Free()
    {
        if (_arena == null)
        {
            return;
        }

        NativeMemory.Free(_arena->Nodes);
        NativeMemory.Free(_arena->Ops);
        NativeMemory.Free(_arena->Children);
        NativeMemory.Free(_arena->Utf8);
        NativeMemory.Free(_arena);
        _arena = null;
    }

    ~RenderArenaOwner() => Free();

    private static T* Allocate<T>(int count)
        where T : unmanaged
    {
        var ptr = (T*)NativeMemory.Alloc(checked((nuint)count * (nuint)sizeof(T)));
        return ptr == null ? throw new OutOfMemoryException() : ptr;
    }
}

public readonly struct ArenaStats
{
    public readonly int Nodes;
    public readonly int Ops;
    public readonly int Children;
    public readonly int Utf8Bytes;
    public readonly uint Generation;

    internal ArenaStats(int nodes, int ops, int children, int utf8Bytes, uint generation)
    {
        Nodes = nodes;
        Ops = ops;
        Children = children;
        Utf8Bytes = utf8Bytes;
        Generation = generation;
    }
}
