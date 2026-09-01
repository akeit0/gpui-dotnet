using System.Runtime.InteropServices;
using System.Text;
using Gpui.Interop;

namespace Gpui;

/// <summary>
/// Managed test/benchmark owner for the unmanaged render arena.
///
/// Production rendering uses a Rust-owned arena lent to managed Render(). This
/// owner remains useful for isolated builder tests and allocation measurements.
/// </summary>
public sealed unsafe class RenderArenaOwner : IDisposable
{
    private RenderArena* _arena;

    internal RenderArena* NativeArena => _arena;

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
        return new RenderContext(_arena);
    }

    internal RenderContext BeginRender(IViewRenderer views, ViewBase owner, GpuiTheme? theme = null)
    {
        BeginRenderCore();
        return new RenderContext(_arena, views, owner, theme ?? GpuiTheme.Default);
    }

    private void BeginRenderCore()
    {
        ObjectDisposedException.ThrowIf(_arena == null, this);

        _arena->NodeLength = 0;
        _arena->OpLength = 0;
        _arena->ChildLength = 0;
        _arena->Utf8Length = 0;

        _arena->Generation = unchecked(_arena->Generation + 1);
        if (_arena->Generation == 0)
        {
            _arena->Generation = 1;
        }
    }

    public ArenaStats GetStats()
    {
        ObjectDisposedException.ThrowIf(_arena == null, this);
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
        ObjectDisposedException.ThrowIf(_arena == null, this);
        ManagedValidator.Validate(_arena, root);
    }

    public string Dump(Element root)
    {
        ObjectDisposedException.ThrowIf(_arena == null, this);
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

        GC.SuppressFinalize(this);
    }

    ~RenderArenaOwner() => Dispose();

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
