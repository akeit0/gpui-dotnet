using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void HexColorsSupportShortAndAlphaForms()
    {
        Assert.Equal(new Color(0x112233FF), Colors.Hex("#123"));
        Assert.Equal(new Color(0x11223344), Colors.Hex("#1234"));
        Assert.Equal(new Color(0x112233FF), Colors.Hex("112233"));
        Assert.Equal(new Color(0x11223344), Colors.Hex("#11223344"));
        Assert.Equal(new Color(0x00000000), Colors.Hex("transparent"));
        Assert.Throws<FormatException>(() => Colors.Hex("#12"));
    }

    [Fact]
    public void ThemeCanBeConfiguredInCodeAndInstalledOnAnApplication()
    {
        var theme = new GpuiTheme(
            "Ocean",
            new GpuiThemeColors
            {
                Background = Colors.Hex("#07111F"),
                Text = Colors.Hex("#E0F2FE"),
                Accent = Colors.Hex("#22D3EE"),
            },
            GpuiThemeAppearance.Dark
        );
        var application = new GpuiApplication();

        application.SetTheme(theme);

        Assert.Same(theme, application.Theme);
        Assert.Equal(new Color(0x07111FFF), application.Theme.Colors.Background);
        Assert.Equal(new Color(0xE0F2FEFF), application.Theme.Colors.Text);
        Assert.Equal(new Color(0x22D3EEFF), application.Theme.Colors.Accent);
    }

    [Fact]
    public void ThemeCanLoadDirectAndZedStyleJson()
    {
        var direct = GpuiTheme.FromJson(
            """
            {
              "name": "Ocean",
              "appearance": "dark",
              "colors": {
                "background": "#07111F",
                "surface_background": "#0F2035",
                "text": "#E0F2FE",
                "text_muted": "#7DD3FC",
                "text.on_accent": "#001018",
                "accent.hover": "#67E8F9",
                "accent.active": "#06B6D4",
                "element.hover": "#12304A"
              },
              "typography": {
                "large": 26,
                "detail": 10
              }
            }
            """
        );

        Assert.Equal("Ocean", direct.Name);
        Assert.Equal(GpuiThemeAppearance.Dark, direct.Appearance);
        Assert.Equal(new Color(0x07111FFF), direct.Colors.Background);
        Assert.Equal(new Color(0x0F2035FF), direct.Colors.SurfaceBackground);
        Assert.Equal(new Color(0x7DD3FCFF), direct.Colors.TextMuted);
        Assert.Equal(new Color(0x001018FF), direct.Colors.TextOnAccent);
        Assert.Equal(new Color(0x67E8F9FF), direct.Colors.AccentHover);
        Assert.Equal(new Color(0x06B6D4FF), direct.Colors.AccentActive);
        Assert.Equal(new Color(0x12304AFF), direct.Colors.ElementHover);
        Assert.Equal(26, direct.Typography.Large);
        Assert.Equal(10, direct.Typography.Detail);

        var family = GpuiTheme.FromJson(
            """
            {
              "$schema": "https://zed.dev/schema/themes/v0.2.0.json",
              "name": "Example",
              "themes": [
                {
                  "name": "Example Dark",
                  "appearance": "dark",
                  "style": {
                    "background": "#101010ff",
                    "text": "#f0f0f0ff",
                    "title_bar.background": "#202020ff"
                  }
                }
              ]
            }
            """,
            "Example Dark"
        );

        Assert.Equal("Example Dark", family.Name);
        Assert.Equal(new Color(0x101010FF), family.Colors.Background);
        Assert.Equal(new Color(0x202020FF), family.Colors.TitleBarBackground);
        Assert.Equal(new Color(0x475569FF), family.Colors.Border);
    }

    [Fact]
    public void RenderContextUsesTheDefaultThemeOutsideAnApplication()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();

        Assert.Same(GpuiTheme.Default, ui.Theme);
        Assert.Equal(GpuiTheme.Default.Colors.Background, ui.Theme.Colors.Background);
    }

    [Fact]
    public void NativeThemePayloadContainsResolvedSemanticRolesOnly()
    {
        var theme = GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark);

        var payload = NativeThemePayload.From(theme);

        Assert.Equal(NativeThemePayload.CurrentVersion, payload.Version);
        Assert.Equal((uint)theme.Appearance, payload.Appearance);
        Assert.Equal(20 * sizeof(uint), Marshal.SizeOf<NativeThemePayload>());
        Assert.Equal(theme.Colors.Background.Rgba, payload.Background);
        Assert.Equal(theme.Colors.TextOnAccent.Rgba, payload.TextOnAccent);
        Assert.Equal(theme.Colors.Accent.Rgba, payload.Accent);
        Assert.Equal(theme.Colors.ScrollbarThumbBackground.Rgba, payload.ScrollbarThumbBackground);
    }
}
