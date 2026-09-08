using System.Runtime.ExceptionServices;
using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>Task observation and foreground completion owned by a View or accepted effect.</summary>
public sealed class WorkScope
{
    private List<PendingWork>? _pendingWork;
    private ViewCommandRoute? _route;
    private CancellationToken _lifetime;
    private readonly int _threadId;
    private ViewRuntime? _runtime;
    private PendingWork? _latest;
    private List<PendingWork>? _retiredWork;

    internal WorkScope(ViewRuntime runtime)
    {
        _runtime = runtime;
        _threadId = Environment.CurrentManagedThreadId;
    }

    internal WorkScope(ViewCommandRoute route, CancellationToken lifetime, int threadId)
    {
        _route = route;
        _lifetime = lifetime;
        _threadId = threadId;
    }

    /// <summary>
    /// Invokes a static producer on the calling UI thread with a request snapshot and this View's
    /// lifetime token. The application owns offloading. Completion runs through UI ingress while
    /// the View remains mounted. Calls are independent; no latest-request policy is implied.
    /// </summary>
    /// <remarks>
    /// Completion state is separate from the request, which must not contain UI references.
    /// Prefer static callbacks to avoid delegate allocation per call. Retirement releases state
    /// and callbacks even if production ignores cancellation. Unhandled failures fault the session.
    /// Cancellation invokes the optional cancelled callback; without it, cancellation is ignored.
    /// Every completion callback must be synchronous, including callbacks supplied indirectly.
    /// </remarks>
    public void Start<TState, TRequest, TResult>(
        TState state,
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        Action<TState, TResult> complete,
        Action<TState, Exception>? failed = null,
        Action<TState>? cancelled = null
    ) => _ = StartCore(state, request, produce, complete, failed, cancelled);

    /// <summary>Replaces the previous latest request, revoking delivery before requesting cancellation.</summary>
    public void StartLatest<TState, TRequest, TResult>(
        TState state,
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        Action<TState, TResult> complete,
        Action<TState, Exception>? failed = null,
        Action<TState>? cancelled = null
    ) => _ = StartCore(state, request, produce, complete, failed, cancelled, latest: true);

    internal PendingWork StartCore<TState, TRequest, TResult>(
        TState state,
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> produce,
        Action<TState, TResult> complete,
        Action<TState, Exception>? failed = null,
        Action<TState>? cancelled = null,
        bool latest = false
    )
    {
        ArgumentNullException.ThrowIfNull(produce);
        ArgumentNullException.ThrowIfNull(complete);
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Work must start on the owning UI thread.");
        ApplicationExecution.AssertEffectsAllowed();
        if (_route is null && _runtime is not null)
        {
            _route = _runtime.RequireActiveRoute();
            _lifetime = _runtime.Lifetime;
        }
        var route = _route ?? throw new InvalidOperationException("The work scope has retired.");
        if (
            ApplicationExecution.Current?.Phase
            is ExecutionPhase.Render
                or ExecutionPhase.DemandRender
        )
            throw new InvalidOperationException("Asynchronous work cannot start during rendering.");
        route.EnsureAvailable();

        var previous = latest ? _latest : null;
        var work = new PendingWork<TState, TResult>(state, complete, failed, cancelled);
        var pending = _pendingWork ??= [];
        work.Attach(this, pending.Count);
        if (latest)
        {
            work.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _latest = work;
        }
        pending.Add(work);
        try
        {
            if (previous is not null)
            {
                var cancellation = previous.Cancellation;
                previous.Cancellation = null;
                RemovePendingWork(previous);
                try
                {
                    cancellation?.Cancel();
                }
                finally
                {
                    cancellation?.Dispose();
                }
            }
            if (!ReferenceEquals(work.Owner, this))
            {
                // Cancellation can synchronously start a newer request or retire this scope.
                work.Observe(null, null, true);
                return work;
            }
            Task<TResult>? task = null;
            Exception? failure = null;
            var wasCancelled = false;
            try
            {
                task =
                    produce(request, work.Cancellation?.Token ?? _lifetime)
                    ?? throw new InvalidOperationException(
                        "The work producer returned a null Task."
                    );
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            work.Observe(task, failure, wasCancelled);
            return work;
        }
        catch
        {
            if (work.Owner is not null)
                RemovePendingWork(work);
            work.Cancellation?.Dispose();
            work.Cancellation = null;
            throw;
        }
    }

    private void RemovePendingWork(PendingWork work)
    {
        if (ReferenceEquals(_latest, work))
            _latest = null;
        var pending = _pendingWork!;
        var last = pending[^1];
        pending[work.Index] = last;
        last.Index = work.Index;
        pending.RemoveAt(pending.Count - 1);
        work.Release();
    }

    internal void Revoke()
    {
        _runtime = null;
        _latest = null;
        _route = null;
        _lifetime = default;
        if (_pendingWork is not { } pending)
            return;
        _pendingWork = null;
        foreach (var work in pending)
            work.Release();
        _retiredWork = pending;
    }

    internal void Retire()
    {
        Revoke();
        var pending = _retiredWork;
        _retiredWork = null;
        if (pending is null)
            return;
        List<Exception>? failures = null;
        foreach (var work in pending)
        {
            try
            {
                work.Cancellation?.Cancel();
            }
            catch (Exception e)
            {
                (failures ??= []).Add(e);
            }
            finally
            {
                work.Cancellation?.Dispose();
                work.Cancellation = null;
            }
        }
        if (failures is not null)
            throw new AggregateException(failures);
    }

    internal abstract class PendingWork : IIngressWork
    {
        private ViewCommandRoute? _route;
        private int _finished;

        // Owner, slot, and completion state are UI-thread-only. An external completion only
        // reads the stable route; retirement severs it before cancelling application work.
        internal WorkScope? Owner { get; private set; }
        internal int Index { get; set; }
        internal bool IsFinished => Volatile.Read(ref _finished) != 0;
        internal CancellationTokenSource? Cancellation;

        internal void Attach(WorkScope owner, int index)
        {
            Owner = owner;
            Index = index;
            _route = owner._route;
        }

        internal void Release()
        {
            Volatile.Write(ref _route, null);
            Owner = null;
            Index = -1;
            ClearCompletion();
        }

        protected void Publish()
        {
            try
            {
                if (Volatile.Read(ref _route) is { } route && route.TryPost(this))
                    return;
                ClearOutcome();
            }
            catch (Exception)
            {
                // Post records native wake-up failure. Do not leak it from the observer
                // or retry delivery into a terminal session.
                ClearOutcome();
            }
            finally
            {
                Volatile.Write(ref _finished, 1);
            }
        }

        protected abstract void ClearCompletion();
        protected abstract void ClearOutcome();
        public abstract void Invoke();
    }

    private sealed class PendingWork<TState, TResult>(
        TState state,
        Action<TState, TResult> complete,
        Action<TState, Exception>? failed,
        Action<TState>? cancelled
    ) : PendingWork
    {
        private TState _state = state;
        private Action<TState, TResult>? _complete = complete;
        private Action<TState, Exception>? _failed = failed;
        private Action<TState>? _cancelled = cancelled;
        private Task<TResult>? _task;
        private TResult _result = default!;
        private Exception? _failure;
        private bool _wasCancelled;

        internal void Observe(Task<TResult>? task, Exception? failure, bool wasCancelled)
        {
            _task = task;
            _failure = failure;
            _wasCancelled = wasCancelled;
            if (task is null || task.IsCompleted)
                OnCompleted();
            else
                task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(OnCompleted);
        }

        private void OnCompleted()
        {
            try
            {
                if (_task is { } task)
                {
                    // Task status defines cancellation. Avoid manufacturing a
                    // TaskCanceledException for the ordinary cancellation path.
                    if (task.IsCanceled)
                        _wasCancelled = true;
                    else if (task.IsFaulted)
                        // Match await's first-exception contract without throwing solely to
                        // transport the failure. Reading Exception marks the Task observed.
                        _failure = task.Exception!.InnerExceptions[0];
                    else
                        _result = task.GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                _failure = exception;
            }
            finally
            {
                _task = null;
            }
            Publish();
        }

        public override void Invoke()
        {
            try
            {
                if (Owner is not { } owner)
                    return;
                var state = _state;
                var complete = _complete!;
                var failed = _failed;
                var cancelled = _cancelled;
                owner.RemovePendingWork(this);
                Cancellation?.Dispose();
                Cancellation = null;
                if (_wasCancelled)
                    cancelled?.Invoke(state);
                else if (_failure is null)
                    complete(state, _result);
                else if (failed is not null)
                    failed(state, _failure);
                else
                    ExceptionDispatchInfo.Capture(_failure).Throw();
            }
            finally
            {
                ClearOutcome();
            }
        }

        protected override void ClearCompletion()
        {
            _state = default!;
            _complete = null;
            _failed = null;
            _cancelled = null;
        }

        protected override void ClearOutcome()
        {
            _result = default!;
            _failure = null;
            _wasCancelled = false;
        }
    }
}
