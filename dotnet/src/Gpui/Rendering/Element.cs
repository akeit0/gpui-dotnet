using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe struct Element
{
    internal readonly RenderArena* Arena;
    internal readonly uint Node;
    internal readonly uint Generation;

    internal Element(RenderArena* arena, uint node, uint generation)
    {
        Arena = arena;
        Node = node;
        Generation = generation;
    }

    public bool IsDefault => Arena == null;
}

// One lightweight tag generic gives component-specific API without creating a
// deeply generic SwiftUI-style type tree.
public readonly unsafe struct Element<TTag>
    where TTag : unmanaged
{
    internal readonly Element Inner;

    internal Element(Element inner) => Inner = inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Element(Element<TTag> value) => value.Inner;
}
