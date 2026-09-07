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
    Color Background,
    Color HoverBackground,
    Color ActiveBackground,
    Color Text,
    Color Border,
    Pixels Padding,
    Pixels Radius
) : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Padding(Padding)
            .Radius(Radius)
            .Background(Background)
            .HoverBackground(HoverBackground)
            .ActiveBackground(ActiveBackground)
            .TextColor(Text)
            .BorderWidth(Px(1))
            .BorderColor(Border);
}

internal readonly record struct SampleCollectionRowStyle(
    Color Background,
    Color Border,
    Color Text,
    Pixels Padding
) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> row) =>
        row
            .Padding(Padding)
            .Radius(Px(6))
            .Background(Background)
            .BorderColor(Border)
            .BorderWidth(Px(1))
            .TextColor(Text);
}

internal static class SampleStyles
{
    internal static SampleCollectionRowStyle CollectionRow(GpuiTheme theme, bool selected)
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
                colors.Accent,
                colors.AccentHover,
                colors.AccentActive,
                colors.TextOnAccent,
                colors.Accent,
                padding,
                radius
            ),
            SampleButtonVariant.Navigation when selected => new SampleButtonStyle(
                colors.Accent,
                colors.AccentHover,
                colors.AccentActive,
                colors.TextOnAccent,
                colors.BorderFocused,
                padding,
                radius
            ),
            SampleButtonVariant.Navigation => new SampleButtonStyle(
                colors.TitleBarBackground,
                colors.TitleBarHover,
                colors.TitleBarInactiveBackground,
                colors.TitleBarText,
                colors.TitleBarHover,
                padding,
                radius
            ),
            _ => new SampleButtonStyle(
                colors.ElementBackground,
                colors.ElementHover,
                colors.ElementActive,
                colors.Text,
                colors.Border,
                padding,
                radius
            ),
        };
    }
}
