using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a retained native scroll container. Wheel/trackpad scrolling stays entirely in
    /// Rust; the resource is scoped by the current managed View handle plus <paramref name="key"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis = ScrollAxis.Vertical,
        params ReadOnlySpan<Element> children
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A scroll resource key cannot be empty.", nameof(key));
        }
        if ((uint)axis > (uint)ScrollAxis.Both)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        var element = ArenaWriter.AddNode<ScrollTag>(_arena, ComponentId.Scroll, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ScrollAxis, (uint)axis);
        ArenaWriter.AddChildren(element.Inner, children);
        return element;
    }

    /// <summary>
    /// Declares a variable-height virtualized list. GPUI retains measurements and requests managed
    /// rows in coarse batches. The renderer must be generated from a [GpuiListItem] method.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ReadOnlySpan<char> key,
        int itemCount,
        ListItemRenderer renderer,
        ListOptions options = default
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A list resource key cannot be empty.", nameof(key));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        if (renderer.IsDefault)
        {
            throw new ArgumentException("A generated list renderer is required.", nameof(renderer));
        }
        if ((uint)options.EffectiveAlignment > (uint)ListAlignment.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var element = ArenaWriter.AddNode<ListTag>(_arena, ComponentId.List, key);
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ListItemCount, checked((uint)itemCount));
        ArenaWriter.AddCallback(element.Inner, OpCode.ListRenderer, renderer.Token);
        ArenaWriter.AddU32(element.Inner, OpCode.ListBatchSize, checked((uint)options.EffectiveBatchSize));
        ArenaWriter.AddF32(element.Inner, OpCode.ListOverdrawPx, options.EffectiveOverdraw);
        ArenaWriter.AddU32(element.Inner, OpCode.ListAlignment, (uint)options.EffectiveAlignment);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CurrentResourceOwner()
    {
        var owner = _owner ?? throw new InvalidOperationException(
            "Scroll/List resources require a mounted managed View. Element-only RenderArenaOwner tests cannot create retained native resources."
        );
        var handle = owner.RuntimeViewHandle;
        return handle != 0
            ? handle
            : throw new InvalidOperationException("The owning View is not mounted.");
    }
}
