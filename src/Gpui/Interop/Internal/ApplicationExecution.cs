namespace Gpui.Interop.Internal;

internal enum ExecutionPhase
{
    Idle,
    Ingress,
    Render,
    DemandRender,
    Event,
    Cleanup,
}

/// <summary>Shared callback-thread identity and external reentrancy guard for one application.</summary>
internal sealed class ApplicationExecution
{
    private int _threadId;
    internal ExecutionPhase Phase { get; private set; }

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
        if (Phase != ExecutionPhase.Idle)
        {
            throw new InvalidOperationException(
                $"Nested managed callbacks are not supported ({Phase} -> {phase})."
            );
        }
        Phase = phase;
        return new Scope(this);
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
                execution.Phase = ExecutionPhase.Idle;
            }
        }
    }
}
