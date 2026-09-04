using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Gpui.Interop;

namespace Gpui;

internal static unsafe class ArenaWriter
{
    internal static Element<NativeExtensionTag> AddNativeExtensionNode(
        RenderArena* arena,
        NativeExtensionComponent component,
        ReadOnlySpan<char> key,
        ReadOnlySpan<char> configuration
    )
    {
        Span<byte> version = stackalloc byte[10];
        Span<byte> schemaHash = stackalloc byte[16];
        if (!component.Extension.Version.TryFormat(version, out var versionLength))
        {
            throw new InvalidOperationException("Failed to encode the extension version.");
        }
        if (
            !component.Extension.SchemaHash.TryFormat(
                schemaHash,
                out var schemaHashLength,
                "X16",
                null
            )
        )
        {
            throw new InvalidOperationException("Failed to encode the extension schema hash.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(component.Extension.Id)
            + Encoding.UTF8.GetByteCount(component.Kind)
            + Encoding.UTF8.GetByteCount(key)
            + versionLength
            + schemaHashLength
            + Encoding.UTF8.GetByteCount(configuration)
            + 5
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(component.Extension.Id, destination);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(component.Kind, destination[written..]);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(key, destination[written..]);
        destination[written++] = 0;
        version[..versionLength].CopyTo(destination[written..]);
        written += versionLength;
        destination[written++] = 0;
        schemaHash[..schemaHashLength].CopyTo(destination[written..]);
        written += schemaHashLength;
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(configuration, destination[written..]);
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)ComponentId.NativeExtension,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<NativeExtensionTag>(new Element(arena, node, arena->Generation));
    }

    internal static Element<NativeExtensionTag> AddNativeExtensionNode(
        RenderArena* arena,
        NativeExtensionComponent component,
        ReadOnlySpan<byte> utf8Key,
        ReadOnlySpan<char> configuration
    )
    {
        Span<byte> version = stackalloc byte[10];
        Span<byte> schemaHash = stackalloc byte[16];
        if (!component.Extension.Version.TryFormat(version, out var versionLength))
        {
            throw new InvalidOperationException("Failed to encode the extension version.");
        }
        if (
            !component.Extension.SchemaHash.TryFormat(
                schemaHash,
                out var schemaHashLength,
                "X16",
                null
            )
        )
        {
            throw new InvalidOperationException("Failed to encode the extension schema hash.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(component.Extension.Id)
            + Encoding.UTF8.GetByteCount(component.Kind)
            + utf8Key.Length
            + versionLength
            + schemaHashLength
            + Encoding.UTF8.GetByteCount(configuration)
            + 5
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(component.Extension.Id, destination);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(component.Kind, destination[written..]);
        destination[written++] = 0;
        utf8Key.CopyTo(destination[written..]);
        written += utf8Key.Length;
        destination[written++] = 0;
        version[..versionLength].CopyTo(destination[written..]);
        written += versionLength;
        destination[written++] = 0;
        schemaHash[..schemaHashLength].CopyTo(destination[written..]);
        written += schemaHashLength;
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(configuration, destination[written..]);
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)ComponentId.NativeExtension,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<NativeExtensionTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>
    /// Writes an extension node whose configuration is already UTF-8. The bytes must not
    /// contain NUL, which separates the node data fields; validated by the caller.
    /// </summary>
    internal static Element<NativeExtensionTag> AddNativeExtensionNode(
        RenderArena* arena,
        NativeExtensionComponent component,
        ReadOnlySpan<char> key,
        ReadOnlySpan<byte> utf8Configuration
    )
    {
        Span<byte> version = stackalloc byte[10];
        Span<byte> schemaHash = stackalloc byte[16];
        if (!component.Extension.Version.TryFormat(version, out var versionLength))
        {
            throw new InvalidOperationException("Failed to encode the extension version.");
        }
        if (
            !component.Extension.SchemaHash.TryFormat(
                schemaHash,
                out var schemaHashLength,
                "X16",
                null
            )
        )
        {
            throw new InvalidOperationException("Failed to encode the extension schema hash.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(component.Extension.Id)
            + Encoding.UTF8.GetByteCount(component.Kind)
            + Encoding.UTF8.GetByteCount(key)
            + versionLength
            + schemaHashLength
            + utf8Configuration.Length
            + 5
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(component.Extension.Id, destination);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(component.Kind, destination[written..]);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(key, destination[written..]);
        destination[written++] = 0;
        version[..versionLength].CopyTo(destination[written..]);
        written += versionLength;
        destination[written++] = 0;
        schemaHash[..schemaHashLength].CopyTo(destination[written..]);
        written += schemaHashLength;
        destination[written++] = 0;
        utf8Configuration.CopyTo(destination[written..]);
        written += utf8Configuration.Length;
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)ComponentId.NativeExtension,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<NativeExtensionTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>
    /// Writes an extension node whose key and configuration are already UTF-8. Neither span
    /// may contain NUL, which separates the node data fields; validated by the caller.
    /// </summary>
    internal static Element<NativeExtensionTag> AddNativeExtensionNode(
        RenderArena* arena,
        NativeExtensionComponent component,
        ReadOnlySpan<byte> utf8Key,
        ReadOnlySpan<byte> utf8Configuration
    )
    {
        Span<byte> version = stackalloc byte[10];
        Span<byte> schemaHash = stackalloc byte[16];
        if (!component.Extension.Version.TryFormat(version, out var versionLength))
        {
            throw new InvalidOperationException("Failed to encode the extension version.");
        }
        if (
            !component.Extension.SchemaHash.TryFormat(
                schemaHash,
                out var schemaHashLength,
                "X16",
                null
            )
        )
        {
            throw new InvalidOperationException("Failed to encode the extension schema hash.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(component.Extension.Id)
            + Encoding.UTF8.GetByteCount(component.Kind)
            + utf8Key.Length
            + versionLength
            + schemaHashLength
            + utf8Configuration.Length
            + 5
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(component.Extension.Id, destination);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(component.Kind, destination[written..]);
        destination[written++] = 0;
        utf8Key.CopyTo(destination[written..]);
        written += utf8Key.Length;
        destination[written++] = 0;
        version[..versionLength].CopyTo(destination[written..]);
        written += versionLength;
        destination[written++] = 0;
        schemaHash[..schemaHashLength].CopyTo(destination[written..]);
        written += schemaHashLength;
        destination[written++] = 0;
        utf8Configuration.CopyTo(destination[written..]);
        written += utf8Configuration.Length;
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)ComponentId.NativeExtension,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<NativeExtensionTag>(new Element(arena, node, arena->Generation));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Element<TTag> AddNode<TTag>(RenderArena* arena, ComponentId component)
        where TTag : unmanaged
    {
        return AddNode<TTag>(arena, component, ReadOnlySpan<char>.Empty);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Element<TTag> AddNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<char> data
    )
        where TTag : unmanaged
    {
        EnsureNodes(arena, 1);

        var dataOffset = 0u;
        var dataLength = 0u;
        if (!data.IsEmpty)
        {
            (dataOffset, dataLength) = AppendUtf8(arena, data);
        }

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = dataOffset,
            DataLength = dataLength,
        };

        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>
    /// Adds a node whose component-specific data is three UTF-8 fields separated by NUL bytes.
    /// This keeps retained resource identity and initial text configuration in one validated node
    /// payload without introducing arena-relative string pointers into operation records.
    /// </summary>
    internal static Element<TTag> AddCompositeNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third
    )
        where TTag : unmanaged
    {
        if (first.Contains('\0') || second.Contains('\0') || third.Contains('\0'))
        {
            throw new ArgumentException("Composite render data cannot contain NUL characters.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            Encoding.UTF8.GetByteCount(first)
            + Encoding.UTF8.GetByteCount(second)
            + Encoding.UTF8.GetByteCount(third)
            + 2
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(first, destination);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(second, destination[written..]);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(third, destination[written..]);
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>Adds three already-encoded UTF-8 fields separated by NUL bytes.</summary>
    internal static Element<TTag> AddCompositeNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ReadOnlySpan<byte> third
    )
        where TTag : unmanaged
    {
        if (first.Contains((byte)0) || second.Contains((byte)0) || third.Contains((byte)0))
        {
            throw new ArgumentException("Composite render data cannot contain NUL bytes.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(first.Length + second.Length + third.Length + 2);
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        first.CopyTo(destination);
        var written = first.Length;
        destination[written++] = 0;
        second.CopyTo(destination[written..]);
        written += second.Length;
        destination[written++] = 0;
        third.CopyTo(destination[written..]);
        written += third.Length;
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>Adds a UTF-8 key followed by two UTF-16 strings as NUL-separated fields.</summary>
    internal static Element<TTag> AddCompositeNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<byte> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third
    )
        where TTag : unmanaged
    {
        if (first.Contains((byte)0) || second.Contains('\0') || third.Contains('\0'))
        {
            throw new ArgumentException("Composite render data cannot contain NUL characters.");
        }

        EnsureNodes(arena, 1);
        var byteCount = checked(
            first.Length
            + Encoding.UTF8.GetByteCount(second)
            + Encoding.UTF8.GetByteCount(third)
            + 2
        );
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        first.CopyTo(destination);
        var written = first.Length;
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(second, destination[written..]);
        destination[written++] = 0;
        written += Encoding.UTF8.GetBytes(third, destination[written..]);
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    /// <summary>Adds table data with an already encoded UTF-8 key.</summary>
    internal static Element<TTag> AddTableNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<TableColumn> columns
    )
        where TTag : unmanaged
    {
        if (key.Contains((byte)0))
        {
            throw new ArgumentException("Table render data cannot contain NUL bytes.");
        }

        EnsureNodes(arena, 1);
        var byteCount = key.Length;
        foreach (var column in columns)
        {
            byteCount = checked(
                byteCount
                + Encoding.UTF8.GetByteCount(column.Key)
                + Encoding.UTF8.GetByteCount(column.Header)
                + 2
            );
        }
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        key.CopyTo(destination);
        var written = key.Length;
        foreach (var column in columns)
        {
            destination[written++] = 0;
            written += Encoding.UTF8.GetBytes(column.Key, destination[written..]);
            destination[written++] = 0;
            written += Encoding.UTF8.GetBytes(column.Header, destination[written..]);
        }
        arena->Utf8Length += written;

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = checked((uint)offset),
            DataLength = checked((uint)written),
        };
        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Element<TTag> AddNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        ReadOnlySpan<byte> utf8
    )
        where TTag : unmanaged
    {
        EnsureNodes(arena, 1);

        var dataOffset = 0u;
        var dataLength = 0u;
        if (!utf8.IsEmpty)
        {
            (dataOffset, dataLength) = AppendUtf8(arena, utf8);
        }

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = dataOffset,
            DataLength = dataLength,
        };

        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Element<TTag> AddNode<TTag>(
        RenderArena* arena,
        ComponentId component,
        uint dataOffset,
        uint dataLength
    )
        where TTag : unmanaged
    {
        EnsureNodes(arena, 1);

        var dataEnd = checked(dataOffset + dataLength);
        if (dataEnd > arena->Utf8Length)
        {
            throw new InvalidOperationException(
                "The UTF-8 payload range is outside the current render arena."
            );
        }

        var node = checked((uint)arena->NodeLength);
        arena->Nodes[arena->NodeLength++] = new NodeRecord
        {
            Component = (ushort)component,
            DataOffset = dataOffset,
            DataLength = dataLength,
        };

        return new Element<TTag>(new Element(arena, node, arena->Generation));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddChild(Element parent, Element child)
    {
        ValidateCurrent(parent);
        ValidateCurrent(child);
        if (child.Arena != parent.Arena)
        {
            throw new InvalidOperationException(
                "Parent and child belong to different render arenas."
            );
        }

        EnsureChildren(parent.Arena, 1);
        parent.Arena->Children[parent.Arena->ChildLength++] = new ChildRecord
        {
            Parent = parent.Node,
            Child = child.Node,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddChildren(Element parent, ReadOnlySpan<Element> children)
    {
        ValidateCurrent(parent);
        EnsureChildren(parent.Arena, children.Length);

        foreach (ref readonly var child in children)
        {
            ValidateCurrent(child);
            if (child.Arena != parent.Arena)
            {
                throw new InvalidOperationException(
                    "Parent and child belong to different render arenas."
                );
            }

            parent.Arena->Children[parent.Arena->ChildLength++] = new ChildRecord
            {
                Parent = parent.Node,
                Child = child.Node,
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddNoArg(Element element, OpCode code)
    {
        AddOp(element, code, ValueKind.None, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddF32(Element element, OpCode code, float value)
    {
        AddOp(element, code, ValueKind.F32, BitConverter.SingleToUInt32Bits(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddF32x2(Element element, OpCode code, float x, float y)
    {
        var packed =
            BitConverter.SingleToUInt32Bits(x) | ((ulong)BitConverter.SingleToUInt32Bits(y) << 32);
        AddOp(element, code, ValueKind.F32x2, packed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddU32(Element element, OpCode code, uint value)
    {
        AddOp(element, code, ValueKind.U32, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddU64(Element element, OpCode code, ulong value)
    {
        AddOp(element, code, ValueKind.U64, value);
    }

    /// <summary>
    /// Appends UTF-8 payload bytes to the arena and records an offset/length data operation.
    /// The payload must be non-empty; native validation additionally rejects interior NUL bytes
    /// and invalid UTF-8.
    /// </summary>
    internal static void AddData(Element element, OpCode code, ReadOnlySpan<char> value)
    {
        var (offset, length) = AppendUtf8(element.Arena, value);
        AddOp(element, code, ValueKind.Data, offset, length);
    }

    /// <summary>
    /// Records an offset/length data operation over already encoded UTF-8 bytes.
    /// The bytes are trusted: they must contain valid UTF-8 with no interior NUL.
    /// </summary>
    internal static void AddData(Element element, OpCode code, ReadOnlySpan<byte> value)
    {
        var (offset, length) = AppendUtf8(element.Arena, value);
        AddOp(element, code, ValueKind.Data, offset, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddCallback(Element element, OpCode code, ulong eventToken)
    {
        AddCallback(element, code, eventToken, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddCallback(Element element, OpCode code, ulong eventToken, ulong payload)
    {
        AddOp(element, code, ValueKind.Callback, eventToken, payload);
    }

    internal static Element AppendFragment(
        RenderArena* destination,
        RenderArena* source,
        uint sourceRoot
    )
    {
        if (sourceRoot >= (uint)source->NodeLength)
        {
            throw new InvalidOperationException("The retained fragment root is invalid.");
        }

        EnsureNodes(destination, source->NodeLength);
        EnsureOps(destination, source->OpLength);
        EnsureChildren(destination, source->ChildLength);
        EnsureUtf8(destination, source->Utf8Length);

        var nodeOffset = checked((uint)destination->NodeLength);
        var utf8Offset = checked((uint)destination->Utf8Length);

        for (var index = 0; index < source->NodeLength; index++)
        {
            var node = source->Nodes[index];
            if (node.DataLength != 0)
            {
                node.DataOffset = checked(node.DataOffset + utf8Offset);
            }
            destination->Nodes[destination->NodeLength++] = node;
        }

        for (var index = 0; index < source->OpLength; index++)
        {
            var operation = source->Ops[index];
            operation.Node = checked(operation.Node + nodeOffset);
            if (
                operation.ValueKind == (ushort)ValueKind.Data
                && operation.B != 0
            )
            {
                operation.A = checked(operation.A + utf8Offset);
            }
            destination->Ops[destination->OpLength++] = operation;
        }

        for (var index = 0; index < source->ChildLength; index++)
        {
            var child = source->Children[index];
            child.Parent = checked(child.Parent + nodeOffset);
            child.Child = checked(child.Child + nodeOffset);
            destination->Children[destination->ChildLength++] = child;
        }

        if (source->Utf8Length != 0)
        {
            new ReadOnlySpan<byte>(source->Utf8, source->Utf8Length).CopyTo(
                new Span<byte>(destination->Utf8 + destination->Utf8Length, source->Utf8Length)
            );
            destination->Utf8Length += source->Utf8Length;
        }

        return new Element(destination, checked(sourceRoot + nodeOffset), destination->Generation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddOp(
        Element element,
        OpCode code,
        ValueKind valueKind,
        ulong a,
        ulong b = 0
    )
    {
        ValidateCurrent(element);
        EnsureOps(element.Arena, 1);

        element.Arena->Ops[element.Arena->OpLength++] = new OpRecord
        {
            Node = element.Node,
            Code = (ushort)code,
            ValueKind = (ushort)valueKind,
            A = a,
            B = b,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateCurrent(Element element)
    {
        if (element.Arena == null)
        {
            throw new InvalidOperationException("Default Element cannot be used.");
        }

        if (element.Generation != element.Arena->Generation)
        {
            throw new InvalidOperationException("Element escaped its render generation.");
        }
    }

    private static (uint Offset, uint Length) AppendUtf8(
        RenderArena* arena,
        ReadOnlySpan<char> value
    )
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        EnsureUtf8(arena, byteCount);

        var offset = arena->Utf8Length;
        var destination = new Span<byte>(arena->Utf8 + offset, byteCount);
        var written = Encoding.UTF8.GetBytes(value, destination);
        arena->Utf8Length += written;

        return (checked((uint)offset), checked((uint)written));
    }

    private static (uint Offset, uint Length) AppendUtf8(
        RenderArena* arena,
        ReadOnlySpan<byte> value
    )
    {
        EnsureUtf8(arena, value.Length);

        var offset = arena->Utf8Length;
        value.CopyTo(new Span<byte>(arena->Utf8 + offset, value.Length));
        arena->Utf8Length += value.Length;

        return (checked((uint)offset), checked((uint)value.Length));
    }

    internal static Span<byte> GetWritableUtf8Span(RenderArena* arena, int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        EnsureUtf8(arena, Math.Max(sizeHint, 1));
        return new Span<byte>(
            arena->Utf8 + arena->Utf8Length,
            arena->Utf8Capacity - arena->Utf8Length
        );
    }

    internal static void AdvanceUtf8(RenderArena* arena, int count)
    {
        if (count < 0 || count > arena->Utf8Capacity - arena->Utf8Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        arena->Utf8Length += count;
    }

    private static void EnsureNodes(RenderArena* arena, int additional)
    {
        if (additional <= arena->NodeCapacity - arena->NodeLength)
        {
            return;
        }

        RequestNativeGrowth(
            arena,
            RenderArenaBuffer.Nodes,
            checked(arena->NodeLength + additional)
        );
        arena->Nodes = Grow(arena->Nodes, ref arena->node_capacity, arena->NodeLength, additional);
    }

    private static void EnsureOps(RenderArena* arena, int additional)
    {
        if (additional <= arena->OpCapacity - arena->OpLength)
        {
            return;
        }

        RequestNativeGrowth(
            arena,
            RenderArenaBuffer.Operations,
            checked(arena->OpLength + additional)
        );
        arena->Ops = Grow(arena->Ops, ref arena->op_capacity, arena->OpLength, additional);
    }

    private static void EnsureChildren(RenderArena* arena, int additional)
    {
        if (additional <= arena->ChildCapacity - arena->ChildLength)
        {
            return;
        }

        RequestNativeGrowth(
            arena,
            RenderArenaBuffer.Children,
            checked(arena->ChildLength + additional)
        );
        arena->Children = Grow(
            arena->Children,
            ref arena->child_capacity,
            arena->ChildLength,
            additional
        );
    }

    private static void EnsureUtf8(RenderArena* arena, int additional)
    {
        if (additional <= arena->Utf8Capacity - arena->Utf8Length)
        {
            return;
        }

        RequestNativeGrowth(arena, RenderArenaBuffer.Utf8, checked(arena->Utf8Length + additional));
        arena->Utf8 = Grow(arena->Utf8, ref arena->utf8_capacity, arena->Utf8Length, additional);
    }

    private static void RequestNativeGrowth(
        RenderArena* arena,
        RenderArenaBuffer buffer,
        int requiredCapacity
    )
    {
        if ((arena->Flags & NativeConstants.ArenaFlagNativeOwned) == 0)
        {
            return;
        }

        switch (buffer)
        {
            case RenderArenaBuffer.Nodes:
                arena->RequiredNodeCapacity = Math.Max(
                    arena->RequiredNodeCapacity,
                    requiredCapacity
                );
                break;
            case RenderArenaBuffer.Operations:
                arena->RequiredOpCapacity = Math.Max(arena->RequiredOpCapacity, requiredCapacity);
                break;
            case RenderArenaBuffer.Children:
                arena->RequiredChildCapacity = Math.Max(
                    arena->RequiredChildCapacity,
                    requiredCapacity
                );
                break;
            case RenderArenaBuffer.Utf8:
                arena->RequiredUtf8Capacity = Math.Max(
                    arena->RequiredUtf8Capacity,
                    requiredCapacity
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buffer));
        }

        throw new RenderArenaGrowthRequiredException();
    }

    private static T* Grow<T>(T* pointer, ref int capacity, int length, int additional)
        where T : unmanaged
    {
        var required = checked(length + additional);
        var newCapacity = capacity;
        while (newCapacity < required)
        {
            newCapacity = checked(newCapacity * 2);
        }

        var newPointer = (T*)
            NativeMemory.Realloc(pointer, checked((nuint)newCapacity * (nuint)sizeof(T)));

        if (newPointer == null)
        {
            throw new OutOfMemoryException();
        }

        capacity = newCapacity;
        return newPointer;
    }
}

internal enum RenderArenaBuffer
{
    Nodes,
    Operations,
    Children,
    Utf8,
}

internal sealed class RenderArenaGrowthRequiredException : Exception { }
