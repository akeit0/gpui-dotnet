using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Gpui;

namespace Gpui.Interop.Internal.Session;

internal sealed unsafe partial class ManagedSession : IViewRenderer
{
    private static readonly IEqualityComparer<View> ViewIdentity =
        ReferenceEqualityComparer.Instance;

    private readonly NativeRuntime _runtime;
    private readonly GpuiApplication _application;
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
