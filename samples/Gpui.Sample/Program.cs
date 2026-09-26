using Gpui;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
var multiWindow = args.Contains("--multi-window", StringComparer.Ordinal);
var persistWindow = args.Contains("--persist-window", StringComparer.Ordinal);
var savedPlacement = persistWindow ? WindowPlacementStore.Load() : null;
var application = new GpuiApplication();
application.SetTheme(SampleThemes.Light);
var options = new GpuiWindowOptions
{
    Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Components",
    Width = savedPlacement?.Width ?? 1040,
    Height = savedPlacement?.Height ?? 700,
    Left = savedPlacement?.Left,
    Top = savedPlacement?.Top,
    InitialState = savedPlacement?.State ?? WindowInitialState.Normal,
    TitleBarStyle =
        stressGrowth || OperatingSystem.IsMacOS()
            ? WindowTitleBarStyle.System
            : WindowTitleBarStyle.Custom,
};
var primaryWindow = stressGrowth
    ? application.OpenWindow(ArenaGrowthView.Spec(), options)
    : application.OpenWindow(SampleShellView.Spec(), options);
if (persistWindow)
{
    primaryWindow.Closed += window =>
    {
        if (window.FinalPlacement is { } placement)
            WindowPlacementStore.Save(placement);
    };
}
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
