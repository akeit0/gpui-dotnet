using System.Collections.Concurrent;
using System.Reflection;
using Gpui;

namespace Gpui.Tests;

public sealed class ApplicationIngressTests
{
    [Fact]
    public async Task StartupOrdersAllInitialWindowsBeforeConcurrentCommands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var application = new GpuiApplication();
        var first = application.OpenWindow(Probe.Spec());
        var second = application.OpenWindow(Probe.Spec());
        application.SetImageCacheBudget(100, 4);
        var host = new Host();
        using var opening = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var producing = new ManualResetEventSlim();
        host.Opening = window =>
        {
            if (window != first)
                return;
            opening.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), cancellationToken));
        };
        MarkRunning(application);
        var startup = Task.Run(() => application.AttachHost(host), cancellationToken);
        Task? producer = null;
        try
        {
            Assert.True(opening.Wait(TimeSpan.FromSeconds(10), cancellationToken));
            // Model reads remain available while native ingress is paused.
            Assert.NotNull(
                await Task.Run(() => application.Theme, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            );
            producer = Task.Run(
                () =>
                {
                    producing.Set();
                    second.SetTitle("Updated");
                    second.Resize(900, 600);
                    second.Activate();
                    second.ToggleFullscreen();
                    second.ShowToast(new GpuiToast("save", "Saved"));
                    second.DismissToast("save");
                    second.ClearToasts();
                    application.SetImageCacheBudget(200, 8);
                    application.SetTheme(GpuiTheme.Default);
                    second.Close();
                },
                cancellationToken
            );
            Assert.True(producing.Wait(TimeSpan.FromSeconds(10), cancellationToken));
        }
        finally
        {
            release.Set();
        }
        await startup.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await producer!.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(
            new[]
            {
                "theme",
                "budget:100:4",
                $"open:{first.Id}",
                $"open:{second.Id}",
                $"title:{second.Id}:Updated",
                $"size:{second.Id}",
                $"activate:{second.Id}",
                $"fullscreen:{second.Id}",
                $"toast:{second.Id}",
                $"dismiss-toast:{second.Id}:save",
                $"clear-toasts:{second.Id}",
                "budget:200:8",
                "theme",
                $"close:{second.Id}",
            },
            host.Commands.ToArray()
        );
        Assert.True(second.CloseRequested);
        application.NativeWindowClosed(second.Id);
        Assert.True(second.IsClosed);
    }

    [Fact]
    public void PendingCloseAndLatestSettingsAreFoldedIntoStartup()
    {
        var application = new GpuiApplication();
        var cancelled = application.OpenWindow(Probe.Spec());
        var remaining = application.OpenWindow(Probe.Spec());
        cancelled.Close();
        remaining.SetTitle("Latest");
        application.SetImageCacheBudget(123, 5);
        MarkRunning(application);
        var host = new Host();
        application.AttachHost(host);
        Assert.True(cancelled.IsClosed);
        Assert.Equal(
            new[] { "theme", "budget:123:5", $"open:{remaining.Id}" },
            host.Commands.ToArray()
        );
        Assert.Equal("Latest", host.OpenedTitle);
    }

    [Fact]
    public void NativeMenuRejectionPreservesAcceptedSnapshot()
    {
        var application = new GpuiApplication();
        var accepted = new GpuiMenu("Accepted");
        application.SetMenuBar(accepted);
        MarkRunning(application);
        var host = new Host();
        application.AttachHost(host);
        host.RejectMenu = true;
        Assert.Throws<InvalidOperationException>(() =>
            application.SetMenuBar(new GpuiMenu("Rejected"))
        );
        Assert.Same(accepted, Assert.Single(application.MenuBarSnapshot()!));
    }

    private static void MarkRunning(GpuiApplication application)
    {
        // Exercise attachment without starting a native event loop or constructing Views.
        var state = typeof(GpuiApplication).GetField(
            "_state",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        state.SetValue(application, Enum.Parse(state.FieldType, "Running"));
    }

    private sealed class Host : IGpuiApplicationHost
    {
        internal readonly ConcurrentQueue<string> Commands = new();
        internal Action<GpuiWindow>? Opening;
        internal string? OpenedTitle;
        internal bool RejectMenu;

        public void SetTheme(GpuiTheme theme) => Commands.Enqueue("theme");

        public void SetImageCacheBudget(ulong maxBytes, ulong maxEntries) =>
            Commands.Enqueue($"budget:{maxBytes}:{maxEntries}");

        public void SetMenuBar(IReadOnlyList<GpuiMenu> menus)
        {
            if (RejectMenu)
                throw new InvalidOperationException("Injected native rejection.");
        }

        public void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot)
        {
            Commands.Enqueue($"open:{window.Id}");
            OpenedTitle = snapshot.Title;
            Opening?.Invoke(window);
        }

        public void CloseWindow(ulong id) => Commands.Enqueue($"close:{id}");

        public void ActivateWindow(ulong id) => Commands.Enqueue($"activate:{id}");

        public void SetWindowTitle(ulong id, string title) =>
            Commands.Enqueue($"title:{id}:{title}");

        public void ResizeWindow(ulong id, float width, float height) =>
            Commands.Enqueue($"size:{id}");

        public void EvictImage(string path) { }

        public void MinimizeWindow(ulong id) { }

        public void ToggleMaximizeWindow(ulong id) { }

        public void ToggleFullscreenWindow(ulong id) => Commands.Enqueue($"fullscreen:{id}");

        public void ShowWindowToast(ulong id, byte[] payload) => Commands.Enqueue($"toast:{id}");

        public void DismissWindowToast(ulong id, string toastId) =>
            Commands.Enqueue($"dismiss-toast:{id}:{toastId}");

        public void ClearWindowToasts(ulong id) => Commands.Enqueue($"clear-toasts:{id}");
    }

    private sealed class Probe(ViewConstruction construction)
        : View(construction),
            IGeneratedViewFactory<Probe>
    {
        public static Probe CreateGpuiView(ViewConstruction construction) => new(construction);

        internal static ViewSpec<Probe> Spec() => default;

        protected override Element Render(ref RenderContext ui) => ui.Div();
    }
}
