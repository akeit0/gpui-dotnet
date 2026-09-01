using Gpui;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed class ApplicationModelTests
{
    [Fact]
    public void FirstPreviewStartsWithVersionOneProtocols()
    {
        Assert.Equal(1u, NativeConstants.AbiVersion);
        Assert.Equal(1u, SemanticRegistry.SchemaVersion);
    }

    [Fact]
    public void ApplicationRequiresAnInitialWindow()
    {
        var application = new GpuiApplication();

        Assert.Throws<InvalidOperationException>(application.Run);
    }

    [Fact]
    public void NativeRuntimeRejectsAnEmptyExplicitNativeLibraryPath()
    {
        Assert.Throws<ArgumentException>(() =>
            NativeRuntime.Load(new NativeRuntimeOptions { LibraryPath = " " })
        );
    }

    [Fact]
    public void RunFailureStillTerminallyUnmountsPendingRoots()
    {
        var application = new GpuiApplication(new NativeRuntimeOptions { LibraryPath = " " });
        var root = new ProbeView();
        var window = application.OpenWindow(root);

        Assert.Throws<ArgumentException>(application.Run);

        Assert.True(window.IsClosed);
        Assert.True(root.Unmounted);
    }

    [Fact]
    public void WindowOptionsValidatePositionSizeAndTitleBar()
    {
        var application = new GpuiApplication();

        Assert.Throws<ArgumentException>(() =>
            application.OpenWindow(new ProbeView(), new GpuiWindowOptions { Left = 10 })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.OpenWindow(new ProbeView(), new GpuiWindowOptions { Width = float.NaN })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.OpenWindow(
                new ProbeView(),
                new GpuiWindowOptions { TitleBarStyle = (WindowTitleBarStyle)99 }
            )
        );
    }

    [Fact]
    public void NativeMenuBarDefinitionsCanBeConfiguredBeforeRun()
    {
        var application = new GpuiApplication();
        var invoked = false;

        application.SetMenuBar(
            new GpuiMenu(
                "File",
                GpuiMenuItem.Command("Open", () => invoked = true),
                GpuiMenuItem.Submenu(
                    new GpuiMenu("Recent", GpuiMenuItem.Command("Example", () => invoked = true))
                ),
                GpuiMenuItem.Separator()
            )
        );

        Assert.NotNull(application.MenuBarSnapshot());
        Assert.False(invoked);
    }

    [Fact]
    public void PendingWindowCanBeConfiguredAndClosed()
    {
        var application = new GpuiApplication();
        var root = new ProbeView();
        var window = application.OpenWindow(root);

        window.SetTitle("Updated");
        window.Resize(900, 600);

        Assert.Equal("Updated", window.Snapshot.Title);
        Assert.Equal(900, window.Snapshot.Width);
        Assert.Equal(600, window.Snapshot.Height);
        Assert.Equal(WindowTitleBarStyle.System, window.Snapshot.TitleBarStyle);
        Assert.Throws<InvalidOperationException>(window.Minimize);
        Assert.Throws<InvalidOperationException>(window.ToggleMaximize);

        window.Close();

        Assert.True(window.IsClosed);
        Assert.True(root.Unmounted);
        window.Close();
        Assert.Throws<InvalidOperationException>(application.Run);
    }

    [Fact]
    public void EachApplicationWindowHasIndependentIdentityAndRootOwnership()
    {
        var application = new GpuiApplication();
        var firstRoot = new ProbeView();
        var first = application.OpenWindow(firstRoot);
        var second = application.OpenWindow(
            new ProbeView(),
            new GpuiWindowOptions { TitleBarStyle = WindowTitleBarStyle.Custom }
        );

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Snapshot.Activate);
        Assert.True(second.Snapshot.Activate);
        Assert.Equal(WindowTitleBarStyle.Custom, second.Snapshot.TitleBarStyle);
        Assert.Throws<InvalidOperationException>(() => application.OpenWindow(firstRoot));

        first.Close();
        Assert.Throws<ObjectDisposedException>(() => application.OpenWindow(firstRoot));
        var reopened = application.OpenWindow(
            new ProbeView(),
            new GpuiWindowOptions { Activate = false }
        );
        Assert.NotEqual(first.Id, reopened.Id);
    }

    private sealed class ProbeView : View
    {
        internal bool Unmounted => IsUnmounted;

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
