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
    ) => Scroll(key, axis, default, children);

    /// <summary>
    /// Declares a vertical retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollOptions options,
        params ReadOnlySpan<Element> children
    ) => Scroll(key, ScrollAxis.Vertical, options, children);

    /// <summary>
    /// Declares a retained native scroll container with explicit scrolling behavior.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ScrollTag> Scroll(
        ReadOnlySpan<char> key,
        ScrollAxis axis,
        ScrollOptions options,
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
        ArenaWriter.AddU32(element.Inner, OpCode.SmoothScroll, options.EffectiveSmoothScrolling ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.ShowScrollbar, options.EffectiveShowScrollbar ? 1u : 0u);
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
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        return ListCore(key, itemCount, null, renderer, options);
    }

    /// <summary>
    /// Declares a variable-height virtualized list with explicit datasource content identity.
    /// Native row batches survive unrelated managed renders while the revision is unchanged.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ReadOnlySpan<char> key,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        ListOptions options = default
    ) => ListCore(key, dataSource.Count, dataSource.ContentRevision, renderer, options);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<ListTag> ListCore(
        ReadOnlySpan<char> key,
        int itemCount,
        ulong? contentRevision,
        ListItemRenderer renderer,
        ListOptions options
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A list resource key cannot be empty.", nameof(key));
        }
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
        ArenaWriter.AddF32(element.Inner, OpCode.ListEstimatedItemHeightPx, options.EffectiveEstimatedItemHeight);
        if (contentRevision is { } revision)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.ListContentRevision, revision);
        }
        ArenaWriter.AddU32(element.Inner, OpCode.SmoothScroll, options.EffectiveSmoothScrolling ? 1u : 0u);
        ArenaWriter.AddU32(element.Inner, OpCode.ShowScrollbar, options.EffectiveShowScrollbar ? 1u : 0u);
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint CurrentResourceOwner()
    {
        var owner = _owner ?? throw new InvalidOperationException(
            "Retained native resources require a mounted managed View. Element-only RenderArenaOwner tests cannot create them."
        );
        var handle = owner.RuntimeViewHandle;
        return handle != 0
            ? handle
            : throw new InvalidOperationException("The owning View is not mounted.");
    }
}
