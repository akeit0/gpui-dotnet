namespace Gpui;

/// <summary>Preferred side of a tooltip relative to its trigger.</summary>
public enum TooltipPlacement : uint
{
    Auto = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
    Left = 4,
}

/// <summary>Alignment of a tooltip along the trigger's cross axis.</summary>
public enum TooltipAlignment : uint
{
    Start = 0,
    Center = 1,
    End = 2,
}

/// <summary>Native tooltip timing and viewport-aware placement.</summary>
public readonly struct TooltipOptions
{
    private const uint DefaultShowDelayMilliseconds = 500;
    private const uint DefaultHideDelayMilliseconds = 300;
    private const float DefaultGap = 8;
    private const float DefaultMargin = 8;
    private readonly bool _initialized;

    public TooltipOptions(
        TooltipPlacement placement = TooltipPlacement.Auto,
        TooltipAlignment alignment = TooltipAlignment.Center,
        TimeSpan? showDelay = null,
        TimeSpan? hideDelay = null,
        float gap = DefaultGap,
        float margin = DefaultMargin
    )
    {
        if ((uint)placement > (uint)TooltipPlacement.Left)
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }
        if ((uint)alignment > (uint)TooltipAlignment.End)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        var resolvedShowDelay =
            showDelay ?? TimeSpan.FromMilliseconds(DefaultShowDelayMilliseconds);
        var resolvedHideDelay =
            hideDelay ?? TimeSpan.FromMilliseconds(DefaultHideDelayMilliseconds);
        if (
            resolvedShowDelay < TimeSpan.Zero
            || resolvedShowDelay.TotalMilliseconds > uint.MaxValue
        )
        {
            throw new ArgumentOutOfRangeException(nameof(showDelay));
        }
        if (
            resolvedHideDelay < TimeSpan.Zero
            || resolvedHideDelay.TotalMilliseconds > uint.MaxValue
        )
        {
            throw new ArgumentOutOfRangeException(nameof(hideDelay));
        }
        if (!float.IsFinite(gap) || gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }
        if (!float.IsFinite(margin) || margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin));
        }

        Placement = placement;
        Alignment = alignment;
        ShowDelay = resolvedShowDelay;
        HideDelay = resolvedHideDelay;
        Gap = gap;
        Margin = margin;
        _initialized = true;
    }

    public TooltipPlacement Placement { get; }
    public TooltipAlignment Alignment { get; }
    public TimeSpan ShowDelay { get; }
    public TimeSpan HideDelay { get; }
    public float Gap { get; }
    public float Margin { get; }

    internal TooltipPlacement EffectivePlacement =>
        _initialized ? Placement : TooltipPlacement.Auto;
    internal TooltipAlignment EffectiveAlignment =>
        _initialized ? Alignment : TooltipAlignment.Center;
    internal uint EffectiveShowDelayMilliseconds =>
        !_initialized ? DefaultShowDelayMilliseconds : checked((uint)ShowDelay.TotalMilliseconds);
    internal uint EffectiveHideDelayMilliseconds =>
        !_initialized ? DefaultHideDelayMilliseconds : checked((uint)HideDelay.TotalMilliseconds);
    internal float EffectiveGap => _initialized ? Gap : DefaultGap;
    internal float EffectiveMargin => _initialized ? Margin : DefaultMargin;
}
