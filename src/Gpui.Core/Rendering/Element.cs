using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>
/// A declaration in one render arena generation. Contains a managed owner reference;
/// supports span composition but cannot be stackalloc'd or reused after arena reset/disposal.
/// </summary>
public readonly unsafe struct Element
{
    internal readonly RenderArenaOwner? Owner;
    internal RenderArena* Arena => Owner is null ? null : Owner.GetArena(Generation);
    internal readonly uint Node;
    internal readonly uint Generation;

    internal Element(RenderArenaOwner owner, uint node, uint generation)
    {
        Owner = owner;
        Node = node;
        Generation = generation;
    }

    public bool IsDefault => Owner is null;

    internal RenderArenaOwner.AccessScope Access()
    {
        if (Owner is null)
            throw new InvalidOperationException("Default Element cannot be used.");
        if (Node >= (uint)Owner.GetArena(Generation)->NodeLength)
            throw new InvalidOperationException("Element node is outside the node arena.");
        return Owner.Access(Generation);
    }
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
