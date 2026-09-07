using Gpui;

var dark = args.Contains("--dark", StringComparer.Ordinal);
var application = new GpuiApplication();
application.SetTheme(dark ? WanderThemes.Dark : WanderThemes.Light);
application.OpenWindow(
    WanderShellView.Spec(),
    new GpuiWindowOptions
    {
        Title = "Wander — Travel Journal",
        Width = 430,
        Height = 800,
        TitleBarStyle = OperatingSystem.IsMacOS()
            ? WindowTitleBarStyle.System
            : WindowTitleBarStyle.Custom,
    }
);
application.Run();
