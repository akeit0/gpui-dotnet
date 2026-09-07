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
    private readonly ConcurrentQueue<IngressWork> _ingress = new();
    private readonly HashSet<ViewBase> _attachedViews = new(ViewIdentity);
    private readonly Dictionary<uint, ViewBase> _viewsByHandle = [];
    private readonly Dictionary<ViewBase, RetainedViewState> _renderStates = new(ViewIdentity);
    private readonly HashSet<ViewBase> _renderingViews = new(ViewIdentity);
    private readonly Stack<ViewBase> _snapshotStack = new();
    private readonly List<ViewBase> _unmountCandidates = [];
    private readonly Stack<(ViewBase View, bool Expanded)> _unmountStack = new();
    private readonly HashSet<ViewBase> _unmountVisited = new(ViewIdentity);
    private ExceptionDispatchInfo? _failure;
    private uint _nextViewHandle;
    private int _renderingStarted;
    private int _renderingManaged;
    private int _notifyAfterRender;
    private int _stopped;
    private int _allViewsPending;
    private int _notificationPending;
    private ulong _nextRenderRevision;
    private ulong _pendingRenderRevision;
    private readonly List<ViewBase> _acceptedViews = [];

    internal ulong PendingRenderRevision => _pendingRenderRevision;

    private readonly record struct IngressWork(object? Target);
    private ApplicationExecution Execution => _application.Execution;
    private bool IsAcceptingWork => Volatile.Read(ref _stopped) == 0 && Failure is null;
    private const int MaxIngressPerRender = 1024;

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

    private RootViewDeclaration? _rootDeclaration;
    private readonly GpuiWindow? _window;
    private ViewBase? _rootView;
    internal ViewBase RootView
    {
        get => _rootView ?? throw new InvalidOperationException("Root construction has not run.");
        private set => _rootView = value;
    }

    internal ManagedSession(NativeRuntime runtime, GpuiApplication application, ulong sessionId,
        RootViewDeclaration declaration, GpuiWindow window)
    {
        _runtime = runtime;
        _application = application;
        _sessionId = sessionId;
        _rootDeclaration = declaration;
        _window = window;
        SynchronizationContext = new GpuiSynchronizationContext(this);
    }
    internal SynchronizationContext SynchronizationContext { get; }
    internal Exception? Failure => Volatile.Read(ref _failure)?.SourceException;

    private int _failureWakePending;

    internal void RecordFailure(Exception exception, bool deferCleanup = false)
    {
        if (Volatile.Read(ref _failure) is null)
        {
            Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);
        }
        DiscardIngress();
        if (deferCleanup || (ReferenceEquals(ApplicationExecution.Current, Execution)
            && Execution.Phase is ExecutionPhase.ArtifactRelease or ExecutionPhase.ArtifactAcceptance))
        {
            // Artifact callbacks cannot invoke application cleanup while native reconciles resources.
            if (Interlocked.Exchange(ref _failureWakePending, 1) == 0)
                try { _runtime.NotifyView(_sessionId); } catch (Exception) { }
            return;
        }
        if (ReferenceEquals(ApplicationExecution.Current, Execution)) Execution.ScheduleFailure(this);
        else if (ApplicationExecution.Current is null && Execution.HasAccess)
        {
            using var cleanup = Execution.Enter(ExecutionPhase.Cleanup);
            Execution.ScheduleFailure(this);
        }
    }

    private void ThrowIfUnavailable() => ThrowIfUnavailable(retireFailure: true);

    private void ThrowIfUnavailable(bool retireFailure)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
        if (retireFailure && Failure is not null && ApplicationExecution.Current is null && Execution.HasAccess)
        {
            using var cleanup = Execution.Enter(ExecutionPhase.Cleanup);
            Execution.ScheduleFailure(this);
        }
        Volatile.Read(ref _failure)?.Throw();
    }

    private void RequireAcceptedRender()
    {
        if (_pendingRenderRevision != 0)
        {
            throw new InvalidOperationException("Published root output is awaiting native acceptance.");
        }
    }

    private void DiscardIngress()
    {
        while (_ingress.TryDequeue(out var work))
        {
            if (work.Target is ViewBase view)
                view.Runtime.ConsumeInvalidation();
        }
        Volatile.Write(ref _allViewsPending, 0);
    }

    internal void Invalidate(ViewBase view)
    {
        if (!IsAcceptingWork || !view.Runtime.TryQueueInvalidation())
        {
            return;
        }
        Enqueue(new IngressWork(view), notify: true);
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
        Interlocked.Exchange(ref _codeUpdatePending, 1);
        InvalidateAllViews(notify: false);
    }

    private int _codeUpdatePending;

    private void InvalidateAllViews(bool notify)
    {
        if (!IsAcceptingWork)
        {
            return;
        }
        if (Interlocked.Exchange(ref _allViewsPending, 1) != 0)
        {
            if (notify)
            {
                NotifyRenderPending();
            }
            return;
        }

        Enqueue(default, notify);
    }

    /// <summary>
    /// Marks the retained fragment owning a native Dynamic wrapper dirty. Native already owns the
    /// frame wake-up, so this intentionally does not enqueue a second notification.
    /// </summary>
    internal void PrepareDynamicFrame(uint ownerView)
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.Ingress);
        RequireAcceptedRender();
        if (ownerView == 0)
        {
            return;
        }
        if (_viewsByHandle.TryGetValue(ownerView, out var view) && view.Runtime.IsMounted)
        {
            MarkDirty(view);
        }
    }

    internal void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!IsAcceptingWork)
        {
            return;
        }

        Enqueue(new IngressWork(callback), notify: true);
    }

    internal void Post(IIngressWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsAcceptingWork)
            Enqueue(new IngressWork(work), notify: true);
    }

    internal void Send(SendOrPostCallback callback, object? state)
    {
        ThrowIfUnavailable();
        using var execution = Execution.Enter(ExecutionPhase.Event);
        RequireAcceptedRender();
        try
        {
            callback(state);
            ThrowIfUnavailable();
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            throw;
        }
    }

    private void Enqueue(IngressWork work, bool notify)
    {
        _ingress.Enqueue(work);
        if (!IsAcceptingWork)
        {
            DiscardIngress();
            return;
        }
        if (notify)
        {
            NotifyRenderPending();
        }
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

        if (Interlocked.Exchange(ref _notificationPending, 1) == 0)
        {
            try
            {
                _runtime.NotifyView(_sessionId);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _notificationPending, 0);
                RecordFailure(exception);
                throw;
            }
        }
    }

    private void BeginRendering()
    {
        Volatile.Write(ref _renderingStarted, 1);
        Volatile.Write(ref _renderingManaged, 1);
        Volatile.Write(ref _notifyAfterRender, 0);
        Volatile.Write(ref _notificationPending, 0);

        // Bound each drain so self-posting producers cannot starve rendering indefinitely.
        var remaining = MaxIngressPerRender;
        while (remaining-- > 0 && _ingress.TryDequeue(out var work))
        {
            ThrowIfUnavailable();
            if (work.Target is ViewBase view)
            {
                view.Runtime.ConsumeInvalidation();
                MarkDirty(view);
            }
            else if (work.Target is Action callback)
            {
                callback();
            }
            else if (work.Target is IIngressWork operation)
            {
                operation.Invoke();
            }
            else
            {
                Volatile.Write(ref _allViewsPending, 0);
                foreach (var state in _renderStates.Values)
                {
                    state.Dirty = true;
                }
            }
        }
        ThrowIfUnavailable();
        Execution.SetPhase(ExecutionPhase.Render);
        if (Interlocked.Exchange(ref _codeUpdatePending, 0) != 0)
            foreach (var view in _attachedViews) view.Ownership.ClearCaches();
    }

    private void EndRendering()
    {
        Volatile.Write(ref _renderingManaged, 0);
        if (
            IsAcceptingWork
            && (Interlocked.Exchange(ref _notifyAfterRender, 0) != 0 || !_ingress.IsEmpty)
        )
        {
            NotifyRenderPending();
        }
    }
}
