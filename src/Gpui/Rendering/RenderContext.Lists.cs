using System.Runtime.CompilerServices;
using Gpui.Interop;

namespace Gpui;

public readonly unsafe ref partial struct RenderContext
{
    /// <summary>
    /// Declares a variable-height virtualized list. GPUI retains measurements and requests managed
    /// rows in coarse batches. The renderer must be generated from a [GpuiListItem] method.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ref ListController controller,
        int itemCount,
        ListItemRenderer renderer,
        ListOptions options = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        BindAutoController(ref controller);
        return ListCore(controller.Utf8KeySpan, itemCount, null, renderer, options);
    }

    /// <summary>
    /// Declares a variable-height virtualized list bound to a controller-owned retained key.
    /// The controller receives its per-view key on the first render and reuses it thereafter.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Element<ListTag> List(
        ref ListController controller,
        ListDataSource dataSource,
        ListItemRenderer renderer,
        ListOptions options = default
    )
    {
        BindAutoController(ref controller);
        return ListCore(
            controller.Utf8KeySpan,
            dataSource.Count,
            dataSource.ContentRevision,
            renderer,
            options,
            dataSource.ProjectionRevision
        );
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
    ) =>
        ListCore(
            key,
            dataSource.Count,
            dataSource.ContentRevision,
            renderer,
            options,
            dataSource.ProjectionRevision
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<ListTag> ListCore(
        ReadOnlySpan<char> key,
        int itemCount,
        ulong? contentRevision,
        ListItemRenderer renderer,
        ListOptions options,
        ulong? projectionRevision = null
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A list resource key cannot be empty.", nameof(key));
        }
        ResourceKeys.ValidateExplicitChars(key, nameof(key));
        ValidateListArguments(renderer, options);

        var element = ArenaWriter.AddNode<ListTag>(_arena, ComponentId.List, key);
        return ConfigureList(
            element,
            itemCount,
            contentRevision,
            renderer,
            options,
            projectionRevision
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<ListTag> ListCore(
        ReadOnlySpan<byte> key,
        int itemCount,
        ulong? contentRevision,
        ListItemRenderer renderer,
        ListOptions options,
        ulong? projectionRevision = null
    )
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("A list resource key cannot be empty.", nameof(key));
        }
        ValidateListArguments(renderer, options);

        var element = ArenaWriter.AddNode<ListTag>(_arena, ComponentId.List, key);
        return ConfigureList(
            element,
            itemCount,
            contentRevision,
            renderer,
            options,
            projectionRevision
        );
    }

    private static void ValidateListArguments(ListItemRenderer renderer, ListOptions options)
    {
        if (renderer.IsDefault)
        {
            throw new ArgumentException("A generated list renderer is required.", nameof(renderer));
        }
        if ((uint)options.EffectiveAlignment > (uint)ListAlignment.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Element<ListTag> ConfigureList(
        Element<ListTag> element,
        int itemCount,
        ulong? contentRevision,
        ListItemRenderer renderer,
        ListOptions options,
        ulong? projectionRevision
    )
    {
        ArenaWriter.AddU32(element.Inner, OpCode.ResourceOwner, CurrentResourceOwner());
        ArenaWriter.AddU32(element.Inner, OpCode.ListItemCount, checked((uint)itemCount));
        ArenaWriter.AddCallback(element.Inner, OpCode.ListRenderer, renderer.Token);
        // As with Scroll, every option below has a native default; default-valued options are
        // omitted so the common list declares only owner, count, renderer, and revision.
        if (options.EffectiveBatchSize != 48)
        {
            ArenaWriter.AddU32(
                element.Inner,
                OpCode.ListBatchSize,
                checked((uint)options.EffectiveBatchSize)
            );
        }
        if (options.EffectiveOverdraw != 240)
        {
            ArenaWriter.AddF32(element.Inner, OpCode.ListOverdrawPx, options.EffectiveOverdraw);
        }
        if (options.EffectiveAlignment != ListAlignment.Top)
        {
            ArenaWriter.AddU32(
                element.Inner,
                OpCode.ListAlignment,
                (uint)options.EffectiveAlignment
            );
        }
        if (options.EffectiveEstimatedItemHeight != 40)
        {
            ArenaWriter.AddF32(
                element.Inner,
                OpCode.ListEstimatedItemHeightPx,
                options.EffectiveEstimatedItemHeight
            );
        }
        if (contentRevision is { } revision)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.ListContentRevision, revision);
        }
        if (projectionRevision is { } projection)
        {
            ArenaWriter.AddU64(element.Inner, OpCode.ListProjectionRevision, projection);
        }
        if (!options.EffectiveSmoothScrolling)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.SmoothScroll, 0);
        }
        if (!options.EffectiveShowScrollbar)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ShowScrollbar, 0);
        }
        if (options.EffectiveScrollbarGutter)
        {
            ArenaWriter.AddU32(element.Inner, OpCode.ScrollbarGutter, 1);
        }
        if (options.EffectiveScrollbarWidth != 8)
        {
            ArenaWriter.AddF32(
                element.Inner,
                OpCode.ScrollbarWidth,
                options.EffectiveScrollbarWidth
            );
        }
        return element;
    }
}
