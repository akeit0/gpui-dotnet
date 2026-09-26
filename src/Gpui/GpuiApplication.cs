using System.Runtime.ExceptionServices;
using System.Text;
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

/// <summary>Native window state selected when the window first opens.</summary>
public enum WindowInitialState : ushort
{
    /// <summary>Open at the requested content size and position.</summary>
    Normal,

    /// <summary>Open maximized, retaining the requested bounds for restore.</summary>
    Maximized,

    /// <summary>Open fullscreen, retaining the requested bounds for restore.</summary>
    Fullscreen,
}

/// <summary>Native placement captured when a window closes. Dimensions are restore bounds.</summary>
public readonly record struct GpuiWindowPlacement(
    float Left,
    float Top,
    float Width,
    float Height,
    WindowInitialState State
);

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
    public WindowInitialState InitialState { get; init; } = WindowInitialState.Normal;
    public float? MinimumWidth { get; init; }
    public float? MinimumHeight { get; init; }

    internal GpuiWindowSnapshot ValidateAndSnapshot()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ValidateSize(Width, Height);
        if (!Enum.IsDefined(TitleBarStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(TitleBarStyle));
        }
        if (!Enum.IsDefined(InitialState))
        {
            throw new ArgumentOutOfRangeException(nameof(InitialState));
        }
        if (Left.HasValue != Top.HasValue)
        {
            throw new ArgumentException("Left and Top must either both be set or both be omitted.");
        }
        if (Left is { } left && (!float.IsFinite(left) || !float.IsFinite(Top!.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(Left));
        }
        if (MinimumWidth.HasValue != MinimumHeight.HasValue)
        {
            throw new ArgumentException(
                "MinimumWidth and MinimumHeight must either both be set or both be omitted."
            );
        }
        if (MinimumWidth is { } minimumWidth)
        {
            if (!float.IsFinite(minimumWidth) || minimumWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(MinimumWidth));
            if (!float.IsFinite(MinimumHeight!.Value) || MinimumHeight.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(MinimumHeight));
            if (minimumWidth > Width || MinimumHeight.Value > Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinimumWidth),
                    "The minimum size must not exceed the initial restore size."
                );
            }
            if (
                Encoding.UTF8.GetByteCount(Title) > Interop.Internal.WindowOpenPayload.MaxTitleBytes
            )
            {
                throw new ArgumentException(
                    "A window title with minimum size must fit within 4096 UTF-8 bytes.",
                    nameof(Title)
                );
            }
        }
        return new GpuiWindowSnapshot(
            Title,
            Width,
            Height,
            Left,
            Top,
            Activate,
            TitleBarStyle,
            InitialState,
            MinimumWidth,
            MinimumHeight
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
    private readonly object _placementGate = new();
    private GpuiWindowPlacement? _finalPlacement;
    internal GpuiApplication Application => _application;
    private int _opened;
    private int _closed;

    internal GpuiWindow(
        GpuiApplication application,
        ulong id,
        RootViewDeclaration rootView,
        GpuiWindowSnapshot snapshot
    )
    {
        _application = application;
        Id = id;
        _rootDeclaration = rootView;
        Snapshot = snapshot;
    }

    /// <summary>Stable for this window's full pending/open/closed lifetime.</summary>
    public ulong Id { get; }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>True after the native window opens and before it closes.</summary>
    public bool IsOpen => Volatile.Read(ref _opened) != 0 && !IsClosed;

    /// <summary>Raised on the GPUI application thread after native window creation.</summary>
    public event Action<GpuiWindow>? Opened;

    /// <summary>Raised on the GPUI application thread after managed View teardown.</summary>
    public event Action<GpuiWindow>? Closed;

    /// <summary>
    /// Final native placement after this window closes successfully. Save it in application
    /// storage and pass its bounds and state to a later <see cref="GpuiWindowOptions"/>.
    /// </summary>
    public GpuiWindowPlacement? FinalPlacement
    {
        get
        {
            lock (_placementGate)
                return _finalPlacement;
        }
    }

    internal void SetFinalPlacement(GpuiWindowPlacement placement)
    {
        lock (_placementGate)
            _finalPlacement = placement;
    }

    private RootViewDeclaration? _rootDeclaration;

    internal RootViewDeclaration TakeRootDeclaration() =>
        Interlocked.Exchange(ref _rootDeclaration, null)
        ?? throw new InvalidOperationException("The window root was already consumed or closed.");

    internal GpuiWindowSnapshot Snapshot { get; set; }
    internal bool CloseRequested { get; set; }

    public void Close() => _application.CloseWindow(this);

    public void Activate() => _application.ActivateWindow(this);

    /// <summary>Minimizes an already-open native window.</summary>
    public void Minimize() => _application.MinimizeWindow(this);

    /// <summary>Toggles an already-open native window between maximized and restored bounds.</summary>
    public void ToggleMaximize() => _application.ToggleMaximizeWindow(this);

    /// <summary>Toggles fullscreen for an already-open native window.</summary>
    public void ToggleFullscreen() => _application.ToggleFullscreenWindow(this);

    /// <summary>Shows or replaces a window-owned notification.</summary>
    public void ShowToast(GpuiToast toast) => _application.ShowWindowToast(this, toast);

    /// <summary>Dismisses a notification by its stable ID.</summary>
    public void DismissToast(string id) => _application.DismissWindowToast(this, id);

    /// <summary>Dismisses all notifications in this window.</summary>
    public void ClearToasts() => _application.ClearWindowToasts(this);

    public void SetTitle(string title) => _application.SetWindowTitle(this, title);

    /// <summary>Changes native window content size. Runtime repositioning is not exposed by GPUI.</summary>
    public void Resize(float width, float height) => _application.ResizeWindow(this, width, height);

    internal void MarkClosed()
    {
        Interlocked.Exchange(ref _rootDeclaration, null);
        Volatile.Write(ref _closed, 1);
    }

    internal void MarkOpened() => Volatile.Write(ref _opened, 1);

    internal void RaiseOpened() => Opened?.Invoke(this);

    internal void RaiseClosed() => Closed?.Invoke(this);

    internal bool WasOpened => Volatile.Read(ref _opened) != 0;
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

    /// <summary>
    /// Extension schemas that the selected host must implement. Compatibility is checked before
    /// the native application event loop starts.
    /// </summary>
    public IReadOnlyList<NativeExtensionRequirement> Extensions { get; init; } = [];
}

/// <summary>
/// Owns one native GPUI application event loop and any number of independent managed windows.
/// Configure at least one window before calling <see cref="Run()"/>.
/// </summary>
public sealed class GpuiApplication
{
    internal Interop.Internal.ApplicationExecution Execution { get; } = new();

    private static long _nextWindowId;

    private readonly object _gate = new();

    // Orders model updates with native enqueueing without holding the model lock across FFI.
    private readonly object _ingressGate = new();
    private readonly Dictionary<ulong, GpuiWindow> _windows = [];
    private readonly NativeRuntimeOptions? _runtimeOptions;
    private GpuiMenu[]? _menuBar;
    private GpuiTheme _theme = GpuiTheme.Default;
    private (ulong MaxBytes, ulong MaxEntries)? _imageCacheBudget;
    private IGpuiApplicationHost? _host;
    private ApplicationState _state;

    /// <summary>Raised after any application window opens natively.</summary>
    public event Action<GpuiWindow>? WindowOpened;

    /// <summary>Raised after any opened window's managed View teardown.</summary>
    public event Action<GpuiWindow>? WindowClosed;

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
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            ArgumentNullException.ThrowIfNull(theme);
            IGpuiApplicationHost? host;
            lock (_gate)
            {
                if (_state == ApplicationState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The GPUI application has already stopped."
                    );
                }

                _theme = theme;
                host = _host;
            }

            host?.SetTheme(theme);
        }
    }

    /// <summary>
    /// Overrides the native image-cache spill budget: recently-visible images kept decoded
    /// past their live range. A zero <paramref name="maxBytes"/> disables the spill tier
    /// (pure live-set, minimal memory); a zero <paramref name="maxEntries"/> leaves the entry
    /// count uncapped. Omitting this call keeps the native defaults (100 MiB, 64 entries).
    /// Existing windows reconcile on their next render so a shrunken budget trims promptly.
    /// </summary>
    public void SetImageCacheBudget(ulong maxBytes, ulong maxEntries)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            IGpuiApplicationHost? host;
            lock (_gate)
            {
                if (_state == ApplicationState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The GPUI application has already stopped."
                    );
                }

                _imageCacheBudget = (maxBytes, maxEntries);
                host = _host;
            }

            host?.SetImageCacheBudget(maxBytes, maxEntries);
        }
    }

    /// <summary>
    /// Drops one image path from every view's native image cache, releasing its decoded bytes
    /// and GPU texture. The next paint reloads the file from disk. Unknown paths are a silent
    /// no-op. Use this after overwriting a file whose path stays mounted; the cache keys by
    /// path and never revalidates content on its own. The path must match the value passed to
    /// the image element exactly.
    /// </summary>
    public void EvictImage(string path)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            IGpuiApplicationHost? host;
            lock (_gate)
            {
                if (_state == ApplicationState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The GPUI application has already stopped."
                    );
                }

                host = _host;
            }

            host?.EvictImage(path);
        }
    }

    internal (ulong MaxBytes, ulong MaxEntries)? ImageCacheBudgetSnapshot()
    {
        lock (_gate)
        {
            return _imageCacheBudget;
        }
    }

    /// <summary>
    /// Installs the application's platform menu. On macOS this is the native global menu bar;
    /// other platforms may use the definitions for their own app-side menu presentation.
    /// </summary>
    /// <remarks>
    /// Menus support 4096 records, 32 menu levels, and 1 MiB of UTF-8 titles. A rejected
    /// replacement preserves the previous menu. Updates enqueue native work; at most 64
    /// installed or pending callback generations are retained before further updates throw.
    /// </remarks>
    public void SetMenuBar(params GpuiMenu[] menus)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            ArgumentNullException.ThrowIfNull(menus);
            if (menus.Any(menu => menu is null))
            {
                throw new ArgumentException(
                    "Menu definitions cannot contain null entries.",
                    nameof(menus)
                );
            }

            var copy = menus.ToArray();
            GpuiMenu.Validate(copy, nameof(menus));

            IGpuiApplicationHost? host;
            lock (_gate)
            {
                if (_state == ApplicationState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The GPUI application has already stopped."
                    );
                }

                host = _host;
            }

            host?.SetMenuBar(copy);
            lock (_gate)
            {
                _menuBar = copy;
            }
        }
    }

    /// <summary>
    /// Adds a window. Before Run it is queued as an initial window; while Run is active it is
    /// opened through the same application event loop and may be called from any thread. The
    /// window owns <paramref name="root"/> until close; closing permanently unmounts it.
    /// </summary>
    public GpuiWindow OpenWindow<TView>(ViewSpec<TView> root, GpuiWindowOptions? options = null)
        where TView : View, IGeneratedViewFactory<TView> =>
        OpenWindowCore(new RootViewDeclaration<TView>(root), options);

    public GpuiWindow OpenWindow<TView, TProps>(
        ViewSpec<TView, TProps> root,
        GpuiWindowOptions? options = null
    )
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps> =>
        OpenWindowCore(new RootViewDeclaration<TView, TProps>(root), options);

    private GpuiWindow OpenWindowCore(RootViewDeclaration rootView, GpuiWindowOptions? options)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            var snapshot = (options ?? new GpuiWindowOptions()).ValidateAndSnapshot();
            var id = checked((ulong)Interlocked.Increment(ref _nextWindowId));
            var window = new GpuiWindow(this, id, rootView, snapshot);
            IGpuiApplicationHost? host;

            lock (_gate)
            {
                if (_state == ApplicationState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The GPUI application has already stopped."
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
    }

    /// <summary>Runs until the last application-owned window closes.</summary>
    public void Run()
    {
        Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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

        try
        {
            RunOnUiThread(() => NativeRuntime.Load(_runtimeOptions).Run(this));
        }
        finally
        {
            FinishRun();
        }
    }

    private void FinishRun()
    {
        lock (_ingressGate)
        {
            lock (_gate)
            {
                _state = ApplicationState.Stopped;
                _host = null;
                foreach (var window in _windows.Values)
                    window.MarkClosed();
                _windows.Clear();
            }
        }
    }

    /// <summary>Runs one framework-owned root with the selected native runtime.</summary>
    public static void Run<TView>(
        ViewSpec<TView> root,
        GpuiWindowOptions? options = null,
        NativeRuntimeOptions? runtimeOptions = null
    )
        where TView : View, IGeneratedViewFactory<TView>
    {
        var application = new GpuiApplication(runtimeOptions);
        application.OpenWindow(root, options);
        application.Run();
    }

    public static void Run<TView, TProps>(
        ViewSpec<TView, TProps> root,
        GpuiWindowOptions? options = null,
        NativeRuntimeOptions? runtimeOptions = null
    )
        where TProps : IEquatable<TProps>
        where TView : View<TProps>, IGeneratedViewFactory<TView, TProps>
    {
        var application = new GpuiApplication(runtimeOptions);
        application.OpenWindow(root, options);
        application.Run();
    }

    internal void AttachHost(IGpuiApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_ingressGate)
        {
            GpuiWindowOpenRequest[] requests;
            lock (_gate)
            {
                if (_state != ApplicationState.Running || _host is not null)
                {
                    throw new InvalidOperationException(
                        "The GPUI application runtime is already attached."
                    );
                }
                requests = _windows
                    .Values.Where(window => !window.IsClosed && !window.CloseRequested)
                    .Select(window => new GpuiWindowOpenRequest(window, window.Snapshot))
                    .ToArray();
            }
            host.SetTheme(Theme);
            if (ImageCacheBudgetSnapshot() is { } budget)
                host.SetImageCacheBudget(budget.MaxBytes, budget.MaxEntries);
            if (MenuBarSnapshot() is { } menus)
                host.SetMenuBar(menus);
            foreach (var request in requests)
                host.OpenWindow(request.Window, request.Snapshot);
            lock (_gate)
                _host = host;
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
        GpuiWindow? closedWindow;
        bool wasOpened;
        lock (_gate)
        {
            if (!_windows.Remove(id, out closedWindow))
            {
                return;
            }
            wasOpened = closedWindow.WasOpened;
            closedWindow.MarkClosed();
        }
        if (wasOpened)
        {
            closedWindow.RaiseClosed();
            WindowClosed?.Invoke(closedWindow);
        }
    }

    internal bool NativeWindowOpened(ulong id)
    {
        GpuiWindow window;
        lock (_gate)
        {
            if (!_windows.TryGetValue(id, out window!) || window.IsClosed || window.WasOpened)
                return false;
            window.MarkOpened();
        }
        window.RaiseOpened();
        WindowOpened?.Invoke(window);
        return true;
    }

    internal bool NativeWindowPlacement(ulong id, GpuiWindowPlacement placement)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue(id, out var window) || window.IsClosed)
                return false;
            window.SetFinalPlacement(placement);
            return true;
        }
    }

    internal void CloseWindow(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
                    window.MarkClosed();
                }
                else
                {
                    window.CloseRequested = true;
                }
            }

            if (host is null)
                return;

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
    }

    internal void ActivateWindow(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
    }

    internal void MinimizeWindow(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
    }

    internal void ToggleMaximizeWindow(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
    }

    internal void ToggleFullscreenWindow(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            IGpuiApplicationHost host;
            lock (_gate)
            {
                ValidateOpenWindow(window);
                host =
                    _host
                    ?? throw new InvalidOperationException("The native window has not opened yet.");
            }
            host.ToggleFullscreenWindow(window.Id);
        }
    }

    internal void ShowWindowToast(GpuiWindow window, GpuiToast toast)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            IGpuiApplicationHost host;
            lock (_gate)
            {
                ValidateOpenWindow(window);
                host =
                    _host
                    ?? throw new InvalidOperationException("The native window has not opened yet.");
            }
            host.ShowWindowToast(window.Id, Interop.Internal.WindowToastPayload.Encode(toast));
        }
    }

    internal void DismissWindowToast(GpuiWindow window, string id)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            IGpuiApplicationHost host;
            lock (_gate)
            {
                ValidateOpenWindow(window);
                host =
                    _host
                    ?? throw new InvalidOperationException("The native window has not opened yet.");
            }
            Interop.Internal.WindowToastPayload.ValidateId(id);
            host.DismissWindowToast(window.Id, id);
        }
    }

    internal void ClearWindowToasts(GpuiWindow window)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
            IGpuiApplicationHost host;
            lock (_gate)
            {
                ValidateOpenWindow(window);
                host =
                    _host
                    ?? throw new InvalidOperationException("The native window has not opened yet.");
            }
            host.ClearWindowToasts(window.Id);
        }
    }

    internal void SetWindowTitle(GpuiWindow window, string title)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
    }

    internal void ResizeWindow(GpuiWindow window, float width, float height)
    {
        lock (_ingressGate)
        {
            Interop.Internal.ApplicationExecution.AssertEffectsAllowed();
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
    WindowTitleBarStyle TitleBarStyle,
    WindowInitialState InitialState,
    float? MinimumWidth,
    float? MinimumHeight
);

internal readonly record struct GpuiWindowOpenRequest(
    GpuiWindow Window,
    GpuiWindowSnapshot Snapshot
);

internal interface IGpuiApplicationHost
{
    void SetMenuBar(IReadOnlyList<GpuiMenu> menus);
    void SetTheme(GpuiTheme theme);
    void SetImageCacheBudget(ulong maxBytes, ulong maxEntries);
    void EvictImage(string path);
    void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot);
    void CloseWindow(ulong windowId);
    void ActivateWindow(ulong windowId);
    void MinimizeWindow(ulong windowId);
    void ToggleMaximizeWindow(ulong windowId);
    void ToggleFullscreenWindow(ulong windowId);
    void ShowWindowToast(ulong windowId, byte[] payload);
    void DismissWindowToast(ulong windowId, string id);
    void ClearWindowToasts(ulong windowId);
    void SetWindowTitle(ulong windowId, string title);
    void ResizeWindow(ulong windowId, float width, float height);
}
