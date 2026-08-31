using System.Text;

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

/// <summary>Native scrolling behavior and presentation.</summary>
public readonly struct ScrollOptions
{
    private readonly bool _initialized;

    public ScrollOptions(
        bool smoothScrolling = true,
        bool showScrollbar = true,
        bool scrollbarGutter = false,
        float scrollbarWidth = 8
    )
    {
        ValidateScrollbarWidth(scrollbarWidth);
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        ScrollbarGutter = scrollbarGutter;
        ScrollbarWidth = scrollbarWidth;
        _initialized = true;
    }

    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }
    public bool ScrollbarGutter { get; }
    public float ScrollbarWidth { get; }

    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
    internal bool EffectiveScrollbarGutter => _initialized && ScrollbarGutter;
    internal float EffectiveScrollbarWidth => _initialized ? ScrollbarWidth : 8;

    internal static void ValidateScrollbarWidth(float width)
    {
        if (!float.IsFinite(width) || width is < 3f or > 32f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScrollbarWidth),
                "Scrollbar width must be finite and between 3 and 32 px."
            );
        }
    }
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
/// Optional imperative handle for a scroll resource declared by the same View with ui.Scroll().
/// Most scroll containers do not need a controller.
/// </summary>
public readonly struct ScrollController
{
    private readonly View? _owner;
    private readonly byte[]? _utf8Key;

    internal ScrollController(View owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    public bool IsDefault => _owner is null;

    /// <summary>Scrolls to a positive content-space offset from the top-left corner.</summary>
    public void ScrollTo(float x, float y)
    {
        if (!float.IsFinite(x) || x < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Scroll position must be finite and non-negative."
            );
        }
        if (!float.IsFinite(y) || y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                "Scroll position must be finite and non-negative."
            );
        }
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToOffset,
                null,
                BitConverter.SingleToUInt32Bits(x),
                BitConverter.SingleToUInt32Bits(y),
                Utf8Key: Utf8KeyArray
            )
        );
    }

    public void ScrollToTop() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToTop,
                null,
                0,
                0,
                Utf8Key: Utf8KeyArray
            )
        );

    public void ScrollToBottom() =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Scroll,
                ResourceCommandKind.ScrollToBottom,
                null,
                0,
                0,
                Utf8Key: Utf8KeyArray
            )
        );

    private View Owner =>
        _owner ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default ScrollController cannot be used.");
}

/// <summary>
/// Optional imperative handle for a virtualized list declared by the same View with ui.List().
/// Use Splice when item identity/order changes so native measurements can be retained precisely.
/// </summary>
public readonly struct ListController
{
    private readonly View? _owner;
    private readonly byte[]? _utf8Key;

    internal ListController(View owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

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

    private View Owner =>
        _owner ?? throw new InvalidOperationException("Default ListController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default ListController cannot be used.");
}

/// <summary>
/// Imperative handle for a retained native input declared by the same View with ui.Input().
/// Native user edits do not require controller calls.
/// </summary>
public readonly struct InputController
{
    private readonly View? _owner;
    private readonly byte[]? _utf8Key;

    internal InputController(View owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    internal InputController(View owner, ReadOnlySpan<byte> utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key.ToArray();
    }

    public bool IsDefault => _owner is null;

    public void Focus() => Dispatch(ResourceCommandKind.InputFocus);

    public void Blur() => Dispatch(ResourceCommandKind.InputBlur);

    public void SelectAll() => Dispatch(ResourceCommandKind.InputSelectAll);

    /// <summary>Replaces the native value and moves the caret to its end.</summary>
    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Input,
                ResourceCommandKind.InputSetValue,
                null,
                0,
                0,
                value,
                Utf8KeyArray
            )
        );
    }

    /// <summary>
    /// Replaces the native value from UTF-8 and moves the caret to its end. The bytes are
    /// trusted: they must contain valid UTF-8 with no interior NUL, and are copied so the
    /// command remains safe after this call returns.
    /// </summary>
    public void SetValue(ReadOnlySpan<byte> utf8Value)
    {
        Owner.DispatchUtf8InputValue(Utf8KeyArray, utf8Value);
    }

    private void Dispatch(ResourceCommandKind command) =>
        Owner.DispatchResourceCommand(
            new ResourceCommand(ResourceKind.Input, command, null, 0, 0, null, Utf8KeyArray)
        );

    private View Owner =>
        _owner ?? throw new InvalidOperationException("Default InputController cannot be used.");
    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default InputController cannot be used.");
}

internal enum ResourceKind : ushort
{
    Scroll = 1,
    List = 2,
    Input = 3,
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
    InputFocus = 20,
    InputBlur = 21,
    InputSetValue = 22,
    InputSelectAll = 23,
}

internal readonly record struct ResourceCommand(
    ResourceKind ResourceKind,
    ResourceCommandKind Command,
    string? Key,
    ulong A,
    ulong B,
    string? Data = null,
    byte[]? Utf8Key = null
);
