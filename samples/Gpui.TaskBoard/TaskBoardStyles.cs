using static Gpui.Units;

namespace Gpui;

/// <summary>
/// Application-owned style vocabulary (primary/danger/navigation/status). The native side only
/// sees flattened semantic operations through <see cref="IGpuiElementStyle{TTag}"/>.
/// </summary>
internal enum BoardButtonVariant
{
    Standard,
    Primary,
    Danger,
    Navigation,
    TableHeader,
}

internal readonly record struct BoardButtonStyle(
    Color Background,
    Color HoverBackground,
    Color ActiveBackground,
    Color Text,
    Color Border
) : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Padding(Px(8))
            .Radius(Px(6))
            .Background(Background)
            .HoverBackground(HoverBackground)
            .ActiveBackground(ActiveBackground)
            .TextColor(Text)
            .BorderWidth(Px(1))
            .BorderColor(Border);
}

internal readonly record struct BoardHeaderButtonStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<ButtonTag>
{
    // Compact chrome for table headers: a full button recipe overflows narrow columns.
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Padding(Px(6))
            .Radius(Px(4))
            .Background(new Color(0))
            .HoverBackground(Theme.Colors.ElementHover)
            .ActiveBackground(Theme.Colors.ElementActive)
            .TextColor(Selected ? Theme.Colors.TextAccent : Theme.Colors.Text)
            .BorderWidth(Px(0));
}

internal readonly record struct BoardCardStyle(GpuiTheme Theme) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> card) =>
        card
            .Padding(Px(12))
            .Radius(Px(10))
            .Background(Theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(Theme.Colors.BorderVariant)
            .TextColor(Theme.Colors.Text);
}

internal readonly record struct BoardRowStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> row) =>
        row
            .Background(Selected ? Theme.Colors.ElementSelected : Theme.Colors.SurfaceBackground)
            .TextColor(Selected ? Theme.Colors.TextAccent : Theme.Colors.Text)
            .PaddingY(Px(7));
}

internal readonly record struct BoardTableStyle(GpuiTheme Theme) : IGpuiElementStyle<TableTag>
{
    public Element<TableTag> Apply(Element<TableTag> table) =>
        table
            .Background(Theme.Colors.SurfaceBackground)
            .BorderColor(Theme.Colors.BorderVariant)
            .BorderWidth(Px(1))
            .Radius(Px(10))
            .HeaderBackground(Theme.Colors.PanelBackground)
            .HeaderTextColor(Theme.Colors.TitleBarText)
            .HeaderBorderColor(Theme.Colors.BorderFocused);
}

internal readonly record struct BoardFieldStyle(GpuiTheme Theme, bool Invalid)
    : IGpuiElementStyle<InputTag>
{
    public Element<InputTag> Apply(Element<InputTag> input)
    {
        var colors = Theme.Colors;
        return input
            .Background(colors.SurfaceBackground)
            .TextColor(colors.Text)
            .BorderColor(Invalid ? colors.Error : colors.Border)
            .PlaceholderColor(Invalid ? colors.Error : colors.TextMuted)
            .CaretColor(Invalid ? colors.Error : colors.Accent)
            .SelectionColor(colors.InfoBackground);
    }
}

internal readonly record struct BoardEstimateStyle(GpuiTheme Theme) : IGpuiElementStyle<SliderTag>
{
    public Element<SliderTag> Apply(Element<SliderTag> slider) =>
        slider
            .TrackColor(Theme.Colors.ElementActive)
            .FillColor(Theme.Colors.Accent)
            .ThumbColor(Theme.Colors.SurfaceBackground)
            .ThumbBorderColor(Theme.Colors.Accent);
}

internal static class BoardStyles
{
    internal static BoardButtonStyle Button(GpuiTheme theme, BoardButtonVariant variant = BoardButtonVariant.Standard, bool selected = false)
    {
        var colors = theme.Colors;
        return variant switch
        {
            BoardButtonVariant.Primary => new(colors.Accent, colors.AccentHover, colors.AccentActive, colors.TextOnAccent, colors.Accent),
            BoardButtonVariant.Danger => new(colors.ErrorBackground, colors.ErrorBackground, colors.ErrorBackground, colors.Error, colors.Error),
            BoardButtonVariant.Navigation when selected => new(colors.Accent, colors.AccentHover, colors.AccentActive, colors.TextOnAccent, colors.BorderFocused),
            BoardButtonVariant.Navigation => new(colors.TitleBarBackground, colors.TitleBarHover, colors.TitleBarBackground, colors.TitleBarText, colors.TitleBarHover),
            _ => new(colors.ElementBackground, colors.ElementHover, colors.ElementActive, colors.Text, colors.Border),
        };
    }

    internal static BoardCardStyle Card(GpuiTheme theme) => new(theme);
    internal static BoardRowStyle Row(GpuiTheme theme, bool selected) => new(theme, selected);
    internal static BoardHeaderButtonStyle HeaderButton(GpuiTheme theme, bool selected) => new(theme, selected);
    internal static BoardTableStyle Table(GpuiTheme theme) => new(theme);
    internal static BoardFieldStyle Field(GpuiTheme theme, bool invalid = false) => new(theme, invalid);
    internal static BoardEstimateStyle Estimate(GpuiTheme theme) => new(theme);

    internal static Color StatusColor(TaskStatus status, GpuiThemeColors colors) =>
        status switch
        {
            TaskStatus.Todo => colors.TextMuted,
            TaskStatus.InProgress => colors.Info,
            TaskStatus.Review => colors.Warning,
            TaskStatus.Done => colors.Success,
            _ => colors.TextMuted,
        };

    internal static Color StatusBackground(TaskStatus status, GpuiThemeColors colors) =>
        status switch
        {
            TaskStatus.InProgress => colors.InfoBackground,
            TaskStatus.Review => colors.WarningBackground,
            TaskStatus.Done => colors.SuccessBackground,
            _ => colors.ElementActive,
        };

    internal static string StatusLabel(TaskStatus status) =>
        status switch
        {
            TaskStatus.Todo => "To do",
            TaskStatus.InProgress => "Doing",
            TaskStatus.Review => "Review",
            TaskStatus.Done => "Done",
            _ => status.ToString(),
        };
}
