using static Gpui.Units;

namespace Gpui;

/// <summary>Mobile-style recipes: chunky touch targets, pills, cards, sun accent.</summary>
internal enum WanderButtonVariant
{
    Standard,
    Primary,
    Chip,
}

internal readonly record struct WanderButtonStyle(
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

internal readonly record struct WanderCardStyle(GpuiTheme Theme) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> card) =>
        card
            .Padding(Px(14))
            .Radius(Px(16))
            .Background(Theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(Theme.Colors.BorderVariant)
            .TextColor(Theme.Colors.Text);
}

internal readonly record struct WanderFieldStyle(GpuiTheme Theme) : IGpuiElementStyle<InputTag>
{
    public Element<InputTag> Apply(Element<InputTag> input) =>
        input
            .Background(Theme.Colors.SurfaceBackground)
            .TextColor(Theme.Colors.Text)
            .BorderColor(Theme.Colors.Border)
            .PlaceholderColor(Theme.Colors.TextMuted)
            .CaretColor(Theme.Colors.Accent)
            .SelectionColor(Theme.Colors.InfoBackground);
}

internal readonly record struct WanderGoalStyle(GpuiTheme Theme) : IGpuiElementStyle<SliderTag>
{
    public Element<SliderTag> Apply(Element<SliderTag> slider) =>
        slider
            .TrackColor(Theme.Colors.ElementActive)
            .FillColor(Theme.Colors.Accent)
            .ThumbColor(Theme.Colors.SurfaceBackground)
            .ThumbBorderColor(Theme.Colors.Accent);
}

internal static class WanderStyles
{
    internal static WanderButtonStyle Button(
        GpuiTheme theme,
        WanderButtonVariant variant = WanderButtonVariant.Standard,
        bool selected = false
    )
    {
        var colors = theme.Colors;
        return variant switch
        {
            WanderButtonVariant.Primary => new(
                colors.Accent, colors.AccentHover, colors.AccentActive,
                colors.TextOnAccent, colors.Accent, Px(12), Px(14)),
            WanderButtonVariant.Chip when selected => new(
                colors.Text, colors.Text, colors.Text,
                colors.SurfaceBackground, colors.Text, Px(9), Px(18)),
            WanderButtonVariant.Chip => new(
                colors.SurfaceBackground, colors.ElementHover, colors.ElementActive,
                colors.TextMuted, colors.Border, Px(9), Px(18)),
            _ => new(
                colors.ElementBackground, colors.ElementHover, colors.ElementActive,
                colors.Text, colors.Border, Px(12), Px(14)),
        };
    }

    internal static WanderCardStyle Card(GpuiTheme theme) => new(theme);
    internal static WanderFieldStyle Field(GpuiTheme theme) => new(theme);
    internal static WanderGoalStyle Goal(GpuiTheme theme) => new(theme);

    internal static string TagLabel(PlaceTag tag) =>
        tag switch
        {
            PlaceTag.Beach => "Beach",
            PlaceTag.Mountain => "Mountain",
            _ => "City",
        };

    internal static Color AvatarColor(int id, GpuiThemeColors colors) =>
        (id % 6) switch
        {
            0 => colors.Accent,
            1 => colors.Info,
            2 => colors.Success,
            3 => colors.Warning,
            4 => colors.Error,
            _ => colors.TextAccent,
        };

    internal static string Stars(int rating) =>
        rating switch
        {
            0 => "☆☆☆☆☆",
            1 => "★☆☆☆☆",
            2 => "★★☆☆☆",
            3 => "★★★☆☆",
            4 => "★★★★☆",
            _ => "★★★★★",
        };
}
