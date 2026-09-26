using System.Collections.Concurrent;
using System.Text;
using Gpui;
using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

/// <summary>
/// Manages the lifetime of native windows for a single <see cref="GpuiApplication"/>.
/// </summary>
internal sealed class ManagedApplication : IGpuiApplicationHost
{
    private readonly NativeRuntime _runtime;
    private readonly ulong _applicationId;
    private readonly GpuiApplication _application;
    private readonly ConcurrentDictionary<ulong, ManagedSession> _sessions = new();
    private readonly MenuGenerations _menuActions = new();
    private long _nextMenuActionId;
    private Exception? _failure;
    private int _stopped;

    internal ManagedApplication(
        NativeRuntime runtime,
        ulong applicationId,
        GpuiApplication application
    )
    {
        _runtime = runtime;
        _applicationId = applicationId;
        _application = application;
    }

    internal Exception? Failure => Volatile.Read(ref _failure);

    internal void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _failure, exception, null);

    internal void ApplyManagedCodeUpdate()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        var sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
        {
            session.PrepareManagedCodeUpdate();
        }

        try
        {
            // Invalidation is queued before native schedules the frame. The render callback
            // consumes it on the application thread before using retained fragments.
            Dispatch(9, 0);
        }
        catch
        {
            // Preserve a useful managed-only fallback if registration or shutdown races the
            // metadata callback. Native row caches still require a later successful update.
            foreach (var session in sessions)
            {
                session.InvalidateAllViews();
            }
            throw;
        }
    }

    internal void Start()
    {
        _application.Execution.BindThread();
        _application.AttachHost(this);
    }

    public void SetMenuBar(IReadOnlyList<GpuiMenu> menus)
    {
        ApplicationExecution.AssertEffectsAllowed();
        ArgumentNullException.ThrowIfNull(menus);
        GpuiMenu.Validate(menus, nameof(menus));

        var records = new List<NativeMenuRecord>();
        var titleBytes = new List<byte>();
        var titleOffsets = new List<int>();
        var actions = new Dictionary<ulong, Action>();

        foreach (var menu in menus)
        {
            AddMenu(menu, uint.MaxValue, records, titleBytes, titleOffsets, actions);
        }

        var titles = titleBytes.ToArray();
        var generation = _menuActions.Stage(actions);
        try
        {
            unsafe
            {
                fixed (NativeMenuRecord* nativeRecords = records.ToArray())
                fixed (byte* nativeTitles = titles)
                {
                    for (var index = 0; index < records.Count; index++)
                    {
                        nativeRecords[index].title = nativeTitles + titleOffsets[index];
                    }

                    var native = new NativeMenuCommand
                    {
                        items = nativeRecords,
                        item_length = records.Count,
                        reserved = 0,
                        reserved2 = 0,
                        generation = generation,
                    };
                    var status = _runtime.Api->dispatch_application_menu(_applicationId, &native);
                    GC.KeepAlive(_runtime);
                    if (status != 0)
                    {
                        throw new InvalidOperationException(
                            $"Native menu update failed for application {_applicationId}: {NativeStatus.Describe(NativeStatusDomain.ApplicationMenu, status)}."
                        );
                    }
                }
            }
        }
        catch
        {
            _menuActions.Reject(generation);
            throw;
        }
    }

    internal void MenuApplied(ulong generation) => _menuActions.Applied(generation);

    public void SetTheme(GpuiTheme theme)
    {
        ApplicationExecution.AssertEffectsAllowed();
        ArgumentNullException.ThrowIfNull(theme);
        var payload = NativeThemePayload.From(theme);
        unsafe
        {
            var payloadPointer = &payload;
            var native = new NativeApplicationCommand
            {
                window_id = 0,
                command = 8,
                flags = 0,
                reserved = 0,
                title = (byte*)payloadPointer,
                title_length = sizeof(NativeThemePayload),
                reserved2 = 0,
                left = 0,
                top = 0,
                width = 0,
                height = 0,
            };
            var status = _runtime.Api->dispatch_application_command(_applicationId, &native);
            GC.KeepAlive(_runtime);
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native theme update failed for application {_applicationId}: {NativeStatus.Describe(NativeStatusDomain.ApplicationCommand, status)}."
                );
            }
        }

        foreach (var session in _sessions.Values)
        {
            session.InvalidateAllViews();
        }
    }

    public void SetImageCacheBudget(ulong maxBytes, ulong maxEntries)
    {
        ApplicationExecution.AssertEffectsAllowed();
        var payload = new NativeImageCacheBudget
        {
            Version = NativeImageCacheBudget.CurrentVersion,
            Reserved = 0,
            MaxBytes = maxBytes,
            MaxEntries = maxEntries,
        };
        unsafe
        {
            var payloadPointer = &payload;
            var native = new NativeApplicationCommand
            {
                window_id = 0,
                command = 10,
                flags = 0,
                reserved = 0,
                title = (byte*)payloadPointer,
                title_length = sizeof(NativeImageCacheBudget),
                reserved2 = 0,
                left = 0,
                top = 0,
                width = 0,
                height = 0,
            };
            var status = _runtime.Api->dispatch_application_command(_applicationId, &native);
            GC.KeepAlive(_runtime);
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native image cache budget update failed for application {_applicationId}: {NativeStatus.Describe(NativeStatusDomain.ApplicationCommand, status)}."
                );
            }
        }
    }

    internal int MenuAction(ulong actionId)
    {
        if (Failure is not null || Volatile.Read(ref _stopped) != 0)
        {
            return -65;
        }
        if (_menuActions.Find(actionId) is not { } callback)
        {
            return -64;
        }

        try
        {
            using var execution = _application.Execution.Enter(ExecutionPhase.Event);
            callback();
            return 0;
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            return -65;
        }
    }

    private void AddMenu(
        GpuiMenu menu,
        uint parent,
        List<NativeMenuRecord> records,
        List<byte> titleBytes,
        List<int> titleOffsets,
        Dictionary<ulong, Action> actions
    )
    {
        var menuIndex = checked((uint)records.Count);
        AddRecord(1, parent, 0, menu.Title, records, titleBytes, titleOffsets);
        foreach (var item in menu.Items)
        {
            if (item.IsSeparator)
            {
                AddRecord(3, menuIndex, 0, string.Empty, records, titleBytes, titleOffsets);
            }
            else if (item.NestedMenu is not null)
            {
                AddMenu(item.NestedMenu, menuIndex, records, titleBytes, titleOffsets, actions);
            }
            else
            {
                var actionId = checked((ulong)Interlocked.Increment(ref _nextMenuActionId));
                actions[actionId] = item.Callback!;
                AddRecord(2, menuIndex, actionId, item.Title, records, titleBytes, titleOffsets);
            }
        }
    }

    private static void AddRecord(
        ushort kind,
        uint parent,
        ulong actionId,
        string title,
        List<NativeMenuRecord> records,
        List<byte> titleBytes,
        List<int> titleOffsets
    )
    {
        var bytes = Encoding.UTF8.GetBytes(title);
        titleOffsets.Add(titleBytes.Count);
        titleBytes.AddRange(bytes);
        records.Add(
            new NativeMenuRecord
            {
                parent = parent,
                kind = kind,
                flags = 0,
                action_id = actionId,
                title = null,
                title_length = bytes.Length,
                reserved = 0,
            }
        );
    }

    public void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot)
    {
        ApplicationExecution.AssertEffectsAllowed();
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new InvalidOperationException("The GPUI application is stopping.");
        }

        var session = new ManagedSession(
            _runtime,
            _application,
            window.Id,
            window.TakeRootDeclaration(),
            window
        );
        if (!_sessions.TryAdd(window.Id, session))
        {
            throw new InvalidOperationException("Failed to register the managed window session.");
        }
        if (!NativeRegistry.Sessions.TryAdd(window.Id, session))
        {
            _sessions.TryRemove(window.Id, out _);
            throw new InvalidOperationException("Failed to register the managed window session.");
        }

        try
        {
            Dispatch(
                1,
                window.Id,
                snapshot.Title,
                snapshot.Left ?? 0,
                snapshot.Top ?? 0,
                snapshot.Width,
                snapshot.Height,
                (ushort)(
                    (snapshot.Left.HasValue ? 1 : 0)
                    | (snapshot.Activate ? 2 : 0)
                    | ((ushort)snapshot.TitleBarStyle << 2)
                )
            );
        }
        catch
        {
            _sessions.TryRemove(window.Id, out _);
            NativeRegistry.Sessions.TryRemove(window.Id, out _);
            StopSession(session);
            throw;
        }
    }

    public void CloseWindow(ulong windowId) => Dispatch(2, windowId);

    public void ActivateWindow(ulong windowId) => Dispatch(3, windowId);

    public void MinimizeWindow(ulong windowId) => Dispatch(6, windowId);

    public void ToggleMaximizeWindow(ulong windowId) => Dispatch(7, windowId);

    public void ToggleFullscreenWindow(ulong windowId) => Dispatch(12, windowId);

    public void ShowWindowToast(ulong windowId, byte[] payload) =>
        DispatchBytes(13, windowId, payload);

    public void DismissWindowToast(ulong windowId, string id) => Dispatch(14, windowId, id);

    public void ClearWindowToasts(ulong windowId) => Dispatch(15, windowId);

    public void SetWindowTitle(ulong windowId, string title) => Dispatch(4, windowId, title);

    public void EvictImage(string path) => Dispatch(11, 0, path);

    public void ResizeWindow(ulong windowId, float width, float height) =>
        Dispatch(5, windowId, width: width, height: height);

    internal int WindowClosed(ulong windowId, int nativeStatus)
    {
        var failed = nativeStatus != 0;
        if (failed)
        {
            RecordFailure(
                new InvalidOperationException(
                    $"Native window {windowId} closed with status {nativeStatus}."
                )
            );
        }

        if (_sessions.TryRemove(windowId, out var session))
        {
            NativeRegistry.Sessions.TryRemove(windowId, out _);
            StopSession(session);
            if (session.Failure is { } failure)
            {
                RecordFailure(failure);
                failed = true;
            }
        }
        _application.NativeWindowClosed(windowId);
        return failed ? -121 : 0;
    }

    internal void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _menuActions.Clear();

        foreach (var (windowId, session) in _sessions.ToArray())
        {
            if (!_sessions.TryRemove(windowId, out _))
            {
                continue;
            }
            NativeRegistry.Sessions.TryRemove(windowId, out _);
            StopSession(session);
            if (session.Failure is { } failure)
            {
                RecordFailure(failure);
            }
            _application.NativeWindowClosed(windowId);
        }
    }

    private void StopSession(ManagedSession session)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
        try
        {
            session.Stop();
        }
        catch (Exception exception)
        {
            session.RecordFailure(exception);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private unsafe void Dispatch(
        ushort command,
        ulong windowId,
        string? title = null,
        float left = 0,
        float top = 0,
        float width = 0,
        float height = 0,
        ushort flags = 0
    )
    {
        ApplicationExecution.AssertEffectsAllowed();
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new InvalidOperationException("The GPUI application is stopping.");
        }

        DispatchBytes(
            command,
            windowId,
            title is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(title),
            left,
            top,
            width,
            height,
            flags
        );
    }

    private unsafe void DispatchBytes(
        ushort command,
        ulong windowId,
        byte[] payload,
        float left = 0,
        float top = 0,
        float width = 0,
        float height = 0,
        ushort flags = 0
    )
    {
        ApplicationExecution.AssertEffectsAllowed();
        if (Volatile.Read(ref _stopped) != 0)
            throw new InvalidOperationException("The GPUI application is stopping.");
        fixed (byte* payloadPointer = payload)
        {
            var native = new NativeApplicationCommand
            {
                window_id = windowId,
                command = command,
                flags = flags,
                reserved = 0,
                title = payloadPointer,
                title_length = payload.Length,
                reserved2 = 0,
                left = left,
                top = top,
                width = width,
                height = height,
            };
            var status = _runtime.Api->dispatch_application_command(_applicationId, &native);
            GC.KeepAlive(_runtime);
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native application command {command} failed for application {_applicationId}, window {windowId}: {NativeStatus.Describe(NativeStatusDomain.ApplicationCommand, status)}."
                );
            }
        }
    }
}
