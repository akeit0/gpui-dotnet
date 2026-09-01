namespace Gpui;

/// <summary>Horizontal alignment of a table column's header text and cell content.</summary>
public enum TableColumnAlignment : byte
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>Whether a table column's width is fixed pixels or a fraction of the table width.</summary>
public enum TableColumnWidth : byte
{
    Pixels = 0,
    Fraction = 1,
}

/// <summary>
/// Declares one table column. Columns are declarative IR: the native side renders the header
/// strip and reconciles every <c>ui.TableCell</c> against the same width and alignment, so
/// header and row geometry always agree. Keys must be unique within a table and must not
/// contain control characters.
/// </summary>
public readonly record struct TableColumn(
    string Key,
    string Header,
    float Width,
    TableColumnWidth Unit = TableColumnWidth.Pixels,
    TableColumnAlignment Alignment = TableColumnAlignment.Left
)
{
    internal bool IsFraction => Unit == TableColumnWidth.Fraction;
    internal int AlignmentCode => (int)Alignment;
}

/// <summary>Native virtualization and presentation options for a table.</summary>
public readonly struct TableOptions
{
    private readonly bool _initialized;

    public TableOptions(
        int batchSize = 48,
        float overdraw = 240,
        float estimatedItemHeight = 40,
        bool showHeader = true,
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
        if (!float.IsFinite(estimatedItemHeight) || estimatedItemHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedItemHeight));
        }
        ScrollOptions.ValidateScrollbarWidth(scrollbarWidth);

        BatchSize = batchSize;
        Overdraw = overdraw;
        EstimatedItemHeight = estimatedItemHeight;
        ShowHeader = showHeader;
        SmoothScrolling = smoothScrolling;
        ShowScrollbar = showScrollbar;
        ScrollbarGutter = scrollbarGutter;
        ScrollbarWidth = scrollbarWidth;
        _initialized = true;
    }

    public int BatchSize { get; }
    public float Overdraw { get; }
    public float EstimatedItemHeight { get; }
    public bool ShowHeader { get; }
    public bool SmoothScrolling { get; }
    public bool ShowScrollbar { get; }
    public bool ScrollbarGutter { get; }
    public float ScrollbarWidth { get; }

    internal int EffectiveBatchSize => _initialized ? BatchSize : 48;
    internal float EffectiveOverdraw => _initialized ? Overdraw : 240;
    internal float EffectiveEstimatedItemHeight => _initialized ? EstimatedItemHeight : 40;
    internal bool EffectiveShowHeader => !_initialized || ShowHeader;
    internal bool EffectiveSmoothScrolling => !_initialized || SmoothScrolling;
    internal bool EffectiveShowScrollbar => !_initialized || ShowScrollbar;
    internal bool EffectiveScrollbarGutter => _initialized && ScrollbarGutter;
    internal float EffectiveScrollbarWidth => _initialized ? ScrollbarWidth : 8;
}
