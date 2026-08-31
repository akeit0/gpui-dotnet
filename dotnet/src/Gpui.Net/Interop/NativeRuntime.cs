using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Gpui;

namespace Gpui.Interop;

/// <summary>
/// Control-plane wrapper around the versioned native function table. Render operations remain
/// direct arena writes and event dispatch uses generated numeric tokens rather than delegates.
/// </summary>
public sealed unsafe class NativeRuntime
{
    private static readonly ConcurrentDictionary<ulong, ManagedApplication> Applications = new();
    private static readonly ConcurrentDictionary<ulong, ManagedSession> Sessions = new();
    private static long _nextApplicationId;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly GpuiDotnetApiV5* _api;

    private NativeRuntime(GpuiDotnetApiV5* api) => _api = api;

    public static NativeRuntime Load()
    {
        var api = NativeMethods.gpui_dotnet_get_api(NativeConstants.AbiVersion);
        if (api == null)
        {
            throw new InvalidOperationException(
                $"gpui-dotnet native ABI {NativeConstants.AbiVersion} is not supported."
            );
        }

        if (api->struct_size < (uint)sizeof(GpuiDotnetApiV5))
        {
            throw new InvalidOperationException(
                $"Native API table is too small. Managed={sizeof(GpuiDotnetApiV5)}, native={api->struct_size}."
            );
        }

        if (api->abi_version != NativeConstants.AbiVersion)
        {
            throw new InvalidOperationException(
                $"Native ABI mismatch. Managed={NativeConstants.AbiVersion}, native={api->abi_version}."
            );
        }

        if (api->schema_hash != NativeConstants.SchemaHash)
        {
            throw new InvalidOperationException(
                $"Binding schema mismatch. Managed=0x{NativeConstants.SchemaHash:X16}, native=0x{api->schema_hash:X16}."
            );
        }

        if (
            api->validate_render == null
            || api->run_application == null
            || api->notify_view == null
            || api->dispatch_command == null
            || api->dispatch_application_command == null
        )
        {
            throw new InvalidOperationException(
                "The native API table is missing required entries."
            );
        }

        return new NativeRuntime(api);
    }

    public void Validate(RenderArenaOwner owner, Element root)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var status = _api->validate_render(owner.NativeArena, root.Node);
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"Native render validation failed with status {status}."
            );
        }
    }

    internal void Run(GpuiApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var id = checked((ulong)Interlocked.Increment(ref _nextApplicationId));
        var managedApplication = new ManagedApplication(this, id, application);
        if (!Applications.TryAdd(id, managedApplication))
        {
            throw new InvalidOperationException("Failed to register the managed GPUI application.");
        }

        var status = 0;
        try
        {
            var callbacks = new ManagedCallbacks
            {
                struct_size = (uint)sizeof(ManagedCallbacks),
                render = &Render,
                click = &Click,
                list_render_range = &ListRenderRange,
                control_event = &ControlEvent,
                application_started = &ApplicationStarted,
                window_closed = &WindowClosed,
            };
            status = _api->run_application(id, &callbacks);
        }
        finally
        {
            managedApplication.Stop();
            Applications.TryRemove(id, out _);
        }

        if (status != 0)
        {
            throw new InvalidOperationException(
                $"The native GPUI application stopped with status {status}.",
                managedApplication.Failure
            );
        }

        if (managedApplication.Failure is not null)
        {
            throw new InvalidOperationException(
                "The managed GPUI application observed a window event, render, or lifecycle failure.",
                managedApplication.Failure
            );
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Render(ulong sessionId, RenderArena* arena, uint* root)
    {
        try
        {
            if (arena == null || root == null || !Sessions.TryGetValue(sessionId, out var session))
            {
                return -100;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                var element = session.RenderRoot(arena);
                *root = element.Node;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (RenderArenaGrowthRequiredException)
        {
            return NativeConstants.RenderGrowRequired;
        }
        catch (Exception exception)
        {
            RecordFailure(sessionId, exception);
            return -101;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ListRenderRange(
        ulong sessionId,
        ulong rendererToken,
        uint start,
        uint count,
        RenderArena* arena,
        uint* root
    )
    {
        try
        {
            if (
                arena == null
                || root == null
                || count == 0
                || count > 512
                || !Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -105;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                var element = session.RenderListRange(rendererToken, start, count, arena);
                *root = element.Node;
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (RenderArenaGrowthRequiredException)
        {
            return NativeConstants.RenderGrowRequired;
        }
        catch (Exception exception)
        {
            RecordFailure(sessionId, exception);
            return -106;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Click(
        ulong sessionId,
        ulong eventToken,
        ulong eventPayload,
        NativeClickEvent* nativeEvent
    )
    {
        try
        {
            if (nativeEvent == null || !Sessions.TryGetValue(sessionId, out var session))
            {
                return -110;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.DispatchClick(
                    eventToken,
                    new ClickEvent
                    {
                        Payload = eventPayload,
                        X = nativeEvent->x,
                        Y = nativeEvent->y,
                        Buttons = nativeEvent->buttons,
                        Modifiers = nativeEvent->modifiers,
                    }
                );
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            RecordFailure(sessionId, exception);
            return -111;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ControlEvent(
        ulong sessionId,
        ulong eventToken,
        NativeControlEvent* nativeEvent
    )
    {
        try
        {
            if (
                nativeEvent == null
                || nativeEvent->reserved != 0
                || nativeEvent->reserved2 != 0
                || nativeEvent->data_length < 0
                || (nativeEvent->data_length != 0 && nativeEvent->data == null)
                || nativeEvent->kind
                    is < (ushort)InputEventKind.Changed
                        or > (ushort)InputEventKind.FocusChanged
                || (nativeEvent->flags & ~1u) != 0
                || !Sessions.TryGetValue(sessionId, out var session)
            )
            {
                return -112;
            }

            var value =
                nativeEvent->data_length == 0
                    ? Array.Empty<byte>()
                    : new ReadOnlySpan<byte>(nativeEvent->data, nativeEvent->data_length).ToArray();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.DispatchInput(
                    eventToken,
                    new InputEvent(
                        (InputEventKind)nativeEvent->kind,
                        value,
                        (nativeEvent->flags & 1) != 0,
                        nativeEvent->revision
                    )
                );
                return 0;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            RecordFailure(sessionId, exception);
            return -113;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ApplicationStarted(ulong applicationId)
    {
        try
        {
            if (!Applications.TryGetValue(applicationId, out var application))
            {
                return -120;
            }
            application.Start();
            return 0;
        }
        catch (Exception exception)
        {
            if (Applications.TryGetValue(applicationId, out var application))
            {
                application.RecordFailure(exception);
            }
            return -122;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WindowClosed(ulong applicationId, ulong windowId, int nativeStatus)
    {
        try
        {
            if (!Applications.TryGetValue(applicationId, out var application))
            {
                return -120;
            }
            return application.WindowClosed(windowId, nativeStatus);
        }
        catch (Exception exception)
        {
            if (Applications.TryGetValue(applicationId, out var application))
            {
                application.RecordFailure(exception);
            }
            return -121;
        }
    }

    private static void RecordFailure(ulong sessionId, Exception exception)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            session.RecordFailure(exception);
        }
    }

    private void NotifyView(ulong sessionId)
    {
        var status = _api->notify_view(sessionId);
        if (status is -30 or -31)
        {
            // A late continuation may race normal native teardown.
            return;
        }
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"Native view notification failed with status {status}."
            );
        }
    }

    private sealed class ManagedApplication : IGpuiApplicationHost
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
                throw new InvalidOperationException(
                    "Failed to register the managed window session."
                );
            }
            if (!Sessions.TryAdd(window.Id, session))
            {
                _sessions.TryRemove(window.Id, out _);
                throw new InvalidOperationException(
                    "Failed to register the managed window session."
                );
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
                Sessions.TryRemove(window.Id, out _);
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
                Sessions.TryRemove(windowId, out _);
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
                Sessions.TryRemove(windowId, out _);
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

        private void Dispatch(
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
                var status = _runtime._api->dispatch_application_command(_applicationId, &native);
                if (status != 0)
                {
                    throw new InvalidOperationException(
                        $"Native application command {command} failed with status {status}."
                    );
                }
            }
        }
    }

    private sealed class ManagedSession : IViewRenderer
    {
        private static readonly IEqualityComparer<View> ViewIdentity =
            ReferenceEqualityComparer.Instance;

        private readonly NativeRuntime _runtime;
        private readonly ulong _sessionId;
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly HashSet<View> _attachedViews = new(ViewIdentity);
        private readonly Dictionary<uint, View> _viewsByHandle = [];
        private readonly object _renderStateGate = new();
        private readonly Dictionary<View, RetainedViewState> _renderStates = new(ViewIdentity);
        private readonly HashSet<View> _renderingViews = new(ViewIdentity);
        private readonly Stack<View> _snapshotStack = new();
        private readonly HashSet<View> _snapshotVisited = new(ViewIdentity);
        private readonly List<View> _detachCandidates = [];
        private readonly Stack<(View View, bool Expanded)> _detachStack = new();
        private readonly HashSet<View> _detachVisited = new(ViewIdentity);
        private ExceptionDispatchInfo? _pendingAsyncFailure;
        private Exception? _failure;
        private uint _nextViewHandle;
        private int _renderingStarted;
        private int _renderingManaged;
        private int _notifyAfterRender;
        private int _stopped;

        internal ManagedSession(NativeRuntime runtime, ulong sessionId, View view)
        {
            _runtime = runtime;
            _sessionId = sessionId;
            RootView = view;
            SynchronizationContext = new GpuiSynchronizationContext(this);
        }

        internal View RootView { get; }
        internal SynchronizationContext SynchronizationContext { get; }
        internal Exception? Failure => Volatile.Read(ref _failure);

        internal void RecordFailure(Exception exception) =>
            Interlocked.CompareExchange(ref _failure, exception, null);

        internal void Invalidate(View view)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }
            MarkDirty(view);
            NotifyRenderPending();
        }

        internal void DispatchResourceCommand(View owner, ResourceCommand command)
        {
            if (Volatile.Read(ref _stopped) != 0 || owner.RuntimeViewHandle == 0)
            {
                return;
            }
            if (command.Utf8Key is null && string.IsNullOrEmpty(command.Key))
            {
                throw new ArgumentException(
                    "A native resource key cannot be empty.",
                    nameof(command)
                );
            }

            var keyUtf8 = command.Utf8Key ?? Encoding.UTF8.GetBytes(command.Key!);
            var dataUtf8 = command.Data is null
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(command.Data);
            fixed (byte* key = keyUtf8)
            fixed (byte* data = dataUtf8)
            {
                var native = new NativeResourceCommand
                {
                    owner_view = owner.RuntimeViewHandle,
                    resource_kind = (ushort)command.ResourceKind,
                    command = (ushort)command.Command,
                    key = key,
                    key_length = keyUtf8.Length,
                    data = data,
                    data_length = dataUtf8.Length,
                    reserved = 0,
                    a = command.A,
                    b = command.B,
                };
                var status = _runtime._api->dispatch_command(_sessionId, &native);
                if (status is -30 or -31)
                {
                    // A controller operation may race normal application teardown.
                    return;
                }
                if (status != 0)
                {
                    throw new InvalidOperationException(
                        $"Native resource command failed with status {status}."
                    );
                }
            }
        }

        internal void DispatchUtf8InputValue(
            View owner,
            ReadOnlySpan<byte> utf8Key,
            ReadOnlySpan<byte> utf8Value
        )
        {
            if (Volatile.Read(ref _stopped) != 0 || owner.RuntimeViewHandle == 0)
            {
                return;
            }
            if (utf8Key.IsEmpty)
            {
                throw new ArgumentException(
                    "A native resource key cannot be empty.",
                    nameof(utf8Key)
                );
            }

            fixed (byte* key = utf8Key)
            fixed (byte* data = utf8Value)
            {
                var native = new NativeResourceCommand
                {
                    owner_view = owner.RuntimeViewHandle,
                    resource_kind = (ushort)ResourceKind.Input,
                    command = (ushort)ResourceCommandKind.InputSetValue,
                    key = key,
                    key_length = utf8Key.Length,
                    data = data,
                    data_length = utf8Value.Length,
                    reserved = 0,
                    a = 0,
                    b = 0,
                };
                var status = _runtime._api->dispatch_command(_sessionId, &native);
                if (status is -30 or -31)
                {
                    return;
                }
                if (status != 0)
                {
                    throw new InvalidOperationException(
                        $"Native input value command failed with status {status}."
                    );
                }
            }
        }

        internal void Post(Action callback)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }

            _posted.Enqueue(callback);
            if (Volatile.Read(ref _stopped) != 0)
            {
                while (_posted.TryDequeue(out _)) { }
                return;
            }
            NotifyRenderPending();
        }

        private void NotifyRenderPending()
        {
            if (Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _renderingStarted) == 0)
            {
                return;
            }

            if (Volatile.Read(ref _renderingManaged) != 0)
            {
                Volatile.Write(ref _notifyAfterRender, 1);
                return;
            }

            _runtime.NotifyView(_sessionId);
        }

        internal Element RenderRoot(RenderArena* arena)
        {
            try
            {
                BeginRendering();
                AttachRoot();
                BeginComposition(RootView);
                var completed = false;
                try
                {
                    var ui = new RenderContext(arena, this, RootView);
                    var element = RootView.RenderCore(ref ui);
                    ManagedValidator.Validate(arena, element);
                    CompleteComposition(RootView);
                    completed = true;
                    CommitSnapshotTree();
                    return element;
                }
                catch
                {
                    if (!completed)
                    {
                        AbortComposition(RootView);
                    }
                    throw;
                }
            }
            finally
            {
                EndRendering();
            }
        }

        internal Element RenderListRange(
            ulong rendererToken,
            uint start,
            uint count,
            RenderArena* arena
        )
        {
            // The token is packed as two independent 32-bit values. Explicitly use unchecked
            // narrowing conversions so a project-wide checked context cannot reject the packing.
            var viewHandle = unchecked((uint)(rendererToken >> 32));
            var rendererId = unchecked((uint)rendererToken);
            if (viewHandle == 0 || rendererId == 0)
            {
                throw new InvalidOperationException("List renderer token 0 is reserved.");
            }
            if (!_viewsByHandle.TryGetValue(viewHandle, out var owner) || !owner.IsMountedCore)
            {
                throw new InvalidOperationException(
                    $"List renderer owner View 0x{viewHandle:X8} is no longer mounted."
                );
            }
            if (count is 0 or > 512 || (ulong)start + count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Volatile.Write(ref _renderingStarted, 1);
            if (Interlocked.CompareExchange(ref _renderingManaged, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "Nested managed list rendering is not supported."
                );
            }
            Volatile.Write(ref _notifyAfterRender, 0);
            try
            {
                // List rows are deliberately element-only. Passing no managed-view renderer and no
                // owner prevents ui.Child(), ui.Scroll(), and nested ui.List() from creating
                // independent retained lifetimes inside the virtualized cache. Generated event
                // tokens still work because they are bound to the owning View, not RenderContext.
                var ui = new RenderContext(arena);
                var batchRoot = ui.Div();
                for (uint offset = 0; offset < count; offset++)
                {
                    var index = checked((int)(start + offset));
                    var row = owner.RenderListItemCore(rendererId, index, ref ui);
                    ArenaWriter.AddChild(batchRoot, row);
                }

                ManagedValidator.Validate(arena, batchRoot);
                return batchRoot;
            }
            finally
            {
                EndRendering();
            }
        }

        private void BeginRendering()
        {
            Volatile.Write(ref _renderingStarted, 1);
            Volatile.Write(ref _renderingManaged, 1);
            Volatile.Write(ref _notifyAfterRender, 0);

            var asyncFailure = Interlocked.Exchange(ref _pendingAsyncFailure, null);
            asyncFailure?.Throw();

            while (_posted.TryDequeue(out var callback))
            {
                callback();
            }
        }

        private void EndRendering()
        {
            Volatile.Write(ref _renderingManaged, 0);
            if (
                Volatile.Read(ref _stopped) == 0
                && (Interlocked.Exchange(ref _notifyAfterRender, 0) != 0 || !_posted.IsEmpty)
            )
            {
                _runtime.NotifyView(_sessionId);
            }
        }

        internal void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            while (_posted.TryDequeue(out _)) { }
            BuildDetachOrder(includeCommittedTree: true);
            foreach (var view in _detachCandidates)
            {
                try
                {
                    Detach(view);
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }
            }

            lock (_renderStateGate)
            {
                foreach (var state in _renderStates.Values)
                {
                    state.Fragment?.Dispose();
                }
                _renderStates.Clear();
            }

            _attachedViews.Clear();
            _viewsByHandle.Clear();
            _renderingViews.Clear();
            _snapshotStack.Clear();
            _snapshotVisited.Clear();
            _detachCandidates.Clear();
            _detachStack.Clear();
            _detachVisited.Clear();
        }

        Element IViewRenderer.RenderChild<TView>(
            View parent,
            ChildSlot requestedSlot,
            RenderArena* destination
        )
        {
            var child = ResolveFrameworkChild<TView>(parent, requestedSlot);
            return RenderResolvedChild(child, destination);
        }

        Element IViewRenderer.RenderChild<TView, TProps>(
            View parent,
            ChildSlot requestedSlot,
            in TProps props,
            RenderArena* destination
        )
        {
            var child = ResolveFrameworkChild<TView, TProps>(parent, requestedSlot, in props);
            return RenderResolvedChild(child, destination);
        }

        Element IViewRenderer.RenderChild(
            View view,
            View parent,
            ChildSlot requestedSlot,
            RenderArena* destination
        )
        {
            ResolveExplicitChild(view, parent, requestedSlot);
            return RenderResolvedChild(view, destination);
        }

        Element IViewRenderer.RenderChild<TProps>(
            View<TProps> view,
            View parent,
            ChildSlot requestedSlot,
            in TProps props,
            RenderArena* destination
        )
        {
            ResolveExplicitChild(view, parent, requestedSlot, in props);
            return RenderResolvedChild(view, destination);
        }

        private TView ResolveFrameworkChild<TView>(View parent, ChildSlot requestedSlot)
            where TView : View, IGeneratedViewFactory<TView>
        {
            EnsureParentIsRendering(parent);
            var parentState = GetRenderState(parent);
            var slot = NormalizeSlot(parentState, requestedSlot);
            EnsureSlotUnused(parentState, slot);

            if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
            {
                if (existing.View.RequiresProps)
                {
                    throw new InvalidOperationException(
                        $"{typeof(TView).Name} derives from View<TProps>; render it with the props overload."
                    );
                }
                RegisterWorkingChild(parentState, slot, existing);
                return (TView)existing.View;
            }

            var child = CreateFrameworkView<TView>();
            if (child.RequiresProps)
            {
                throw new InvalidOperationException(
                    $"{typeof(TView).Name} derives from View<TProps>; render it with the props overload."
                );
            }

            AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Framework);
            return child;
        }

        private TView ResolveFrameworkChild<TView, TProps>(
            View parent,
            ChildSlot requestedSlot,
            in TProps props
        )
            where TView : View<TProps>, IGeneratedViewFactory<TView>
        {
            EnsureParentIsRendering(parent);
            var parentState = GetRenderState(parent);
            var slot = NormalizeSlot(parentState, requestedSlot);
            EnsureSlotUnused(parentState, slot);

            if (TryGetFrameworkChild<TView>(parentState, slot, out var existing))
            {
                var child = (TView)existing.View;
                if (child.ApplyProps(in props))
                {
                    MarkViewFragmentDirty(child);
                }
                RegisterWorkingChild(parentState, slot, existing);
                return child;
            }

            var created = CreateFrameworkView<TView>();
            _ = created.ApplyProps(in props);
            AttachResolvedCandidate(created, parent, parentState, slot, ChildOwnership.Framework);
            return created;
        }

        private void ResolveExplicitChild(View child, View parent, ChildSlot requestedSlot)
        {
            if (child.RequiresProps)
            {
                throw new InvalidOperationException(
                    $"{child.GetType().Name} derives from View<TProps>; render it with the props overload."
                );
            }

            EnsureParentIsRendering(parent);
            EnsureNotRecursive(child);
            var parentState = GetRenderState(parent);
            var slot = NormalizeSlot(parentState, requestedSlot);
            EnsureSlotUnused(parentState, slot);
            if (TryGetExplicitChild(parentState, slot, child, out var existing))
            {
                RegisterWorkingChild(parentState, slot, existing);
                return;
            }

            AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Explicit);
        }

        private void ResolveExplicitChild<TProps>(
            View<TProps> child,
            View parent,
            ChildSlot requestedSlot,
            in TProps props
        )
        {
            EnsureParentIsRendering(parent);
            EnsureNotRecursive(child);
            var parentState = GetRenderState(parent);
            var slot = NormalizeSlot(parentState, requestedSlot);
            EnsureSlotUnused(parentState, slot);
            if (TryGetExplicitChild(parentState, slot, child, out var existing))
            {
                if (child.ApplyProps(in props))
                {
                    MarkViewFragmentDirty(child);
                }
                RegisterWorkingChild(parentState, slot, existing);
                return;
            }

            _ = child.ApplyProps(in props);
            AttachResolvedCandidate(child, parent, parentState, slot, ChildOwnership.Explicit);
        }

        private static TView CreateFrameworkView<TView>()
            where TView : View, IGeneratedViewFactory<TView>
        {
            var child = TView.CreateGpuiView();
            return child
                ?? throw new InvalidOperationException(
                    $"The generated factory for {typeof(TView).FullName} returned null."
                );
        }

        private void AttachResolvedCandidate(
            View child,
            View parent,
            RetainedViewState parentState,
            ChildSlot slot,
            ChildOwnership ownership
        )
        {
            EnsureNotRecursive(child);
            var childState = PrepareOwnership(child, parent);
            try
            {
                Attach(child);
            }
            catch
            {
                RollBackPreparedOwnership(child, childState, parent);
                throw;
            }

            var entry = new ChildEntry(child, ownership);
            GetCandidates(parentState)[slot] = entry;
            RegisterWorkingChild(parentState, slot, entry);
        }

        private void EnsureNotRecursive(View child)
        {
            if (_renderingViews.Contains(child))
            {
                throw new InvalidOperationException(
                    "Managed views cannot recursively render themselves."
                );
            }
        }

        private Element RenderResolvedChild(View view, RenderArena* destination)
        {
            var state = GetRenderState(view);
            long requiredVersion;
            lock (_renderStateGate)
            {
                requiredVersion = state.RequiredVersion;
            }

            if (state.Fragment is null || state.RenderedVersion != requiredVersion)
            {
                state.Fragment ??= new RenderArenaOwner(64, 256, 128, 4096);
                BeginComposition(view);
                try
                {
                    var ui = state.Fragment.BeginRender(this, view);
                    var element = view.RenderCore(ref ui);
                    state.Fragment.Validate(element);
                    state.Root = element.Node;
                    CompleteComposition(view);
                    lock (_renderStateGate)
                    {
                        state.RenderedVersion = requiredVersion;
                    }
                }
                catch
                {
                    AbortComposition(view);
                    throw;
                }
            }

            return ArenaWriter.AppendFragment(destination, state.Fragment.NativeArena, state.Root);
        }

        private static bool TryGetFrameworkChild<TView>(
            RetainedViewState parentState,
            ChildSlot slot,
            out ChildEntry entry
        )
            where TView : View
        {
            if (
                parentState.Candidates is not null
                && parentState.Candidates.TryGetValue(slot, out entry)
                && entry.Ownership == ChildOwnership.Framework
                && entry.View.GetType() == typeof(TView)
            )
            {
                return true;
            }

            if (
                parentState.Children is not null
                && parentState.Children.TryGetValue(slot, out entry)
                && entry.Ownership == ChildOwnership.Framework
                && entry.View.GetType() == typeof(TView)
            )
            {
                return true;
            }

            entry = default;
            return false;
        }

        private static bool TryGetExplicitChild(
            RetainedViewState parentState,
            ChildSlot slot,
            View requested,
            out ChildEntry entry
        )
        {
            if (
                parentState.Candidates is not null
                && parentState.Candidates.TryGetValue(slot, out entry)
                && entry.Ownership == ChildOwnership.Explicit
                && ReferenceEquals(entry.View, requested)
            )
            {
                return true;
            }

            if (
                parentState.Children is not null
                && parentState.Children.TryGetValue(slot, out entry)
                && entry.Ownership == ChildOwnership.Explicit
                && ReferenceEquals(entry.View, requested)
            )
            {
                return true;
            }

            entry = default;
            return false;
        }

        private RetainedViewState PrepareOwnership(View child, View parent)
        {
            lock (_renderStateGate)
            {
                var childState = GetRenderState(child);
                if (childState.Parent is not null && !ReferenceEquals(childState.Parent, parent))
                {
                    throw new InvalidOperationException(
                        $"Managed View '{child.GetType().Name}' is already owned by "
                            + $"'{childState.Parent.GetType().Name}'. A View instance may have only one parent."
                    );
                }
                childState.Parent = parent;
                return childState;
            }
        }

        private void RollBackPreparedOwnership(
            View child,
            RetainedViewState childState,
            View parent
        )
        {
            lock (_renderStateGate)
            {
                if (
                    !_attachedViews.Contains(child)
                    && ReferenceEquals(childState.Parent, parent)
                    && (childState.Children is null || childState.Children.Count == 0)
                    && (childState.Candidates is null || childState.Candidates.Count == 0)
                    && !childState.HasStagedComposition
                )
                {
                    childState.Parent = null;
                    if (_renderStates.Remove(child, out var removed))
                    {
                        removed.Fragment?.Dispose();
                    }
                }
            }
        }

        private static ChildSlot NormalizeSlot(RetainedViewState state, ChildSlot requestedSlot)
        {
            if (!requestedSlot.IsAuto)
            {
                return requestedSlot;
            }

            if (state.WorkingNextPosition == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The managed child positional slot space was exhausted."
                );
            }

            return ChildSlot.Positional(state.WorkingNextPosition++);
        }

        private static void EnsureSlotUnused(RetainedViewState parentState, ChildSlot slot)
        {
            if (parentState.WorkingChildren?.ContainsKey(slot) == true)
            {
                throw new InvalidOperationException(
                    $"Managed child slot '{slot}' was rendered more than once in a single Render()."
                );
            }
        }

        private static void RegisterWorkingChild(
            RetainedViewState parentState,
            ChildSlot slot,
            ChildEntry entry
        )
        {
            if (!GetWorkingViews(parentState).Add(entry.View))
            {
                throw new InvalidOperationException(
                    "The same managed child View instance cannot be rendered in multiple slots of one parent."
                );
            }
            GetWorkingChildren(parentState).Add(slot, entry);
        }

        private void EnsureParentIsRendering(View parent)
        {
            if (!_renderingViews.Contains(parent))
            {
                throw new InvalidOperationException(
                    "A child view can only be rendered while its parent is rendering."
                );
            }
        }

        internal void DispatchClick(ulong eventToken, ClickEvent clickEvent)
        {
            var viewHandle = (uint)(eventToken >> 32);
            var handlerId = (uint)eventToken;
            if (viewHandle == 0 || handlerId == 0)
            {
                throw new InvalidOperationException("Malformed generated event token.");
            }
            if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
            {
                throw new InvalidOperationException(
                    $"Event references detached or unknown view handle {viewHandle}."
                );
            }

            var pending = owner.DispatchClickCore(handlerId, clickEvent);
            if (!pending.IsCompletedSuccessfully)
            {
                ObserveEventTask(pending);
            }
        }

        internal void DispatchInput(ulong eventToken, InputEvent inputEvent)
        {
            var viewHandle = (uint)(eventToken >> 32);
            var handlerId = (uint)eventToken;
            if (viewHandle == 0 || handlerId == 0)
            {
                throw new InvalidOperationException("Malformed generated input event token.");
            }
            if (!_viewsByHandle.TryGetValue(viewHandle, out var owner))
            {
                throw new InvalidOperationException(
                    $"Input event references detached or unknown view handle {viewHandle}."
                );
            }

            var pending = owner.DispatchInputCore(handlerId, inputEvent);
            if (!pending.IsCompletedSuccessfully)
            {
                ObserveEventTask(pending);
            }
        }

        private void ObserveEventTask(ValueTask pending)
        {
            _ = pending
                .AsTask()
                .ContinueWith(
                    completed => ObserveEventCompletion(completed),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
        }

        private void ObserveEventCompletion(Task pending)
        {
            try
            {
                pending.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a normal event outcome; it must not surface as an unhandled
                // native callback failure.
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                Interlocked.CompareExchange(
                    ref _pendingAsyncFailure,
                    ExceptionDispatchInfo.Capture(exception),
                    null
                );
                try
                {
                    NotifyRenderPending();
                }
                catch (Exception notifyFailure)
                {
                    RecordFailure(notifyFailure);
                }
            }
        }

        private void AttachRoot()
        {
            lock (_renderStateGate)
            {
                var rootState = GetRenderState(RootView);
                if (rootState.Parent is not null)
                {
                    throw new InvalidOperationException(
                        "The root View cannot be owned by another View."
                    );
                }
            }
            Attach(RootView);
        }

        private void Attach(View view)
        {
            if (!_attachedViews.Add(view))
            {
                return;
            }

            if (_nextViewHandle == uint.MaxValue)
            {
                _attachedViews.Remove(view);
                throw new InvalidOperationException("The session view-handle space was exhausted.");
            }

            var handle = ++_nextViewHandle;
            _viewsByHandle.Add(handle, view);
            try
            {
                _ = GetRenderState(view);
                view.AttachRuntime(
                    handle,
                    Post,
                    Invalidate,
                    DispatchResourceCommand,
                    DispatchUtf8InputValue
                );
            }
            catch
            {
                _viewsByHandle.Remove(handle);
                _attachedViews.Remove(view);
                lock (_renderStateGate)
                {
                    if (_renderStates.Remove(view, out var failedState))
                    {
                        failedState.Fragment?.Dispose();
                    }
                }
                throw;
            }
        }

        private void Detach(View view)
        {
            var handle = view.RuntimeViewHandle;
            Exception? lifecycleFailure = null;
            try
            {
                view.DetachRuntime();
            }
            catch (Exception exception)
            {
                lifecycleFailure = exception;
            }
            finally
            {
                if (handle != 0)
                {
                    _viewsByHandle.Remove(handle);
                }
                _attachedViews.Remove(view);

                lock (_renderStateGate)
                {
                    if (_renderStates.Remove(view, out var state))
                    {
                        state.Parent = null;
                        state.Fragment?.Dispose();
                    }
                }
            }

            if (lifecycleFailure is not null)
            {
                ExceptionDispatchInfo.Capture(lifecycleFailure).Throw();
            }
        }

        private RetainedViewState GetRenderState(View view)
        {
            lock (_renderStateGate)
            {
                if (!_renderStates.TryGetValue(view, out var state))
                {
                    state = new RetainedViewState();
                    _renderStates.Add(view, state);
                }
                return state;
            }
        }

        private void MarkDirty(View view)
        {
            lock (_renderStateGate)
            {
                if (Volatile.Read(ref _stopped) != 0 || view.RuntimeViewHandle == 0)
                {
                    return;
                }

                View? current = view;
                while (current is not null)
                {
                    if (!_renderStates.TryGetValue(current, out var state))
                    {
                        return;
                    }
                    state.RequiredVersion++;
                    current = state.Parent;
                }
            }
        }

        private void MarkViewFragmentDirty(View view)
        {
            lock (_renderStateGate)
            {
                if (_renderStates.TryGetValue(view, out var state))
                {
                    state.RequiredVersion++;
                }
            }
        }

        private void BeginComposition(View view)
        {
            if (!_renderingViews.Add(view))
            {
                throw new InvalidOperationException(
                    "Managed views cannot recursively render themselves."
                );
            }

            var state = GetRenderState(view);
            state.WorkingChildren?.Clear();
            state.WorkingViews?.Clear();
            state.WorkingNextPosition = 0;
        }

        private void CompleteComposition(View view)
        {
            if (!_renderingViews.Remove(view))
            {
                throw new InvalidOperationException("Managed view composition was not active.");
            }

            var state = GetRenderState(view);
            (state.StagedChildren, state.WorkingChildren) = (
                state.WorkingChildren,
                state.StagedChildren
            );
            state.WorkingChildren?.Clear();
            state.WorkingViews?.Clear();
            state.HasStagedComposition = true;
        }

        private void AbortComposition(View view)
        {
            if (!_renderingViews.Remove(view))
            {
                return;
            }

            var state = GetRenderState(view);
            state.WorkingChildren?.Clear();
            state.WorkingViews?.Clear();
            state.WorkingNextPosition = 0;
        }

        private void CommitSnapshotTree()
        {
            _snapshotStack.Clear();
            _snapshotVisited.Clear();

            lock (_renderStateGate)
            {
                _snapshotStack.Push(RootView);
                while (_snapshotStack.TryPop(out var current))
                {
                    if (!_snapshotVisited.Add(current))
                    {
                        continue;
                    }
                    if (!_renderStates.TryGetValue(current, out var state))
                    {
                        throw new InvalidOperationException(
                            "The committed managed view tree references a missing render state."
                        );
                    }

                    if (state.HasStagedComposition)
                    {
                        (state.Children, state.StagedChildren) = (
                            state.StagedChildren,
                            state.Children
                        );
                        state.StagedChildren?.Clear();
                        state.HasStagedComposition = false;
                        state.Candidates?.Clear();
                    }

                    if (state.Children is null)
                    {
                        continue;
                    }

                    foreach (var entry in state.Children.Values)
                    {
                        if (!_renderStates.TryGetValue(entry.View, out var childState))
                        {
                            throw new InvalidOperationException(
                                "A committed child View is missing its retained render state."
                            );
                        }
                        if (
                            childState.Parent is not null
                            && !ReferenceEquals(childState.Parent, current)
                        )
                        {
                            throw new InvalidOperationException(
                                "A managed child View cannot be committed under multiple parents."
                            );
                        }
                        childState.Parent = current;
                        _snapshotStack.Push(entry.View);
                    }
                }
            }

            BuildDetachOrder(includeCommittedTree: false);
            foreach (var view in _detachCandidates)
            {
                try
                {
                    Detach(view);
                }
                catch (Exception exception)
                {
                    // The new ownership graph is already committed. Cleanup is best-effort here;
                    // retain lifecycle failures for the final session result.
                    RecordFailure(exception);
                }
            }
        }

        private void BuildDetachOrder(bool includeCommittedTree)
        {
            _detachCandidates.Clear();
            _detachStack.Clear();
            _detachVisited.Clear();

            lock (_renderStateGate)
            {
                foreach (var view in _attachedViews)
                {
                    if (!includeCommittedTree && _snapshotVisited.Contains(view))
                    {
                        continue;
                    }

                    _detachStack.Push((view, false));
                    while (_detachStack.TryPop(out var entry))
                    {
                        if (entry.Expanded)
                        {
                            _detachCandidates.Add(entry.View);
                            continue;
                        }

                        if (!_detachVisited.Add(entry.View))
                        {
                            continue;
                        }

                        _detachStack.Push((entry.View, true));
                        if (!_renderStates.TryGetValue(entry.View, out var state))
                        {
                            continue;
                        }

                        PushCleanupChildren(state.Children, includeCommittedTree);
                        if (state.HasStagedComposition)
                        {
                            PushCleanupChildren(state.StagedChildren, includeCommittedTree);
                        }
                        PushCleanupChildren(state.Candidates, includeCommittedTree);
                    }
                }
            }
        }

        private void PushCleanupChildren(
            Dictionary<ChildSlot, ChildEntry>? children,
            bool includeCommittedTree
        )
        {
            if (children is null)
            {
                return;
            }

            foreach (var child in children.Values)
            {
                if (!includeCommittedTree && _snapshotVisited.Contains(child.View))
                {
                    continue;
                }
                _detachStack.Push((child.View, false));
            }
        }

        private enum ChildOwnership : byte
        {
            Framework,
            Explicit,
        }

        private readonly struct ChildEntry
        {
            internal ChildEntry(View view, ChildOwnership ownership)
            {
                View = view;
                Ownership = ownership;
            }

            internal View View { get; }
            internal ChildOwnership Ownership { get; }
        }

        private static Dictionary<ChildSlot, ChildEntry> GetWorkingChildren(
            RetainedViewState state
        ) => state.WorkingChildren ??= [];

        private static Dictionary<ChildSlot, ChildEntry> GetCandidates(RetainedViewState state) =>
            state.Candidates ??= [];

        private static HashSet<View> GetWorkingViews(RetainedViewState state) =>
            state.WorkingViews ??= new HashSet<View>(ViewIdentity);

        private sealed class RetainedViewState
        {
            internal View? Parent;
            internal RenderArenaOwner? Fragment;
            internal uint Root;
            internal long RequiredVersion = 1;
            internal long RenderedVersion;
            internal uint WorkingNextPosition;
            internal bool HasStagedComposition;
            internal Dictionary<ChildSlot, ChildEntry>? Children;
            internal Dictionary<ChildSlot, ChildEntry>? WorkingChildren;
            internal Dictionary<ChildSlot, ChildEntry>? StagedChildren;
            internal Dictionary<ChildSlot, ChildEntry>? Candidates;
            internal HashSet<View>? WorkingViews;
        }

        private sealed class GpuiSynchronizationContext(ManagedSession session)
            : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                ArgumentNullException.ThrowIfNull(d);
                session.Post(() => d(state));
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                ArgumentNullException.ThrowIfNull(d);
                if (!ReferenceEquals(Current, this))
                {
                    throw new NotSupportedException(
                        "Synchronous cross-thread dispatch is not supported."
                    );
                }
                d(state);
            }

            public override SynchronizationContext CreateCopy() => this;
        }
    }
}
