using Gpui;
using static Gpui.Units;

internal enum SampleButtonVariant
{
    Standard,
    Primary,
    Navigation,
}

/// <summary>
/// Sample-owned button recipe. GPUI.NET only knows how to apply the typed style; the variant
/// vocabulary and its semantic-token mapping belong to this application.
/// </summary>
internal readonly record struct SampleButtonStyle(
    InteractionColors Colors,
    Color Border,
    Pixels Padding,
    Pixels Radius
) : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button.Padding(Padding).Radius(Radius).Paint(Colors).BorderWidth(Px(1)).BorderColor(Border);
}

internal readonly record struct SampleCollectionItemStyle(
    Color Background,
    Color Border,
    Color Text,
    Pixels Padding
) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> item) =>
        item.Padding(Padding)
            .Radius(Px(6))
            .Surface(new(Background, Text))
            .BorderColor(Border)
            .BorderWidth(Px(1));
}

internal readonly record struct SampleTableRowStyle(GpuiTheme Theme, bool Selected)
    : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> row) =>
        row.Surface(
                new(
                    Selected ? Theme.Colors.ElementSelected : Theme.Colors.SurfaceBackground,
                    Selected ? Theme.Colors.TextAccent : Theme.Colors.Text
                )
            )
            .PaddingY(Px(8));
}

internal readonly record struct SampleInputStyle(GpuiTheme Theme, bool Invalid)
    : IGpuiElementStyle<InputTag>
{
    public Element<InputTag> Apply(Element<InputTag> input)
    {
        var colors = Theme.Colors;
        var accent = Invalid ? colors.Error : colors.Accent;
        return input
            .Surface(new(colors.SurfaceBackground, colors.Text))
            .BorderColor(Invalid ? colors.Error : colors.Border)
            .PlaceholderColor(Invalid ? colors.Error : colors.TextMuted)
            .CaretColor(accent)
            .SelectionColor(Invalid ? colors.ErrorBackground : colors.InfoBackground);
    }
}

internal readonly record struct SampleTableStyle(GpuiTheme Theme) : IGpuiElementStyle<TableTag>
{
    public Element<TableTag> Apply(Element<TableTag> table) =>
        table
            .Surface(new(Theme.Colors.SurfaceBackground, Theme.Colors.Text))
            .BorderColor(Theme.Colors.BorderVariant)
            .BorderWidth(Px(1))
            .Radius(Px(8))
            .HeaderBackground(Theme.Colors.InfoBackground)
            .HeaderTextColor(Theme.Colors.Info)
            .HeaderBorderColor(Theme.Colors.BorderFocused);
}

internal readonly record struct SampleSliderStyle(GpuiTheme Theme) : IGpuiElementStyle<SliderTag>
{
    public Element<SliderTag> Apply(Element<SliderTag> slider) =>
        slider
            .TrackColor(Theme.Colors.SuccessBackground)
            .FillColor(Theme.Colors.Success)
            .ThumbColor(Theme.Colors.SurfaceBackground)
            .ThumbBorderColor(Theme.Colors.Success);
}

internal static class SampleStyles
{
    internal static SampleTableStyle Table(GpuiTheme theme) => new(theme);

    internal static SampleSliderStyle Slider(GpuiTheme theme) => new(theme);

    internal static SampleTableRowStyle TableRow(GpuiTheme theme, bool selected) =>
        new(theme, selected);

    internal static SampleInputStyle Input(GpuiTheme theme, bool invalid = false) =>
        new(theme, invalid);

    internal static SampleButtonStyle TableHeader(GpuiTheme theme) =>
        new(
            new(
                new(new Color(0), theme.Colors.Info),
                new(theme.Colors.ElementHover, theme.Colors.Info),
                new(theme.Colors.ElementActive, theme.Colors.Info)
            ),
            new Color(0),
            Px(6),
            Px(4)
        );

    internal static SampleCollectionItemStyle CollectionItem(GpuiTheme theme, bool selected)
    {
        var colors = theme.Colors;
        return new(
            selected ? colors.ElementSelected : colors.SurfaceBackground,
            selected ? colors.BorderSelected : colors.BorderVariant,
            selected ? colors.TextAccent : colors.Text,
            Px(selected ? 12 : 9)
        );
    }

    internal static SampleButtonStyle Button(
        GpuiTheme theme,
        SampleButtonVariant variant = SampleButtonVariant.Standard,
        bool selected = false
    )
    {
        var colors = theme.Colors;
        var padding = Px(8);
        var radius = Px(6);
        return variant switch
        {
            SampleButtonVariant.Primary => new SampleButtonStyle(
                new(
                    new(colors.Accent, colors.TextOnAccent),
                    new(colors.AccentHover, colors.TextOnAccent),
                    new(colors.AccentActive, colors.TextOnAccent)
                ),
                colors.Accent,
                padding,
                radius
            ),
            SampleButtonVariant.Navigation when selected => new SampleButtonStyle(
                new(
                    new(colors.Accent, colors.TextOnAccent),
                    new(colors.AccentHover, colors.TextOnAccent),
                    new(colors.AccentActive, colors.TextOnAccent)
                ),
                colors.BorderFocused,
                padding,
                radius
            ),
            SampleButtonVariant.Navigation => new SampleButtonStyle(
                new(
                    new(colors.TitleBarBackground, colors.TitleBarText),
                    new(colors.TitleBarHover, colors.TitleBarText),
                    new(colors.TitleBarInactiveBackground, colors.TitleBarText)
                ),
                colors.TitleBarHover,
                padding,
                radius
            ),
            _ => new SampleButtonStyle(
                new(
                    new(colors.ElementBackground, colors.Text),
                    new(colors.ElementHover, colors.Text),
                    new(colors.ElementActive, colors.Text)
                ),
                colors.Border,
                padding,
                radius
            ),
        };
    }
}
