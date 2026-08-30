namespace Gpui;

/// <summary>Scroll direction for a retained native scroll container.</summary>
public enum ScrollAxis : uint
{
    Vertical = 0,
    Horizontal = 1,
    Both = 2,
}

/// <summary>Logical list growth direction.</summary>
public enum ListAlignment : uint
{
    Top = 0,
    Bottom = 1,
}

/// <summary>Native scrolling behavior and presentation.</summary>
public readonly struct ScrollOptions
{
    private readonly bool _initialized;

    public ScrollOptions(bool smoothScrolling = true, bool showScrollbar = true)
    {
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        _initialized = true;
    }

    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }

    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
}

/// <summary>
/// Native virtualization options. List rows may have different heights; GPUI measures visible
/// rows and retains those measurements. Managed rendering is requested in coarse batches.
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
        bool showScrollbar = true
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
        BatchSize = batchSize;
        Overdraw = overdraw;
        Alignment = alignment;
        EstimatedItemHeight = estimatedItemHeight;
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        _initialized = true;
    }

    public int BatchSize { get; }
    public float Overdraw { get; }
    public ListAlignment Alignment { get; }
    public float EstimatedItemHeight { get; }
    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }

    internal int EffectiveBatchSize => _initialized ? BatchSize : 48;
    internal float EffectiveOverdraw => _initialized ? Overdraw : 240;
    internal ListAlignment EffectiveAlignment => _initialized ? Alignment : ListAlignment.Top;
    internal float EffectiveEstimatedItemHeight => _initialized ? EstimatedItemHeight : 40;
    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
}

/// <summary>
/// Optional imperative handle for a scroll resource declared by the same View with ui.Scroll().
/// Most scroll containers do not need a controller.
/// </summary>
public readonly struct ScrollController
{
    private readonly View? _owner;
    private readonly string? _key;

    internal ScrollController(View owner, string key)
    {
        _owner = owner;
        _key = key;
    }

    public bool IsDefault => _owner is null;

    /// <summary>Scrolls to a positive content-space offset from the top-left corner.</summary>
    public void ScrollTo(float x, float y)
    {
        if (!float.IsFinite(x) || x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Scroll position must be finite and non-negative.");
        }
        if (!float.IsFinite(y) || y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Scroll position must be finite and non-negative.");
        }
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToOffset,
                Key,
                BitConverter.SingleToUInt32Bits(x),
                BitConverter.SingleToUInt32Bits(y)
            )
        );
    }

    public void ScrollToTop() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.Scroll, ResourceCommandKind.ScrollToTop, Key, 0, 0)
        );

    public void ScrollToBottom() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.Scroll, ResourceCommandKind.ScrollToBottom, Key, 0, 0)
        );

    private View Owner => _owner ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
    private string Key => _key ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
}

/// <summary>
/// Optional imperative handle for a virtualized list declared by the same View with ui.List().
/// Use Splice when item identity/order changes so native measurements can be retained precisely.
/// </summary>
public readonly struct ListController
{
    private readonly View? _owner;
    private readonly string? _key;

    internal ListController(View owner, string key)
    {
        _owner = owner;
        _key = key;
    }

    public bool IsDefault => _owner is null;

    public void ScrollToItem(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.List, ResourceCommandKind.ListScrollToItem, Key, checked((uint)index), 0)
        );
    }

    public void Splice(int start, int removedCount, int insertedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(removedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(insertedCount);
        var packed = ((ulong)checked((uint)removedCount) << 32) | checked((uint)insertedCount);
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.List, ResourceCommandKind.ListSplice, Key, checked((uint)start), packed)
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
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.List,
                ResourceCommandKind.ListRefresh,
                Key,
                checked((uint)start),
                checked((uint)count)
            )
        );
        Owner.InvalidateFromController();
    }

    public void Reset(int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.List, ResourceCommandKind.ListReset, Key, checked((uint)itemCount), 0)
        );
        Owner.InvalidateFromController();
    }

    private View Owner => _owner ?? throw new InvalidOperationException("Default ListController cannot be used.");
    private string Key => _key ?? throw new InvalidOperationException("Default ListController cannot be used.");
}

internal enum ResourceKind : ushort
{
    Scroll = 1,
    List = 2,
}

internal enum ResourceCommandKind : ushort
{
    ScrollToOffset = 1,
    ScrollToTop = 2,
    ScrollToBottom = 3,
    ListScrollToItem = 10,
    ListSplice = 11,
    ListReset = 12,
    ListRefresh = 13,
}

internal readonly record struct ResourceCommand(
    ResourceKind ResourceKind,
    ResourceCommandKind Command,
    string Key,
    ulong A,
    ulong B
);
