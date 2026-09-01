namespace Gpui;

/// <summary>Placement of overlay content inside the window viewport.</summary>
public enum OverlayPlacement : uint
{
    Center = 0,
    Top = 1,
    TopRight = 2,
    Right = 3,
    BottomRight = 4,
    Bottom = 5,
    BottomLeft = 6,
    Left = 7,
    TopLeft = 8,
}

/// <summary>Window edge used by the semantic <see cref="RenderContext.Sheet"/> composition.</summary>
public enum SheetSide : uint
{
    Right = 0,
    Left = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>Window overlay behavior and presentation.</summary>
public readonly struct OverlayOptions
{
    private const uint DefaultPriority = 10;
    private const float DefaultMargin = 16;
    private const uint DefaultBackdropRgba = 0x00000060;
    private readonly bool _initialized;

    public OverlayOptions(
        OverlayPlacement placement = OverlayPlacement.Center,
        bool modal = true,
        bool dismissOnBackdrop = true,
        bool dismissOnEscape = true,
        uint priority = DefaultPriority,
        float margin = DefaultMargin,
        Color backdrop = default
    )
    {
        if ((uint)placement > (uint)OverlayPlacement.TopLeft)
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }
        if (!float.IsFinite(margin) || margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }
        Placement = placement;
        Modal = modal;
        DismissOnBackdrop = dismissOnBackdrop;
        DismissOnEscape = dismissOnEscape;
        Priority = priority;
        Margin = margin;
        Backdrop = backdrop;
        _initialized = true;
    }

    public OverlayPlacement Placement { get; }
    public bool Modal { get; }
    public bool DismissOnBackdrop { get; }
    public bool DismissOnEscape { get; }
    public uint Priority { get; }
    public float Margin { get; }
    public Color Backdrop { get; }

    internal OverlayPlacement EffectivePlacement =>
        _initialized ? Placement : OverlayPlacement.Center;
    internal bool EffectiveModal => !_initialized || Modal;
    internal bool EffectiveDismissOnBackdrop => !_initialized || DismissOnBackdrop;
    internal bool EffectiveDismissOnEscape => !_initialized || DismissOnEscape;
    internal uint EffectivePriority => _initialized ? Priority : DefaultPriority;
    internal float EffectiveMargin => _initialized ? Margin : DefaultMargin;
    internal Color EffectiveBackdrop => _initialized ? Backdrop : new Color(DefaultBackdropRgba);
}
