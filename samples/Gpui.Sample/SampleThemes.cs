using Gpui;

internal static class SampleThemes
{
    internal static GpuiTheme Light { get; } =
        new(
            "Sample Light",
            new GpuiThemeColors
            {
                Background = Colors.Hex("#F1F5F9"),
                SurfaceBackground = Colors.Hex("#FFFFFF"),
                ElevatedSurfaceBackground = Colors.Hex("#FFFFFF"),
                ElementBackground = Colors.Hex("#FFFFFF"),
                ElementHover = Colors.Hex("#F1F5F9"),
                ElementActive = Colors.Hex("#E2E8F0"),
                ElementSelected = Colors.Hex("#E0E7FF"),
                Text = Colors.Hex("#1E293B"),
                TextMuted = Colors.Hex("#64748B"),
                TextPlaceholder = Colors.Hex("#94A3B8"),
                TextDisabled = Colors.Hex("#94A3B8"),
                TextAccent = Colors.Hex("#4338CA"),
                TextOnAccent = Colors.Hex("#FFFFFF"),
                Accent = Colors.Hex("#4F46E5"),
                AccentHover = Colors.Hex("#4338CA"),
                AccentActive = Colors.Hex("#3730A3"),
                Icon = Colors.Hex("#334155"),
                IconMuted = Colors.Hex("#64748B"),
                TitleBarBackground = Colors.Hex("#0F172A"),
                TitleBarHover = Colors.Hex("#1E293B"),
                TitleBarText = Colors.Hex("#F8FAFC"),
                PanelBackground = Colors.Hex("#0F172A"),
                PanelFocusedBorder = Colors.Hex("#818CF8"),
                Success = Colors.Hex("#059669"),
                SuccessBackground = Colors.Hex("#DCFCE7"),
                Warning = Colors.Hex("#B45309"),
                WarningBackground = Colors.Hex("#FEF3C7"),
                Error = Colors.Hex("#B91C1C"),
                ErrorBackground = Colors.Hex("#FEE2E2"),
                Info = Colors.Hex("#1E40AF"),
                InfoBackground = Colors.Hex("#DBEAFE"),
            },
            GpuiThemeAppearance.Light
        );

    internal static GpuiTheme Dark { get; } =
        GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark, "Sample Dark");
}
