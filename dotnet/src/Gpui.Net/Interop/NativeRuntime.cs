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
    private static readonly ConcurrentDictionary<ulong, ManagedSession> Sessions = new();
    private static long _nextSessionId;

    private readonly GpuiDotnetApiV2* _api;

    private NativeRuntime(GpuiDotnetApiV2* api) => _api = api;

    public static NativeRuntime Load()
    {
        var api = NativeMethods.gpui_dotnet_get_api(NativeConstants.AbiVersion);
        if (api == null)
        {
            throw new InvalidOperationException(
                $"gpui-dotnet native ABI {NativeConstants.AbiVersion} is not supported."
            );
        }

        if (api->struct_size < (uint)sizeof(GpuiDotnetApiV2))
        {
            throw new InvalidOperationException(
                $"Native API table is too small. Managed={sizeof(GpuiDotnetApiV2)}, native={api->struct_size}."
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

        if (api->validate_render == null || api->run_application == null || api->notify_view == null)
        {
            throw new InvalidOperationException("The native API table is missing required entries.");
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

    internal void Run(View view, GpuiApplicationOptions options)
    {
        var id = checked((ulong)Interlocked.Increment(ref _nextSessionId));
        var session = new ManagedSession(this, id, view);
        if (!Sessions.TryAdd(id, session))
        {
            throw new InvalidOperationException("Failed to register the managed view session.");
        }

        var status = 0;
        try
        {
            var callbacks = new ManagedCallbacks
            {
                struct_size = (uint)sizeof(ManagedCallbacks),
                render = &Render,
                click = &Click,
                shutdown = &Shutdown,
            };
            var title = Encoding.UTF8.GetBytes(options.Title);
            fixed (byte* titlePointer = title)
            {
                status = _api->run_application(
                    id,
                    &callbacks,
                    titlePointer,
                    title.Length,
                    options.Width,
                    options.Height
                );
            }
        }
        finally
        {
            // Stop first so OnUnmounted failures are part of the final session result.
            session.Stop();
            Sessions.TryRemove(id, out _);
        }

        if (status != 0)
        {
            throw new InvalidOperationException(
                $"The native GPUI application stopped with status {status}.",
                session.Failure
            );
        }

        if (session.Failure is not null)
        {
            throw new InvalidOperationException(
                "The managed GPUI session observed an event, render, or lifecycle failure.",
                session.Failure
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
    private static int Click(ulong sessionId, ulong eventToken, NativeClickEvent* nativeEvent)
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
    private static int Shutdown(ulong sessionId)
    {
        try
        {
            if (!Sessions.TryGetValue(sessionId, out var session))
            {
                return -120;
            }

            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(session.SynchronizationContext);
            try
            {
                session.Stop();
                return session.Failure is null ? 0 : -121;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
        catch (Exception exception)
        {
            RecordFailure(sessionId, exception);
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

    private sealed class ManagedSession : IViewRenderer
    {
        private static readonly IEqualityComparer<View> ViewIdentity = ReferenceEqualityComparer.Instance;

        private readonly NativeRuntime _runtime;
        private readonly ulong _sessionId;
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly HashSet<View> _attachedViews = new(ViewIdentity);
        private readonly Dictionary<uint, View> _viewsByHandle = [];
        private readonly object _renderStateGate = new();
        private readonly Dictionary<View, RetainedViewState> _renderStates = new(ViewIdentity);
        private readonly Dictionary<View, HashSet<View>> _childrenByParent = new(ViewIdentity);
        private readonly Dictionary<View, HashSet<View>> _parentsByChild = new(ViewIdentity);
        private readonly HashSet<View> _renderingViews = new(ViewIdentity);
        private readonly Stack<View> _dirtyStack = new();
        private readonly HashSet<View> _dirtyVisited = new(ViewIdentity);
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
                Attach(RootView);
                BeginComposition(RootView);
                try
                {
                    var ui = new RenderContext(arena, this, RootView);
                    var element = RootView.RenderCore(ref ui);
                    ManagedValidator.Validate(arena, element);
                    CompleteComposition(RootView);
                    CommitSnapshotTree();
                    return element;
                }
                catch
                {
                    AbortComposition(RootView);
                    throw;
                }
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
                _childrenByParent.Clear();
                _parentsByChild.Clear();
                _dirtyStack.Clear();
                _dirtyVisited.Clear();
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

        Element IViewRenderer.RenderChild(View view, View parent, RenderArena* destination)
        {
            if (_renderingViews.Contains(view))
            {
                throw new InvalidOperationException("Managed views cannot recursively render themselves.");
            }
            if (!_renderingViews.Contains(parent))
            {
                throw new InvalidOperationException(
                    "A child view can only be rendered while its parent is rendering."
                );
            }

            if (!GetRenderState(parent).PendingChildren.Add(view))
            {
                throw new InvalidOperationException(
                    "The same managed child View instance cannot be rendered more than once by one parent."
                );
            }
            Attach(view);
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
                //_ = ObserveEventTaskAsync(pending);
            }
        }
        // TODO: use direct ConfigureAwait(false) UnsafeOncompleted, and cache delegate for efficiency.
        // Commented out because await is not allowed in unsafe context.
        // private async Task ObserveEventTaskAsync(ValueTask pending)
        // {
        //     try
        //     {
        //         await pending;
        //     }
        //     catch (OperationCanceledException)
        //     {
        //         // Cancellation is a normal event outcome; it must not surface as an unhandled
        //         // native callback failure.
        //     }
        //     catch (Exception exception)
        //     {
        //         RecordFailure(exception);
        //         Interlocked.CompareExchange(
        //             ref _pendingAsyncFailure,
        //             ExceptionDispatchInfo.Capture(exception),
        //             null
        //         );
        //         try
        //         {
        //             NotifyRenderPending();
        //         }
        //         catch (Exception notifyFailure)
        //         {
        //             RecordFailure(notifyFailure);
        //         }
        //     }
        // }

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
                view.AttachRuntime(handle, Post, Invalidate);
                _ = GetRenderState(view);
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
                    _childrenByParent.Remove(view);
                    _parentsByChild.Remove(view);
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
                        state.Fragment?.Dispose();
                        foreach (var child in state.Children)
                        {
                            if (_parentsByChild.TryGetValue(child, out var parents))
                            {
                                parents.Remove(view);
                                if (parents.Count == 0)
                                {
                                    _parentsByChild.Remove(child);
                                }
                            }
                        }
                    }

                    _childrenByParent.Remove(view);
                    if (_parentsByChild.Remove(view, out var oldParents))
                    {
                        foreach (var parent in oldParents)
                        {
                            if (_renderStates.TryGetValue(parent, out var parentState))
                            {
                                parentState.Children.Remove(view);
                                parentState.PendingChildren.Remove(view);
                            }
                        }
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
                    state = new RetainedViewState(ViewIdentity);
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

                _dirtyStack.Clear();
                _dirtyVisited.Clear();
                _dirtyStack.Push(view);
                while (_dirtyStack.TryPop(out var current))
                {
                    if (!_dirtyVisited.Add(current))
                    {
                        continue;
                    }

                    if (!_renderStates.TryGetValue(current, out var state))
                    {
                        state = new RetainedViewState(ViewIdentity);
                        _renderStates.Add(current, state);
                    }
                    state.RequiredVersion++;

                    if (_parentsByChild.TryGetValue(current, out var parents))
                    {
                        foreach (var parent in parents)
                        {
                            _dirtyStack.Push(parent);
                        }
                    }
                }
            }
        }

        private void BeginComposition(View view)
        {
            if (!_renderingViews.Add(view))
            {
                throw new InvalidOperationException("Managed views cannot recursively render themselves.");
            }
            GetRenderState(view).PendingChildren.Clear();
        }

        private void CompleteComposition(View view)
        {
            _renderingViews.Remove(view);
            var state = GetRenderState(view);

            lock (_renderStateGate)
            {
                // Validate the pending relationship before mutating the committed graph so a
                // reparenting error cannot leave a half-committed tree behind.
                foreach (var child in state.PendingChildren)
                {
                    if (
                        _parentsByChild.TryGetValue(child, out var existingParents)
                        && existingParents.Count != 0
                        && !existingParents.Contains(view)
                    )
                    {
                        throw new InvalidOperationException(
                            "A managed child View may have only one parent in a committed tree."
                        );
                    }
                }

                foreach (var child in state.Children)
                {
                    if (_parentsByChild.TryGetValue(child, out var parents))
                    {
                        parents.Remove(view);
                        if (parents.Count == 0)
                        {
                            _parentsByChild.Remove(child);
                        }
                    }
                }

                (state.Children, state.PendingChildren) = (state.PendingChildren, state.Children);
                _childrenByParent[view] = state.Children;
                foreach (var child in state.Children)
                {
                    if (!_parentsByChild.TryGetValue(child, out var parents))
                    {
                        parents = new HashSet<View>(ViewIdentity);
                        _parentsByChild.Add(child, parents);
                    }
                    parents.Add(view);
                }
            }
        }

        private void AbortComposition(View view)
        {
            _renderingViews.Remove(view);
            GetRenderState(view).PendingChildren.Clear();
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
                    if (_childrenByParent.TryGetValue(current, out var children))
                    {
                        foreach (var child in children)
                        {
                            _snapshotStack.Push(child);
                        }
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
                    // retain the lifecycle error for the final session result instead of leaving
                    // composition half-published.
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
                        if (_childrenByParent.TryGetValue(entry.View, out var children))
                        {
                            foreach (var child in children)
                            {
                                if (includeCommittedTree || !_snapshotVisited.Contains(child))
                                {
                                    _detachStack.Push((child, false));
                                }
                            }
                        }
                    }
                }
            }
        }

        private sealed class RetainedViewState
        {
            internal RetainedViewState(IEqualityComparer<View> comparer)
            {
                Children = new HashSet<View>(comparer);
                PendingChildren = new HashSet<View>(comparer);
            }

            internal RenderArenaOwner? Fragment;
            internal uint Root;
            internal long RequiredVersion = 1;
            internal long RenderedVersion;
            internal HashSet<View> Children;
            internal HashSet<View> PendingChildren;
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
