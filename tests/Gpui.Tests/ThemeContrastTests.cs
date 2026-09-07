namespace Gpui.Tests;

public sealed class ThemeContrastTests
{
    [Theory]
    [InlineData(GpuiThemeAppearance.Light)]
    [InlineData(GpuiThemeAppearance.Dark)]
    public void DefaultAccentForegroundIsReadableOnEveryAccentState(GpuiThemeAppearance appearance)
    {
        var defaults = GpuiTheme.CreateDefault(appearance);
        var jsonDefaults = GpuiTheme.FromJson(appearance == GpuiThemeAppearance.Dark
            ? "{\"appearance\":\"dark\"}" : "{\"appearance\":\"light\"}");
        foreach (var theme in new[] { defaults, jsonDefaults })
        {
            var colors = theme.Colors;
            foreach (var background in new[] { colors.Accent, colors.AccentHover, colors.AccentActive })
                ContrastAssert.OpaqueText(colors.TextOnAccent, background, $"{theme.Name}, {appearance}, accent");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GalleryButtonRecipesKeepTextReadableAcrossInteractionStates(bool dark)
    {
        var theme = dark ? SampleThemes.Dark : SampleThemes.Light;
        foreach (var variant in Enum.GetValues<SampleButtonVariant>())
        foreach (var selected in new[] { false, true })
        {
            var colors = SampleStyles.Button(theme, variant, selected).Colors;
            foreach (var surface in new[] { colors.Normal, colors.Hover, colors.Pressed })
                ContrastAssert.OpaqueText(surface.Foreground, surface.Background,
                    $"{theme.Name}, {variant}, selected={selected}");
        }
    }
}
