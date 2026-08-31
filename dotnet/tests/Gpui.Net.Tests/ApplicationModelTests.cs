using Gpui;

namespace Gpui.Net.Tests;

public sealed class ApplicationModelTests
{
    [Fact]
    public void ApplicationRequiresAnInitialWindow()
    {
        var application = new GpuiApplication();

        Assert.Throws<InvalidOperationException>(application.Run);
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
    public void PendingWindowCanBeConfiguredAndClosed()
    {
        var application = new GpuiApplication();
        var window = application.OpenWindow(new ProbeView());

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
        var reopened = application.OpenWindow(
            firstRoot,
            new GpuiWindowOptions { Activate = false }
        );
        Assert.NotEqual(first.Id, reopened.Id);
    }

    private sealed class ProbeView : View
    {
        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
