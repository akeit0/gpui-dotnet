using System.Text;

namespace Gpui;

/// <summary>Logical list growth direction.</summary>
public enum ListAlignment : uint
{
    Top = 0,
    Bottom = 1,
}

/// <summary>
/// Declares the current shape and content identity of a virtualized datasource. Keep
/// <see cref="ContentRevision"/> stable across unrelated View renders and change it whenever
/// cached row output may have changed.
/// </summary>
public readonly struct ListDataSource
{
    public ListDataSource(int count, ulong contentRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
        ContentRevision = contentRevision;
    }

    public int Count { get; }
    public ulong ContentRevision { get; }
}

/// <summary>
/// Native virtualization options. List rows may have different heights; GPUI measures visible
/// rows and retains those measurements. Managed rendering is requested in coarse batches.
/// Options that equal their defaults are not written into the render arena at all; native
/// applies the same defaults when an option is absent.
/// </summary>
public readonly struct ListOptions
{
    private readonly bool _initialized;

    public ListOptions(
        int batchSize = 48,
        float overdraw = 240,
        ListAlignment alignment = ListAlignment.Top,
        float estimatedItemHeight = 40,
        bool smoothScrolling = true,
        bool showScrollbar = true,
        bool scrollbarGutter = false,
        float scrollbarWidth = 8
    )
    {
        if (batchSize is < 1 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be 1..512.");
        }
        if (!float.IsFinite(overdraw) || overdraw < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overdraw));
        }
        if ((uint)alignment > (uint)ListAlignment.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        if (!float.IsFinite(estimatedItemHeight) || estimatedItemHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedItemHeight));
        }
        ScrollOptions.ValidateScrollbarWidth(scrollbarWidth);
        BatchSize = batchSize;
        Overdraw = overdraw;
        Alignment = alignment;
        EstimatedItemHeight = estimatedItemHeight;
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        ScrollbarGutter = scrollbarGutter;
        ScrollbarWidth = scrollbarWidth;
        _initialized = true;
    }

    public int BatchSize { get; }
    public float Overdraw { get; }
    public ListAlignment Alignment { get; }
    public float EstimatedItemHeight { get; }
    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }
    public bool ScrollbarGutter { get; }
    public float ScrollbarWidth { get; }

    internal int EffectiveBatchSize => _initialized ? BatchSize : 48;
    internal float EffectiveOverdraw => _initialized ? Overdraw : 240;
    internal ListAlignment EffectiveAlignment => _initialized ? Alignment : ListAlignment.Top;
    internal float EffectiveEstimatedItemHeight => _initialized ? EstimatedItemHeight : 40;
    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
    internal bool EffectiveScrollbarGutter => _initialized && ScrollbarGutter;
    internal float EffectiveScrollbarWidth => _initialized ? ScrollbarWidth : 8;
}

/// <summary>
/// Optional imperative handle for a virtualized list declared by the same View with ui.List().
/// Use Splice when item identity/order changes so native measurements can be retained precisely.
/// A default controller is assigned a stable key when passed by reference to ui.List() or ui.Table().
/// </summary>
public readonly struct ListController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal ListController(ViewBase owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    internal ListController(ViewBase owner, ReadOnlySpan<byte> utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key.ToArray();
    }

    /// <summary>True once this controller has been bound to a resource.</summary>
    public bool IsBound => _utf8Key is not null;

    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;

    public bool IsDefault => _owner is null;

    public void ScrollToItem(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.List,
                ResourceCommandKind.ListScrollToItem,
                null,
                checked((uint)index),
                0,
                Utf8Key: Utf8KeyArray
            )
        );
    }

    public void Splice(int start, int removedCount, int insertedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(removedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(insertedCount);
        var packed = ((ulong)checked((uint)removedCount) << 32) | checked((uint)insertedCount);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.List,
                ResourceCommandKind.ListSplice,
                null,
                checked((uint)start),
                packed,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    /// <summary>
    /// Invalidates a stable item range without changing item count. Use this when row content or
    /// height changes and GPUI must discard measurements for the affected items.
    /// </summary>
    public void Refresh(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        DispatchRefresh(start, count);
        Owner.InvalidateFromController();
    }

    /// <summary>
    /// Invalidates multiple stable item ranges with one managed update. Each range is queued as
    /// a standard Refresh command; native commits the whole queued sequence in one pass, so
    /// cached batches and measurements are discarded only where the ranges intersect. Ranges use
    /// the item indices of the snapshot that carries them, exactly like <see cref="Refresh"/>.
    /// </summary>
    public void RefreshRanges(params ReadOnlySpan<(int Start, int Count)> ranges)
    {
        if (ranges.Length == 0)
        {
            throw new ArgumentException("At least one refresh range is required.", nameof(ranges));
        }
        foreach (var (start, count) in ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
        }
        foreach (var (start, count) in ranges)
        {
            DispatchRefresh(start, count);
        }
        Owner.InvalidateFromController();
    }

    private void DispatchRefresh(int start, int count) =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.List,
                ResourceCommandKind.ListRefresh,
                null,
                checked((uint)start),
                checked((uint)count),
                Utf8Key: Utf8KeyArray
            )
        );

    public void Reset(int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.List,
                ResourceCommandKind.ListReset,
                null,
                checked((uint)itemCount),
                0,
                Utf8Key: Utf8KeyArray
            )
        );
        Owner.InvalidateFromController();
    }

    private ViewBase Owner =>
        _owner ?? throw new InvalidOperationException("Default ListController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default ListController cannot be used.");
}
