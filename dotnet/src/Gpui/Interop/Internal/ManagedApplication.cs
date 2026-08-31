using System.Collections.Concurrent;
using System.Text;
using Gpui;
using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

/// <summary>
/// Manages the lifetime of native windows for a single <see cref="GpuiApplication"/>.
/// Previously nested inside NativeRuntime; now a top-level internal class for single responsibility.
/// </summary>
internal sealed class ManagedApplication : IGpuiApplicationHost
{
    private readonly NativeRuntime _runtime;
    private readonly ulong _applicationId;
    private readonly GpuiApplication _application;
    private readonly ConcurrentDictionary<ulong, ManagedSession> _sessions = new();
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

    internal void Start()
    {
        foreach (var request in _application.AttachHost(this))
        {
            OpenWindow(request.Window, request.Snapshot);
        }
    }

    public void OpenWindow(GpuiWindow window, GpuiWindowSnapshot snapshot)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new InvalidOperationException("The GPUI application is stopping.");
        }

        var session = new ManagedSession(_runtime, window.Id, window.RootView);
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

    public void SetWindowTitle(ulong windowId, string title) => Dispatch(4, windowId, title);

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
        if (Volatile.Read(ref _stopped) != 0)
        {
            throw new InvalidOperationException("The GPUI application is stopping.");
        }

        var titleUtf8 = title is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(title);
        fixed (byte* titlePointer = titleUtf8)
        {
            var native = new NativeApplicationCommand
            {
                window_id = windowId,
                command = command,
                flags = flags,
                reserved = 0,
                title = titlePointer,
                title_length = titleUtf8.Length,
                reserved2 = 0,
                left = left,
                top = top,
                width = width,
                height = height,
            };
            var status = _runtime.Api->dispatch_application_command(_applicationId, &native);
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Native application command {command} failed with status {status}."
                );
            }
        }
    }
}
