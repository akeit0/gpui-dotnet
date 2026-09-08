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
    InteractionColors Colors,
    Color SecondaryText,
    Color Border
) : IGpuiElementStyle<ButtonTag>
{
    public BoardContentStyle SecondaryContent => new(SecondaryText);

    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button.Padding(Px(8)).Radius(Px(6)).Paint(Colors).BorderWidth(Px(1)).BorderColor(Border);
}

internal readonly record struct BoardContentStyle(Color Text) : IGpuiElementStyle<TextTag>
{
    public Element<TextTag> Apply(Element<TextTag> text) => text.TextColor(Text);
}

internal readonly record struct BoardHeaderButtonStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<ButtonTag>
{
    // Compact chrome for table headers: a full button recipe overflows narrow columns.
    public Element<ButtonTag> Apply(Element<ButtonTag> button)
    {
        var foreground = Selected ? Theme.Colors.TextAccent : Theme.Colors.Text;
        return button
            .Padding(Px(6))
            .Radius(Px(4))
            .Paint(
                new InteractionColors(
                    new(new Color(0), foreground),
                    new(Theme.Colors.ElementHover, foreground),
                    new(Theme.Colors.ElementActive, foreground)
                )
            )
            .BorderWidth(Px(0));
    }
}

internal readonly record struct BoardCardStyle(GpuiTheme Theme) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> card) =>
        card.Padding(Px(12))
            .Radius(Px(10))
            .Surface(new(Theme.Colors.SurfaceBackground, Theme.Colors.Text))
            .BorderWidth(Px(1))
            .BorderColor(Theme.Colors.BorderVariant);
}

internal readonly record struct BoardRowStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> row) =>
        row.Surface(
                new(
                    Selected ? Theme.Colors.ElementSelected : Theme.Colors.SurfaceBackground,
                    Selected ? Theme.Colors.TextAccent : Theme.Colors.Text
                )
            )
            .PaddingY(Px(7));
}

internal readonly record struct BoardTableStyle(GpuiTheme Theme) : IGpuiElementStyle<TableTag>
{
    public Element<TableTag> Apply(Element<TableTag> table) =>
        table
            .Surface(new(Theme.Colors.SurfaceBackground, Theme.Colors.Text))
            .BorderColor(Theme.Colors.BorderVariant)
            .BorderWidth(Px(1))
            .Radius(Px(10))
            .HeaderBackground(Theme.Colors.SurfaceBackground)
            .HeaderTextColor(Theme.Colors.Text)
            .HeaderBorderColor(Theme.Colors.BorderVariant);
}

internal readonly record struct BoardFieldStyle(GpuiTheme Theme, bool Invalid)
    : IGpuiElementStyle<InputTag>
{
    public Element<InputTag> Apply(Element<InputTag> input)
    {
        var colors = Theme.Colors;
        return input
            .Surface(new(colors.SurfaceBackground, colors.Text))
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
    internal static BoardButtonStyle Button(
        GpuiTheme theme,
        BoardButtonVariant variant = BoardButtonVariant.Standard,
        bool selected = false
    )
    {
        var colors = theme.Colors;
        return variant switch
        {
            BoardButtonVariant.Primary => new(
                new(
                    new(colors.Accent, colors.TextOnAccent),
                    new(colors.AccentHover, colors.TextOnAccent),
                    new(colors.AccentActive, colors.TextOnAccent)
                ),
                colors.TextOnAccent,
                colors.Accent
            ),
            BoardButtonVariant.Danger => new(
                new(
                    new(colors.ErrorBackground, colors.Error),
                    new(colors.ErrorBackground, colors.Error),
                    new(colors.ErrorBackground, colors.Error)
                ),
                colors.Error,
                colors.Error
            ),
            BoardButtonVariant.Navigation when selected => new(
                new(
                    new(colors.Accent, colors.TextOnAccent),
                    new(colors.AccentHover, colors.TextOnAccent),
                    new(colors.AccentActive, colors.TextOnAccent)
                ),
                colors.TextOnAccent,
                colors.BorderFocused
            ),
            BoardButtonVariant.Navigation => new(
                new(
                    new(colors.ElementBackground, colors.Text),
                    new(colors.ElementHover, colors.Text),
                    new(colors.ElementActive, colors.Text)
                ),
                colors.TextMuted,
                colors.BorderVariant
            ),
            _ => new(
                new(
                    new(colors.ElementBackground, colors.Text),
                    new(colors.ElementHover, colors.Text),
                    new(colors.ElementActive, colors.Text)
                ),
                colors.TextMuted,
                colors.Border
            ),
        };
    }

    internal static BoardCardStyle Card(GpuiTheme theme) => new(theme);

    internal static BoardRowStyle Row(GpuiTheme theme, bool selected) => new(theme, selected);

    internal static BoardHeaderButtonStyle HeaderButton(GpuiTheme theme, bool selected) =>
        new(theme, selected);

    internal static BoardTableStyle Table(GpuiTheme theme) => new(theme);

    internal static BoardFieldStyle Field(GpuiTheme theme, bool invalid = false) =>
        new(theme, invalid);

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
