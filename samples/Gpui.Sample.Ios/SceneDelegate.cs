using CoreAnimation;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Gpui.Sample.Ios;

[Register("SceneDelegate")]
internal sealed class SceneDelegate : UIWindowSceneDelegate
{
    private CADisplayLink? _displayLink;
    private nint _gpuiWindow;

    public override UIWindow? Window { get; set; }

    public override void WillConnect(
        UIScene scene,
        UISceneSession session,
        UISceneConnectionOptions connectionOptions
    )
    {
        if (scene is not UIWindowScene windowScene)
        {
            return;
        }

        _gpuiWindow = IosNative.GetWindow();
        var controllerPointer = IosNative.GetViewController(_gpuiWindow);
        var controller = Runtime.GetNSObject<UIViewController>(controllerPointer, owns: false)
            ?? throw new InvalidOperationException("gpui-mobile did not create a view controller.");

        Window = new UIWindow(windowScene)
        {
            RootViewController = new GpuiHostViewController(controller, _gpuiWindow),
        };
        Window.MakeKeyAndVisible();

        _displayLink = CADisplayLink.Create(() => IosNative.RequestFrame(_gpuiWindow));
        _displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    public override void DidBecomeActive(UIScene scene) => IosNative.DidBecomeActive();

    public override void WillResignActive(UIScene scene) => IosNative.WillResignActive();

    public override void DidEnterBackground(UIScene scene) => IosNative.DidEnterBackground();

    public override void DidDisconnect(UIScene scene)
    {
        _displayLink?.Invalidate();
        _displayLink = null;
    }
}

internal sealed class GpuiHostViewController : UIViewController
{
    private readonly UIViewController _content;
    private readonly nint _gpuiWindow;

    internal GpuiHostViewController(UIViewController content, nint gpuiWindow)
    {
        _content = content;
        _gpuiWindow = gpuiWindow;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.FromRGB(15, 23, 42);
        AddChildViewController(_content);
        View!.AddSubview(_content.View!);
        _content.DidMoveToParentViewController(this);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        // UIKit owns system-bar and cutout geometry. Give GPUI the safe content
        // frame so ordinary semantic layouts cannot render underneath it.
        _content.View!.Frame = View!.SafeAreaLayoutGuide.LayoutFrame;
        IosNative.LayoutView(_gpuiWindow);
        _ = IosNative.RequestFrame(_gpuiWindow);
    }
}
