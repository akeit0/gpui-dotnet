namespace Gpui;

/// <summary>Warm light and dark themes with content colors matched to Wander's surfaces.</summary>
internal static class WanderThemes
{
    internal static GpuiTheme Light { get; } =
        new(
            "Wander Light",
            new GpuiThemeColors
            {
                Background = Colors.Hex("#E8E4DC"),
                SurfaceBackground = Colors.Hex("#FFFFFF"),
                ElevatedSurfaceBackground = Colors.Hex("#FFFFFF"),
                ElementBackground = Colors.Hex("#FFFFFF"),
                ElementHover = Colors.Hex("#F3EFE7"),
                ElementActive = Colors.Hex("#E7E0D2"),
                ElementSelected = Colors.Hex("#FFE7C2"),
                Text = Colors.Hex("#22301F"),
                TextMuted = Colors.Hex("#686254"),
                TextPlaceholder = Colors.Hex("#A8A294"),
                TextDisabled = Colors.Hex("#A8A294"),
                TextAccent = Colors.Hex("#A54A00"),
                TextOnAccent = Colors.Hex("#FFFFFF"),
                Accent = Colors.Hex("#BC5400"),
                AccentHover = Colors.Hex("#A54A00"),
                AccentActive = Colors.Hex("#903F00"),
                Icon = Colors.Hex("#3E4A3A"),
                IconMuted = Colors.Hex("#686254"),
                TitleBarBackground = Colors.Hex("#1E2B1F"),
                TitleBarHover = Colors.Hex("#2C3D2A"),
                TitleBarText = Colors.Hex("#F7F3EA"),
                PanelBackground = Colors.Hex("#1E2B1F"),
                PanelFocusedBorder = Colors.Hex("#F2A93B"),
                Success = Colors.Hex("#2E7D32"),
                SuccessBackground = Colors.Hex("#DFF0DF"),
                Warning = Colors.Hex("#9A5B00"),
                WarningBackground = Colors.Hex("#FFF1CF"),
                Error = Colors.Hex("#B42318"),
                ErrorBackground = Colors.Hex("#FDE4E2"),
                Info = Colors.Hex("#0B5FFF"),
                InfoBackground = Colors.Hex("#E3EDFF"),
            },
            GpuiThemeAppearance.Light
        );

    internal static GpuiTheme Dark { get; } =
        GpuiTheme.FromJson(
            """
            {
              "name": "Wander Dark",
              "appearance": "dark",
              "colors": {
                "text.muted": "#CBD5E1",
                "icon.muted": "#CBD5E1"
              }
            }
            """
        );
}
