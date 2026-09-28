using Foundation;
using UIKit;

namespace Gpui.Sample.Ios;

[Register("AppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    private GpuiApplication? _application;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        IosNative.SetEmbedded();
        _application = new GpuiApplication();
        _application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
        _application.OpenWindow(
            new MobileView(),
            new GpuiWindowOptions
            {
                Title = "GPUI.NET iOS",
                Width = 390,
                Height = 844,
                TitleBarStyle = WindowTitleBarStyle.Hidden,
            }
        );
        _application.StartEmbedded();
        return true;
    }
}
