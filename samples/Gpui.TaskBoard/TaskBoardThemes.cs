namespace Gpui;

/// <summary>Application ambient theme. Roles are semantic; product variants live in styles.</summary>
internal static class TaskBoardThemes
{
    internal static GpuiTheme Light { get; } =
        new(
            "TaskBoard Light",
            new GpuiThemeColors
            {
                Background = Colors.Hex("#EDF1F6"),
                SurfaceBackground = Colors.Hex("#FFFFFF"),
                ElevatedSurfaceBackground = Colors.Hex("#FFFFFF"),
                ElementBackground = Colors.Hex("#FFFFFF"),
                ElementHover = Colors.Hex("#EAF0F7"),
                ElementActive = Colors.Hex("#DCE5F1"),
                ElementSelected = Colors.Hex("#DEF0FF"),
                Text = Colors.Hex("#16233A"),
                TextMuted = Colors.Hex("#506079"),
                TextPlaceholder = Colors.Hex("#8FA0B8"),
                TextDisabled = Colors.Hex("#8FA0B8"),
                TextAccent = Colors.Hex("#0B5FFF"),
                TextOnAccent = Colors.Hex("#FFFFFF"),
                Accent = Colors.Hex("#0B5FFF"),
                AccentHover = Colors.Hex("#084FD6"),
                AccentActive = Colors.Hex("#063FAE"),
                Icon = Colors.Hex("#33415C"),
                IconMuted = Colors.Hex("#5B6B84"),
                TitleBarBackground = Colors.Hex("#101C33"),
                TitleBarHover = Colors.Hex("#1B2C4E"),
                TitleBarText = Colors.Hex("#F4F7FC"),
                PanelBackground = Colors.Hex("#FFFFFF"),
                PanelFocusedBorder = Colors.Hex("#7AA5FF"),
                Success = Colors.Hex("#0E7A3D"),
                SuccessBackground = Colors.Hex("#DFF5E5"),
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
              "name": "TaskBoard Dark",
              "appearance": "dark",
              "colors": {
                "text.muted": "#CBD5E1"
              }
            }
            """
        );
}
