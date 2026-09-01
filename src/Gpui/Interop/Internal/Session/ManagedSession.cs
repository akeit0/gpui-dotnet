using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession : IViewRenderer
{
    private static readonly IEqualityComparer<ViewBase> ViewIdentity =
        ReferenceEqualityComparer.Instance;

    private readonly NativeRuntime _runtime;
    private readonly GpuiApplication _application;
    private readonly ulong _sessionId;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly HashSet<ViewBase> _attachedViews = new(ViewIdentity);
    private readonly Dictionary<uint, ViewBase> _viewsByHandle = [];
    private readonly object _renderStateGate = new();
    private readonly Dictionary<ViewBase, RetainedViewState> _renderStates = new(ViewIdentity);
    private readonly HashSet<ViewBase> _renderingViews = new(ViewIdentity);
    private readonly Stack<ViewBase> _snapshotStack = new();
    private readonly HashSet<ViewBase> _snapshotVisited = new(ViewIdentity);
    private readonly List<ViewBase> _unmountCandidates = [];
    private readonly Stack<(ViewBase View, bool Expanded)> _unmountStack = new();
    private readonly HashSet<ViewBase> _unmountVisited = new(ViewIdentity);
    private ExceptionDispatchInfo? _pendingAsyncFailure;
    private Exception? _failure;
    private uint _nextViewHandle;
    private int _renderingStarted;
    private int _renderingManaged;
    private int _notifyAfterRender;
    private int _stopped;
    private Exception? _renderFailure;

    internal ManagedSession(
        NativeRuntime runtime,
        GpuiApplication application,
        ulong sessionId,
        View view
    )
    {
        _runtime = runtime;
        _application = application;
        _sessionId = sessionId;
        RootView = view;
        SynchronizationContext = new GpuiSynchronizationContext(this);
    }

    internal View RootView { get; }
    internal SynchronizationContext SynchronizationContext { get; }
    internal Exception? Failure => Volatile.Read(ref _failure) ?? Volatile.Read(ref _renderFailure);

    internal void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _failure, exception, null);

    internal void RecordRenderFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _renderFailure, exception, null);

    internal void Invalidate(ViewBase view)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }
        MarkDirty(view);
        NotifyRenderPending();
    }

    /// <summary>
    /// Invalidates every retained View fragment in this window. Application-wide inputs such as
    /// the active theme are not props, so descendants cannot discover those changes through the
    /// normal parent/child reconciliation path.
    /// </summary>
    internal void InvalidateAllViews()
    {
        InvalidateAllViews(notify: true);
    }

    internal void PrepareManagedCodeUpdate()
    {
        Volatile.Write(ref _renderFailure, null);
        InvalidateAllViews(notify: false);
    }

    private void InvalidateAllViews(bool notify)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        lock (_renderStateGate)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }

            foreach (var state in _renderStates.Values)
            {
                state.RequiredVersion++;
            }
        }

        if (notify)
        {
            NotifyRenderPending();
        }
    }

    /// <summary>
    /// Marks the retained fragment owning a native Dynamic wrapper dirty. Native already owns the
    /// frame wake-up, so this intentionally does not enqueue a second notification.
    /// </summary>
    internal void PrepareDynamicFrame(uint ownerView)
    {
        if (Volatile.Read(ref _stopped) != 0 || ownerView == 0)
        {
            return;
        }
        if (_viewsByHandle.TryGetValue(ownerView, out var view) && view.IsMountedCore)
        {
            MarkDirty(view);
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
}
