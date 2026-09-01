namespace Gpui.Interop;

internal static class NativeConstants
{
    internal const uint AbiVersion = 6;
    internal const ulong SchemaHash = SemanticRegistry.SchemaHash;
    internal const uint ArenaFlagNativeOwned = 1;
    internal const int RenderGrowRequired = 1;
}

// Pascal-cased managed conveniences over the csbindgen-owned ABI fields.
internal unsafe partial struct RenderArena
{
    internal NodeRecord* Nodes
    {
        get => nodes;
        set => nodes = value;
    }
    internal int NodeLength
    {
        get => node_length;
        set => node_length = value;
    }
    internal int NodeCapacity
    {
        get => node_capacity;
        set => node_capacity = value;
    }
    internal OpRecord* Ops
    {
        get => ops;
        set => ops = value;
    }
    internal int OpLength
    {
        get => op_length;
        set => op_length = value;
    }
    internal int OpCapacity
    {
        get => op_capacity;
        set => op_capacity = value;
    }
    internal ChildRecord* Children
    {
        get => children;
        set => children = value;
    }
    internal int ChildLength
    {
        get => child_length;
        set => child_length = value;
    }
    internal int ChildCapacity
    {
        get => child_capacity;
        set => child_capacity = value;
    }
    internal byte* Utf8
    {
        get => utf8;
        set => utf8 = value;
    }
    internal int Utf8Length
    {
        get => utf8_length;
        set => utf8_length = value;
    }
    internal int Utf8Capacity
    {
        get => utf8_capacity;
        set => utf8_capacity = value;
    }
    internal uint Generation
    {
        get => generation;
        set => generation = value;
    }
    internal uint Flags
    {
        get => flags;
        set => flags = value;
    }
    internal int RequiredNodeCapacity
    {
        get => required_node_capacity;
        set => required_node_capacity = value;
    }
    internal int RequiredOpCapacity
    {
        get => required_op_capacity;
        set => required_op_capacity = value;
    }
    internal int RequiredChildCapacity
    {
        get => required_child_capacity;
        set => required_child_capacity = value;
    }
    internal int RequiredUtf8Capacity
    {
        get => required_utf8_capacity;
        set => required_utf8_capacity = value;
    }
}

internal partial struct NodeRecord
{
    internal ushort Component
    {
        get => component;
        set => component = value;
    }
    internal ushort Flags
    {
        get => flags;
        set => flags = value;
    }
    internal uint DataOffset
    {
        get => data_offset;
        set => data_offset = value;
    }
    internal uint DataLength
    {
        get => data_length;
        set => data_length = value;
    }
}

internal partial struct OpRecord
{
    internal uint Node
    {
        get => node;
        set => node = value;
    }
    internal ushort Code
    {
        get => code;
        set => code = value;
    }
    internal ushort ValueKind
    {
        get => value_kind;
        set => value_kind = value;
    }
    internal ulong A
    {
        get => a;
        set => a = value;
    }
    internal ulong B
    {
        get => b;
        set => b = value;
    }
}

internal partial struct ChildRecord
{
    internal uint Parent
    {
        get => parent;
        set => parent = value;
    }
    internal uint Child
    {
        get => child;
        set => child = value;
    }
}
