namespace Gpui.Interop.Internal;

internal enum ExecutionPhase
{
    Idle,
    Ingress,
    Render,
    DemandRender,
    ArtifactRelease,
    ArtifactAcceptance,
    Acceptance,
    Event,
    Cleanup,
}

/// <summary>Shared callback-thread identity and external reentrancy guard for one application.</summary>
internal sealed class ApplicationExecution
{
    [ThreadStatic] internal static ApplicationExecution? Current;
    private int _threadId;
    private HashSet<Session.ManagedSession>? _reactiveSessions;
    private HashSet<Session.ManagedSession>? _failedSessions;
    internal ExecutionPhase Phase { get; private set; }

    internal static void AssertEffectsAllowed()
    {
        // Current is thread-local: worker ingress remains valid while the UI thread renders.
        if (Current?.Phase is ExecutionPhase.Render or ExecutionPhase.DemandRender
            || ViewOwnership.Constructing || ReactiveConsumer.Comparing)
            throw new InvalidOperationException("Framework effects are not allowed during rendering.");
    }

    internal void BindThread()
    {
        var current = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _threadId, current, 0);
        if (owner != 0 && owner != current)
        {
            throw new InvalidOperationException(
                "Managed application callbacks must run on the GPUI application thread."
            );
        }
    }

    internal void AssertAccess()
    {
        if (Volatile.Read(ref _threadId) != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "Managed application state is confined to the GPUI application thread."
            );
        }
    }

    internal bool HasAccess => Volatile.Read(ref _threadId) == Environment.CurrentManagedThreadId;

    internal Scope Enter(ExecutionPhase phase)
    {
        BindThread();
        if (Phase != ExecutionPhase.Idle || Current is not null)
        {
            throw new InvalidOperationException(
                $"Nested managed callbacks are not supported ({Phase} -> {phase})."
            );
        }
        Phase = phase;
        Current = this;
        return new Scope(this);
    }

    internal void ScheduleArtifacts(Session.ManagedSession session) =>
        (_reactiveSessions ??= []).Add(session);

    internal void ScheduleFailure(Session.ManagedSession session) =>
        (_failedSessions ??= []).Add(session);

    private void RetireFailures()
    {
        if (_failedSessions is null || _failedSessions.Count == 0) return;
        Phase = ExecutionPhase.Cleanup;
        while (_failedSessions.Count != 0)
        {
            var session = _failedSessions.First();
            _failedSessions.Remove(session);
            session.RetireFailedViews();
        }
    }

    private void FlushArtifacts(ref Exception? failure)
    {
        if (_reactiveSessions is null)
            return;
        foreach (var session in _reactiveSessions)
        {
            try { session.FlushReactiveArtifacts(); }
            catch (Exception exception) { session.RecordFailure(exception); failure ??= exception; }
        }
        _reactiveSessions.Clear();
    }

    private void DrainBoundary()
    {
        Exception? failure = null;
        do
        {
            FlushArtifacts(ref failure);
            RetireFailures();
            // Retirement can change Signals in surviving windows. Flush those rows and
            // retire any resulting failures before returning control to native or the caller.
        } while (_reactiveSessions is { Count: > 0 });
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal void SetPhase(ExecutionPhase phase)
    {
        AssertAccess();
        Phase = phase;
    }

    internal readonly struct Scope(ApplicationExecution? execution) : IDisposable
    {
        public void Dispose()
        {
            if (execution is not null)
            {
                try { execution.DrainBoundary(); }
                finally { execution.Phase = ExecutionPhase.Idle; Current = null; }
            }
        }
    }
}
