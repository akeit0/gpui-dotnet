using Gpui;

var dark = args.Contains("--dark", StringComparer.Ordinal);
var application = new GpuiApplication();
application.SetTheme(dark ? TaskBoardThemes.Dark : TaskBoardThemes.Light);
application.OpenWindow(
    TaskBoardShellView.Spec(),
    new GpuiWindowOptions
    {
        Title = "TaskBoard — Team Project Tracker",
        Width = 1240,
        Height = 780,
        TitleBarStyle = OperatingSystem.IsMacOS()
            ? WindowTitleBarStyle.System
            : WindowTitleBarStyle.Custom,
    }
);
application.Run();
