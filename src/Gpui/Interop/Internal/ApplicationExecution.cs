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
    internal ExecutionPhase Phase { get; private set; }

    internal static void AssertEffectsAllowed()
    {
        // Current is thread-local: worker ingress remains valid while the UI thread renders.
        if (Current?.Phase is ExecutionPhase.Render or ExecutionPhase.DemandRender)
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

    private void FlushArtifacts()
    {
        if (_reactiveSessions is null)
            return;
        Exception? failure = null;
        foreach (var session in _reactiveSessions)
        {
            try { session.FlushReactiveArtifacts(); }
            catch (Exception exception) { session.RecordFailure(exception); failure ??= exception; }
        }
        _reactiveSessions.Clear();
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
                try { execution.FlushArtifacts(); }
                finally { execution.Phase = ExecutionPhase.Idle; Current = null; }
            }
        }
    }
}
