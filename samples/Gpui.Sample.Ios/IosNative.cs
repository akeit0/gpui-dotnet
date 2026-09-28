using System.Runtime.InteropServices;

namespace Gpui.Sample.Ios;

internal static partial class IosNative
{
    [LibraryImport("__Internal", EntryPoint = "gpui_ios_set_embedded")]
    internal static partial void SetEmbedded();

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_get_window")]
    internal static partial nint GetWindow();

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_view_controller")]
    internal static partial nint GetViewController(nint window);

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_layout_view")]
    internal static partial void LayoutView(nint window);

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_request_frame")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool RequestFrame(nint window);

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_did_become_active")]
    internal static partial void DidBecomeActive(nint application = 0);

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_will_resign_active")]
    internal static partial void WillResignActive(nint application = 0);

    [LibraryImport("__Internal", EntryPoint = "gpui_ios_did_enter_background")]
    internal static partial void DidEnterBackground(nint application = 0);
}
