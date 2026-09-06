using Gpui;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
var multiWindow = args.Contains("--multi-window", StringComparer.Ordinal);
var application = new GpuiApplication();
application.SetTheme(SampleThemes.Light);
var options = new GpuiWindowOptions
{
    Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Components",
    Width = 1040,
    Height = 700,
    TitleBarStyle = stressGrowth || OperatingSystem.IsMacOS()
        ? WindowTitleBarStyle.System : WindowTitleBarStyle.Custom,
};
if (stressGrowth) application.OpenWindow(ArenaGrowthView.Spec(), options);
else application.OpenWindow(SampleShellView.Spec(), options);
if (multiWindow)
{
    application.OpenWindow(
        CompanionWindowView.Spec("Started with --multi-window"),
        new GpuiWindowOptions
        {
            Title = "GPUI.NET Companion — Window 2",
            Width = 860,
            Height = 620,
            Left = 80,
            Top = 80,
        }
    );
}
application.Run();
