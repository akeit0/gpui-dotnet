using System.Runtime.InteropServices;
using Gpui;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed class ApplicationModelTests
{
    [Fact]
    public void UsesExpectedProtocolVersions()
    {
        Assert.Equal(7u, NativeConstants.AbiVersion);
        Assert.Equal(1u, SemanticRegistry.SchemaVersion);
    }

    [Fact]
    public unsafe void AcceptanceCallbackExtendsTheNativeCallbackTable()
    {
        Assert.Equal(12 * IntPtr.Size, sizeof(ManagedCallbacks));
        Assert.Equal(
            11 * IntPtr.Size,
            (int)Marshal.OffsetOf<ManagedCallbacks>(nameof(ManagedCallbacks.accept_artifact))
        );
        Assert.Equal(16, sizeof(NativeArtifactKey));
        Assert.Equal(
            8,
            (int)Marshal.OffsetOf<NativeArtifactKey>(nameof(NativeArtifactKey.artifact))
        );
        Assert.Equal(
            16 + 8 * IntPtr.Size,
            (int)Marshal.OffsetOf<GpuiDotnetApiV3>(nameof(GpuiDotnetApiV3.invalidate_artifacts))
        );
        Assert.Equal(
            9 * IntPtr.Size,
            (int)
                System.Runtime.InteropServices.Marshal.OffsetOf<ManagedCallbacks>(
                    nameof(ManagedCallbacks.render_completed)
                )
        );
        Assert.Equal(
            10 * IntPtr.Size,
            (int)
                System.Runtime.InteropServices.Marshal.OffsetOf<ManagedCallbacks>(
                    nameof(ManagedCallbacks.release_artifact)
                )
        );
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
    public void RunFailureClosesPendingWindowsWithoutConstructingRoots()
    {
        var application = new GpuiApplication(new NativeRuntimeOptions { LibraryPath = " " });
        var root = ProbeView.Spec();
        var window = application.OpenWindow(root);

        Assert.Throws<ArgumentException>(application.Run);

        Assert.True(window.IsClosed);
        Assert.Equal(0, ProbeView.Constructions);
    }

    [Fact]
    public void WindowOptionsValidatePositionSizeAndTitleBar()
    {
        var application = new GpuiApplication();

        Assert.Throws<ArgumentException>(() =>
            application.OpenWindow(ProbeView.Spec(), new GpuiWindowOptions { Left = 10 })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.OpenWindow(ProbeView.Spec(), new GpuiWindowOptions { Width = float.NaN })
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            application.OpenWindow(
                ProbeView.Spec(),
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
        var root = ProbeView.Spec();
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
        Assert.Equal(0, ProbeView.Constructions);
        window.Close();
        Assert.Throws<InvalidOperationException>(application.Run);
    }

    [Fact]
    public void EachApplicationWindowHasIndependentIdentityAndRootOwnership()
    {
        var application = new GpuiApplication();
        var firstRoot = ProbeView.Spec();
        var first = application.OpenWindow(firstRoot);
        var second = application.OpenWindow(
            ProbeView.Spec(),
            new GpuiWindowOptions { TitleBarStyle = WindowTitleBarStyle.Custom }
        );

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Snapshot.Activate);
        Assert.True(second.Snapshot.Activate);
        Assert.Equal(WindowTitleBarStyle.Custom, second.Snapshot.TitleBarStyle);
        var third = application.OpenWindow(firstRoot);
        Assert.NotEqual(first.Id, third.Id);

        first.Close();
        Assert.False(application.OpenWindow(firstRoot).IsClosed);
        var reopened = application.OpenWindow(
            ProbeView.Spec(),
            new GpuiWindowOptions { Activate = false }
        );
        Assert.NotEqual(first.Id, reopened.Id);
    }

    [Fact]
    public unsafe void ImageCacheBudgetPayloadMatchesNativeLayout()
    {
        Assert.Equal(24, sizeof(NativeImageCacheBudget));
        Assert.Equal(
            0,
            (int)Marshal.OffsetOf<NativeImageCacheBudget>(nameof(NativeImageCacheBudget.Version))
        );
        Assert.Equal(
            4,
            (int)Marshal.OffsetOf<NativeImageCacheBudget>(nameof(NativeImageCacheBudget.Reserved))
        );
        Assert.Equal(
            8,
            (int)Marshal.OffsetOf<NativeImageCacheBudget>(nameof(NativeImageCacheBudget.MaxBytes))
        );
        Assert.Equal(
            16,
            (int)Marshal.OffsetOf<NativeImageCacheBudget>(nameof(NativeImageCacheBudget.MaxEntries))
        );
    }

    [Fact]
    public void ImageCacheBudgetIsStoredUntilHostAttaches()
    {
        var application = new GpuiApplication();

        Assert.Null(application.ImageCacheBudgetSnapshot());

        application.SetImageCacheBudget(1024, 8);

        Assert.Equal((1024UL, 8UL), application.ImageCacheBudgetSnapshot());
    }

    [Fact]
    public void EvictImageValidatesPathWithoutAHost()
    {
        var application = new GpuiApplication();

        application.EvictImage("C:/pictures/photo.png");

        Assert.Throws<ArgumentException>(() => application.EvictImage(" "));
    }

    private sealed class ProbeView : View, IGeneratedViewFactory<ProbeView>
    {
        internal static int Constructions;

        public ProbeView(ViewConstruction construction)
            : base(construction) => Constructions++;

        public static ProbeView CreateGpuiView(ViewConstruction construction) => new(construction);

        internal static ViewSpec<ProbeView> Spec() => default;

        internal bool Unmounted => IsUnmounted;

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
