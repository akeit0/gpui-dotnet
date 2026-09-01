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
    Pixels Radius,
    Pixels FontSize
) : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Padding(Padding)
            .Radius(Radius)
            .FontSize(FontSize)
            .Background(Background)
            .HoverBackground(HoverBackground)
            .ActiveBackground(ActiveBackground)
            .TextColor(Text)
            .BorderWidth(Px(1))
            .BorderColor(Border);
}

internal static class SampleStyles
{
    internal static SampleButtonStyle Button(
        GpuiTheme theme,
        SampleButtonVariant variant = SampleButtonVariant.Standard,
        bool selected = false
    )
    {
        var colors = theme.Colors;
        var fontSize = Px(theme.Typography.Button);
        return variant switch
        {
            SampleButtonVariant.Primary => new SampleButtonStyle(
                colors.Accent,
                colors.AccentHover,
                colors.AccentActive,
                colors.TextOnAccent,
                colors.Accent,
                Px(10),
                Px(6),
                fontSize
            ),
            SampleButtonVariant.Navigation when selected => new SampleButtonStyle(
                colors.Accent,
                colors.AccentHover,
                colors.AccentActive,
                colors.TextOnAccent,
                colors.BorderFocused,
                Px(10),
                Px(6),
                fontSize
            ),
            SampleButtonVariant.Navigation => new SampleButtonStyle(
                colors.TitleBarBackground,
                colors.TitleBarHover,
                colors.TitleBarInactiveBackground,
                colors.TitleBarText,
                colors.TitleBarHover,
                Px(10),
                Px(6),
                fontSize
            ),
            _ => new SampleButtonStyle(
                colors.ElementBackground,
                colors.ElementHover,
                colors.ElementActive,
                colors.Text,
                colors.Border,
                Px(8),
                Px(6),
                fontSize
            ),
        };
    }
}
