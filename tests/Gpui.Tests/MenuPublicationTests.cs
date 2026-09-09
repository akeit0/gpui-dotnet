using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop;
using Gpui.Interop.Internal;

namespace Gpui.Tests;

public sealed unsafe class MenuPublicationTests
{
    [ThreadStatic]
    private static int _status;

    [ThreadStatic]
    private static ulong _action;

    [ThreadStatic]
    private static ulong _generation;

    [Fact]
    public void NativeDispatchFailureDoesNotRevokeVisibleMenuCallbacks()
    {
        var api = new GpuiDotnetApiV3 { dispatch_application_menu = &Dispatch };
        var constructor = typeof(NativeRuntime)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var runtime = (NativeRuntime)
            constructor.Invoke([Pointer.Box(&api, typeof(GpuiDotnetApiV3*)), null]);
        var application = new GpuiApplication();
        application.Execution.BindThread();
        var host = new ManagedApplication(runtime, 1, application);
        var calls = 0;
        _status = 0;
        host.SetMenuBar([new GpuiMenu("File", GpuiMenuItem.Command("Old", () => calls++))]);
        var oldAction = _action;
        host.MenuApplied(_generation);
        _status = -65;
        Assert.Throws<InvalidOperationException>(() =>
            host.SetMenuBar([
                new GpuiMenu("File", GpuiMenuItem.Command("Rejected", () => calls += 10)),
            ])
        );
        Assert.Equal(0, host.MenuAction(oldAction));
        Assert.Equal(-64, host.MenuAction(_action));
        _status = 0;
        host.SetMenuBar([new GpuiMenu("File", GpuiMenuItem.Command("New", () => calls += 100))]);
        Assert.Equal(0, host.MenuAction(oldAction));
        host.MenuApplied(_generation);
        Assert.Equal(-64, host.MenuAction(oldAction));
        Assert.Equal(0, host.MenuAction(_action));
        Assert.Equal(102, calls);
        host.Stop();
        Assert.Equal(-65, host.MenuAction(_action));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Dispatch(ulong applicationId, NativeMenuCommand* command)
    {
        _generation = command->generation;
        _action = command->items[1].action_id;
        return _status;
    }
}
