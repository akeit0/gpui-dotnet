using System.Runtime.ExceptionServices;
using Gpui.Interop;

namespace Gpui;

/// <summary>Native title-bar presentation selected when a window is opened.</summary>
public enum WindowTitleBarStyle : ushort
{
    /// <summary>Use platform-provided title-bar chrome.</summary>
    System,

    /// <summary>Extend managed content into the title bar and provide custom control regions.</summary>
    Custom,

    /// <summary>Remove title-bar chrome entirely.</summary>
    Hidden,
}

/// <summary>Initial native window placement and presentation.</summary>
public sealed class GpuiWindowOptions
{
    public string Title { get; init; } = "GPUI.NET";
    public float Width { get; init; } = 720;
    public float Height { get; init; } = 480;
    public float? Left { get; init; }
    public float? Top { get; init; }
    public bool Activate { get; init; } = true;
    public WindowTitleBarStyle TitleBarStyle { get; init; } = WindowTitleBarStyle.System;

    internal GpuiWindowSnapshot ValidateAndSnapshot()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ValidateSize(Width, Height);
        if (!Enum.IsDefined(TitleBarStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(TitleBarStyle));
        }
        if (Left.HasValue != Top.HasValue)
        {
            throw new ArgumentException("Left and Top must either both be set or both be omitted.");
        }
        if (Left is { } left && (!float.IsFinite(left) || !float.IsFinite(Top!.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(Left));
        }
        return new GpuiWindowSnapshot(
            Title,
            Width,
            Height,
            Left,
            Top,
            Activate,
            TitleBarStyle
        );
    }

    internal static void ValidateSize(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (!float.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
    }
}

/// <summary>A stable managed handle to one application-owned native window.</summary>
public sealed class GpuiWindow
{
    private readonly GpuiApplication _application;
    private int _closed;

    internal GpuiWindow(
        GpuiApplication application,
        ulong id,
        View rootView,
        GpuiWindowSnapshot snapshot
    )
    {
        _application = application;
        Id = id;
        RootView = rootView;
        Snapshot = snapshot;
    }

    /// <summary>Stable for this window's full pending/open/closed lifetime.</summary>
    public ulong Id { get; }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    internal View RootView { get; }
    internal GpuiWindowSnapshot Snapshot { get; set; }
    internal bool CloseRequested { get; set; }

    public void Close() => _application.CloseWindow(this);

    public void Activate() => _application.ActivateWindow(this);

    public void SetTitle(string title) => _application.SetWindowTitle(this, title);

    /// <summary>Changes native window content size. Runtime repositioning is not exposed by GPUI.</summary>
    public void Resize(float width, float height) => _application.ResizeWindow(this, width, height);

    internal void MarkClosed() => Volatile.Write(ref _closed, 1);
}

/// <summary>
/// Owns one native GPUI application event loop and any number of independent managed windows.
/// Configure at least one window before calling <see cref="Run()"/>.
/// </summary>
public sealed class GpuiApplication
{
    private static long _nextWindowId;

    private readonly object _gate = new();
    private readonly Dictionary<ulong, GpuiWindow> _windows = [];
    private readonly HashSet<View> _roots = new(ReferenceEqualityComparer.Instance);
    private IGpuiApplicationHost? _host;
    private ApplicationState _state;

    /// <summary>
    /// Adds a window. Before Run it is queued as an initial window; while Run is active it is
    /// opened through the same application event loop and may be called from any thread.
    /// </summary>
    public GpuiWindow OpenWindow(View rootView, GpuiWindowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rootView);
        var snapshot = (options ?? new GpuiWindowOptions()).ValidateAndSnapshot();
        var id = checked((ulong)Interlocked.Increment(ref _nextWindowId));
        var window = new GpuiWindow(this, id, rootView, snapshot);
        IGpuiApplicationHost? host;

        lock (_gate)
        {
            if (_state == ApplicationState.Stopped)
            {
                throw new InvalidOperationException("The GPUI application has already stopped.");
            }
            if (rootView.IsMountedCore || !_roots.Add(rootView))
            {
                throw new InvalidOperationException(
                    "A View instance can be the root of only one open GPUI window."
                );
            }
            if (snapshot.Activate)
            {
                ClearPendingActivation();
            }
            _windows.Add(id, window);
            host = _host;
        }

        if (host is not null)
        {
            try
            {
                host.OpenWindow(window, snapshot);
            }
            catch
            {
                NativeWindowClosed(id);
                throw;
            }
        }
        return window;
    }

    /// <summary>Runs until the last application-owned window closes.</summary>
    public void Run()
    {
        lock (_gate)
        {
            if (_state != ApplicationState.Created)
            {
                throw new InvalidOperationException("A GpuiApplication instance can run only once.");
            }
            if (_windows.Count == 0)
            {
                throw new InvalidOperationException("Open at least one window before running the application.");
            }
            _state = ApplicationState.Running;
        }

        try
        {
            RunOnUiThread(() => NativeRuntime.Load().Run(this));
        }
        finally
        {
            lock (_gate)
            {
                _state = ApplicationState.Stopped;
                _host = null;
                foreach (var window in _windows.Values)
                {
                    window.MarkClosed();
                }
                _windows.Clear();
                _roots.Clear();
            }
        }
    }

    /// <summary>One-window convenience over the application/window ownership model.</summary>
    public static void Run(View rootView, GpuiWindowOptions? options = null)
    {
        var application = new GpuiApplication();
        application.OpenWindow(rootView, options);
        application.Run();
    }

    internal GpuiWindowOpenRequest[] AttachHost(IGpuiApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_gate)
        {
            if (_state != ApplicationState.Running || _host is not null)
            {
                throw new InvalidOperationException("The GPUI application runtime is already attached.");
            }
            _host = host;
            return _windows.Values
                .Where(window => !window.IsClosed && !window.CloseRequested)
                .Select(window => new GpuiWindowOpenRequest(window, window.Snapshot))
                .ToArray();
        }
    }

    internal void NativeWindowClosed(ulong id)
    {
        lock (_gate)
        {
            if (!_windows.Remove(id, out var window))
            {
                return;
            }
            _roots.Remove(window.RootView);
            window.MarkClosed();
        }
    }

    internal void CloseWindow(GpuiWindow window)
    {
        IGpuiApplicationHost? host;
        lock (_gate)
        {
            ValidateOwnedWindow(window);
            if (window.IsClosed || window.CloseRequested)
            {
                return;
            }
            host = _host;
            if (host is null)
            {
                _windows.Remove(window.Id);
                _roots.Remove(window.RootView);
                window.MarkClosed();
                return;
            }
            window.CloseRequested = true;
        }

        try
        {
            host.CloseWindow(window.Id);
        }
        catch
        {
            lock (_gate)
            {
                if (!window.IsClosed)
                {
                    window.CloseRequested = false;
                }
            }
            throw;
        }
    }

    internal void ActivateWindow(GpuiWindow window)
    {
        IGpuiApplicationHost? host;
        lock (_gate)
        {
            ValidateOpenWindow(window);
            host = _host;
            if (host is null)
            {
                ClearPendingActivation();
                window.Snapshot = window.Snapshot with { Activate = true };
                return;
            }
        }
        host.ActivateWindow(window.Id);
    }

    internal void SetWindowTitle(GpuiWindow window, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        IGpuiApplicationHost? host;
        lock (_gate)
        {
            ValidateOpenWindow(window);
            window.Snapshot = window.Snapshot with { Title = title };
            host = _host;
        }
        host?.SetWindowTitle(window.Id, title);
    }

    internal void ResizeWindow(GpuiWindow window, float width, float height)
    {
        GpuiWindowOptions.ValidateSize(width, height);
        IGpuiApplicationHost? host;
        lock (_gate)
        {
            ValidateOpenWindow(window);
            window.Snapshot = window.Snapshot with { Width = width, Height = height };
            host = _host;
        }
        host?.ResizeWindow(window.Id, width, height);
    }

    private void ValidateOwnedWindow(GpuiWindow window)
    {
        if (!_windows.TryGetValue(window.Id, out var owned) || !ReferenceEquals(owned, window))
        {
            if (window.IsClosed)
            {
                return;
            }
            throw new InvalidOperationException("The window does not belong to this application.");
        }
    }

    private void ValidateOpenWindow(GpuiWindow window)
    {
        ValidateOwnedWindow(window);
        if (window.IsClosed || window.CloseRequested)
        {
            throw new InvalidOperationException("The GPUI window is closed or closing.");
        }
    }

    private void ClearPendingActivation()
    {
        foreach (var existing in _windows.Values)
        {
            existing.Snapshot = existing.Snapshot with { Activate = false };
        }
    }

    private static void RunOnUiThread(Action run)
    {
        if (
            !OperatingSystem.IsWindows()
            || Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
        )
        {
            run();
            return;
        }

        Exception? failure = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = false,
            Name = "GPUI UI",
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private enum ApplicationState
    {
        Created,
        Running,
        Stopped,
    }
}

internal readonly record struct GpuiWindowSnapshot(
    string Title,
    float Width,
    float Height,
    float? Left,
    float? Top,
    bool Activate,
    WindowTitleBarStyle TitleBarStyle
);

internal readonly record struct GpuiWindowOpenRequest(
    GpuiWindow Window,
    GpuiWindowSnapshot Snapshot
);

internal interface IGpuiApplicationHost
{
    void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot);
    void CloseWindow(ulong windowId);
    void ActivateWindow(ulong windowId);
    void SetWindowTitle(ulong windowId, string title);
    void ResizeWindow(ulong windowId, float width, float height);
}
