using static Gpui.Units;

namespace Gpui;

/// <summary>Mobile-style recipes: chunky touch targets, pills, cards, sun accent.</summary>
internal enum WanderButtonVariant
{
    Standard,
    Primary,
    Chip,
    Navigation,
    Like,
}

internal readonly record struct WanderButtonStyle(
    InteractionColors Colors,
    Color Border,
    Pixels Padding,
    Pixels Radius,
    float Weight = 400
) : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Padding(Padding)
            .Radius(Radius)
            .FontWeight(Weight)
            .Paint(Colors)
            .BorderWidth(Px(1))
            .BorderColor(Border);
}

internal readonly record struct WanderCardStyle(GpuiTheme Theme) : IGpuiElementStyle<DivTag>
{
    public Element<DivTag> Apply(Element<DivTag> card) =>
        card
            .Padding(Px(14))
            .Radius(Px(16))
            .Surface(new(Theme.Colors.SurfaceBackground, Theme.Colors.Text))
            .BorderWidth(Px(1))
            .BorderColor(Theme.Colors.BorderVariant);
}

internal readonly record struct WanderFieldStyle(GpuiTheme Theme) : IGpuiElementStyle<InputTag>
{
    public Element<InputTag> Apply(Element<InputTag> input) =>
        input
            .Surface(new(Theme.Colors.SurfaceBackground, Theme.Colors.Text))
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
    private static readonly WanderButtonStyle LightSelection = new(
        new(
            new(new(0xFFF1DDFF), new(0x8A430BFF)),
            new(new(0xFFE6BFFF), new(0x8A430BFF)),
            new(new(0xFFD9A3FF), new(0x8A430BFF))),
        new(0xF2D1A7FF), Px(9), Px(18), 600);

    private static readonly WanderButtonStyle DarkSelection = new(
        new(
            new(new(0x3A2A1BFF), new(0xFFD7A0FF)),
            new(new(0x493321FF), new(0xFFD7A0FF)),
            new(new(0x583D24FF), new(0xFFD7A0FF))),
        new(0x7C5632FF), Px(9), Px(18), 600);

    private static readonly WanderButtonStyle LightLiked = new(
        new(
            new(new(0xFCE7F0FF), new(0x9D174DFF)),
            new(new(0xFBCFE1FF), new(0x9D174DFF)),
            new(new(0xF9B6D2FF), new(0x9D174DFF))),
        new(0xF4AAC8FF), Px(9), Px(18), 600);

    private static readonly WanderButtonStyle DarkLiked = new(
        new(
            new(new(0x421F32FF), new(0xFFBDD7FF)),
            new(new(0x52263EFF), new(0xFFBDD7FF)),
            new(new(0x642B48FF), new(0xFFBDD7FF))),
        new(0xA34C76FF), Px(9), Px(18), 600);

    internal static WanderButtonStyle Button(
        GpuiTheme theme,
        WanderButtonVariant variant = WanderButtonVariant.Standard,
        bool selected = false
    )
    {
        var colors = theme.Colors;
        var dark = theme.Appearance == GpuiThemeAppearance.Dark;
        return variant switch
        {
            WanderButtonVariant.Primary => new(
                new(
                    new(colors.Accent, colors.TextOnAccent),
                    new(colors.AccentHover, colors.TextOnAccent),
                    new(colors.AccentActive, colors.TextOnAccent)),
                colors.Accent, Px(12), Px(14)),
            WanderButtonVariant.Like when selected => dark ? DarkLiked : LightLiked,
            WanderButtonVariant.Navigation when selected =>
                (dark ? DarkSelection : LightSelection) with { Radius = Px(12) },
            WanderButtonVariant.Chip when selected => dark ? DarkSelection : LightSelection,
            WanderButtonVariant.Navigation => new(
                new(
                    new(colors.SurfaceBackground, colors.TextMuted),
                    new(colors.ElementHover, colors.Text),
                    new(colors.ElementActive, colors.Text)),
                colors.SurfaceBackground, Px(9), Px(12)),
            WanderButtonVariant.Chip or WanderButtonVariant.Like => new(
                new(
                    new(colors.SurfaceBackground, colors.TextMuted),
                    new(colors.ElementHover, colors.TextMuted),
                    new(colors.ElementActive, colors.TextMuted)),
                colors.Border, Px(9), Px(18)),
            _ => new(
                new(
                    new(colors.ElementBackground, colors.Text),
                    new(colors.ElementHover, colors.Text),
                    new(colors.ElementActive, colors.Text)),
                colors.Border, Px(12), Px(14)),
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
