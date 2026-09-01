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
        return new GpuiWindowSnapshot(Title, Width, Height, Left, Top, Activate, TitleBarStyle);
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

    /// <summary>Minimizes an already-open native window.</summary>
    public void Minimize() => _application.MinimizeWindow(this);

    /// <summary>Toggles an already-open native window between maximized and restored bounds.</summary>
    public void ToggleMaximize() => _application.ToggleMaximizeWindow(this);

    public void SetTitle(string title) => _application.SetWindowTitle(this, title);

    /// <summary>Changes native window content size. Runtime repositioning is not exposed by GPUI.</summary>
    public void Resize(float width, float height) => _application.ResizeWindow(this, width, height);

    internal void MarkClosed() => Volatile.Write(ref _closed, 1);
}

/// <summary>
/// Selects the native library used by a <see cref="GpuiApplication"/> instance.
/// </summary>
/// <remarks>
/// Leave <see cref="LibraryPath"/> unset to use the RID-native library supplied by the
/// <c>Gpui</c> package. An extension package may point this at its own Rust-built native host,
/// provided that host exposes the GPUI.NET C ABI contract.
/// </remarks>
public sealed class NativeRuntimeOptions
{
    /// <summary>
    /// Optional path to a native library exporting <c>gpui_dotnet_get_api</c>. The path may be
    /// absolute or relative to the process working directory.
    /// </summary>
    public string? LibraryPath { get; init; }
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
    private readonly NativeRuntimeOptions? _runtimeOptions;
    private GpuiMenu[]? _menuBar;
    private GpuiTheme _theme = GpuiTheme.Default;
    private IGpuiApplicationHost? _host;
    private ApplicationState _state;

    /// <summary>
    /// Creates an application using the package's default native host or an explicitly selected
    /// native host.
    /// </summary>
    public GpuiApplication()
        : this(null) { }

    /// <summary>Creates an application with an explicitly selected native host.</summary>
    public GpuiApplication(NativeRuntimeOptions? runtimeOptions)
    {
        _runtimeOptions = runtimeOptions;
    }

    /// <summary>The theme used by every managed View rendered by this application.</summary>
    public GpuiTheme Theme
    {
        get
        {
            lock (_gate)
            {
                return _theme;
            }
        }
    }

    /// <summary>
    /// Installs the application's semantic theme. Existing windows are invalidated so they pick
    /// up the new colors on their next render.
    /// </summary>
    public void SetTheme(GpuiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        IGpuiApplicationHost? host;
        lock (_gate)
        {
            if (_state == ApplicationState.Stopped)
            {
                throw new InvalidOperationException("The GPUI application has already stopped.");
            }

            _theme = theme;
            host = _host;
        }

        host?.SetTheme(theme);
    }

    /// <summary>
    /// Installs the application's platform menu. On macOS this is the native global menu bar;
    /// other platforms may use the definitions for their own app-side menu presentation.
    /// </summary>
    public void SetMenuBar(params GpuiMenu[] menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        if (menus.Any(menu => menu is null))
        {
            throw new ArgumentException(
                "Menu definitions cannot contain null entries.",
                nameof(menus)
            );
        }

        var copy = menus.ToArray();
        foreach (var menu in copy)
        {
            GpuiMenu.Validate(menu, "menu");
        }

        IGpuiApplicationHost? host;
        lock (_gate)
        {
            if (_state == ApplicationState.Stopped)
            {
                throw new InvalidOperationException("The GPUI application has already stopped.");
            }

            _menuBar = copy;
            host = _host;
        }

        host?.SetMenuBar(copy);
    }

    /// <summary>
    /// Adds a window. Before Run it is queued as an initial window; while Run is active it is
    /// opened through the same application event loop and may be called from any thread. The
    /// window owns <paramref name="rootView"/> until close; closing permanently unmounts it.
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
            if (rootView.IsUnmountedCore)
            {
                throw new ObjectDisposedException(
                    rootView.GetType().FullName,
                    "An unmounted View instance cannot own another window."
                );
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
                throw new InvalidOperationException(
                    "A GpuiApplication instance can run only once."
                );
            }
            if (_windows.Count == 0)
            {
                throw new InvalidOperationException(
                    "Open at least one window before running the application."
                );
            }
            _state = ApplicationState.Running;
        }

        Exception? runFailure = null;
        try
        {
            RunOnUiThread(() => NativeRuntime.Load(_runtimeOptions).Run(this));
        }
        catch (Exception exception)
        {
            runFailure = exception;
        }

        var cleanupFailures = FinishRun();
        if (runFailure is not null)
        {
            if (cleanupFailures.Count == 0)
            {
                ExceptionDispatchInfo.Capture(runFailure).Throw();
            }
            cleanupFailures.Insert(0, runFailure);
            throw new AggregateException("GPUI run and View cleanup both failed.", cleanupFailures);
        }
        if (cleanupFailures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }
        if (cleanupFailures.Count > 1)
        {
            throw new AggregateException(
                "Multiple Views failed during application cleanup.",
                cleanupFailures
            );
        }
    }

    private List<Exception> FinishRun()
    {
        View[] roots;
        lock (_gate)
        {
            _state = ApplicationState.Stopped;
            _host = null;
            foreach (var window in _windows.Values)
            {
                window.MarkClosed();
            }
            _windows.Clear();
            roots = _roots.ToArray();
        }

        List<Exception> failures = [];
        foreach (var root in roots)
        {
            try
            {
                UnmountRoot(root);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        return failures;
    }

    /// <summary>One-window convenience over the application/window ownership model.</summary>
    public static void Run(View rootView, GpuiWindowOptions? options = null) =>
        Run(rootView, options, null);

    /// <summary>
    /// One-window convenience that selects an explicit native host.
    /// </summary>
    public static void Run(
        View rootView,
        GpuiWindowOptions? options,
        NativeRuntimeOptions? runtimeOptions
    )
    {
        var application = new GpuiApplication(runtimeOptions);
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
                throw new InvalidOperationException(
                    "The GPUI application runtime is already attached."
                );
            }
            _host = host;
            return _windows
                .Values.Where(window => !window.IsClosed && !window.CloseRequested)
                .Select(window => new GpuiWindowOpenRequest(window, window.Snapshot))
                .ToArray();
        }
    }

    internal GpuiMenu[]? MenuBarSnapshot()
    {
        lock (_gate)
        {
            return _menuBar?.ToArray();
        }
    }

    internal void NativeWindowClosed(ulong id)
    {
        View root;
        lock (_gate)
        {
            if (!_windows.Remove(id, out var window))
            {
                return;
            }
            window.MarkClosed();
            root = window.RootView;
        }
        UnmountRoot(root);
    }

    internal void CloseWindow(GpuiWindow window)
    {
        IGpuiApplicationHost? host;
        View? root = null;
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
                window.MarkClosed();
                root = window.RootView;
            }
            else
            {
                window.CloseRequested = true;
            }
        }

        if (root is not null)
        {
            UnmountRoot(root);
            return;
        }

        try
        {
            host!.CloseWindow(window.Id);
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

    private void UnmountRoot(View root)
    {
        try
        {
            root.UnmountRuntime();
        }
        finally
        {
            lock (_gate)
            {
                _roots.Remove(root);
            }
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

    internal void MinimizeWindow(GpuiWindow window)
    {
        IGpuiApplicationHost host;
        lock (_gate)
        {
            ValidateOpenWindow(window);
            host =
                _host
                ?? throw new InvalidOperationException("The native window has not opened yet.");
        }
        host.MinimizeWindow(window.Id);
    }

    internal void ToggleMaximizeWindow(GpuiWindow window)
    {
        IGpuiApplicationHost host;
        lock (_gate)
        {
            ValidateOpenWindow(window);
            host =
                _host
                ?? throw new InvalidOperationException("The native window has not opened yet.");
        }
        host.ToggleMaximizeWindow(window.Id);
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
    void SetMenuBar(IReadOnlyList<GpuiMenu> menus);
    void SetTheme(GpuiTheme theme);
    void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot);
    void CloseWindow(ulong windowId);
    void ActivateWindow(ulong windowId);
    void MinimizeWindow(ulong windowId);
    void ToggleMaximizeWindow(ulong windowId);
    void SetWindowTitle(ulong windowId, string title);
    void ResizeWindow(ulong windowId, float width, float height);
}
