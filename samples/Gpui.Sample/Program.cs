using Gpui;

var stressGrowth = args.Contains("--stress-growth", StringComparer.Ordinal);
var multiWindow = args.Contains("--multi-window", StringComparer.Ordinal);
var application = new GpuiApplication();
application.SetTheme(SampleThemes.Light);
var shell = new SampleShellView { Application = application };
var menuBar = stressGrowth ? Array.Empty<GpuiMenu>() : shell.CreateMenuBar();
shell.MenuBar = menuBar;
if (!stressGrowth && OperatingSystem.IsMacOS())
{
    application.SetMenuBar(menuBar);
}
View rootView = stressGrowth ? new ArenaGrowthView() : shell;
var mainWindow = application.OpenWindow(
    rootView,
    new GpuiWindowOptions
    {
        Title = stressGrowth ? "GPUI.NET Arena Growth" : "GPUI.NET Components",
        Width = 1040,
        Height = 700,
        TitleBarStyle =
            stressGrowth || OperatingSystem.IsMacOS()
                ? WindowTitleBarStyle.System
                : WindowTitleBarStyle.Custom,
    }
);
if (rootView is SampleShellView mainShell)
{
    mainShell.Window = mainWindow;
}
if (multiWindow)
{
    var secondRoot = new CompanionWindowView { Origin = "Started with --multi-window" };
    var secondWindow = application.OpenWindow(
        secondRoot,
        new GpuiWindowOptions
        {
            Title = "GPUI.NET Companion — Window 2",
            Width = 860,
            Height = 620,
            Left = 80,
            Top = 80,
        }
    );
    secondRoot.Window = secondWindow;
}
application.Run();
