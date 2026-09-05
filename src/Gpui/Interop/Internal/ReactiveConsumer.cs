using Gpui.Interop.Internal.Session;

namespace Gpui.Interop.Internal;

internal interface ISignal
{
    ulong Revision { get; }
    void Attach(DependencyEdge edge);
    void Detach(DependencyEdge edge);
}

internal sealed class DependencyEdge(ISignal signal, ReactiveConsumer consumer)
{
    internal readonly ISignal Signal = signal;
    internal readonly ReactiveConsumer Consumer = consumer;
    internal DependencyEdge? Previous;
    internal DependencyEdge? Next;
    internal bool Attached;
    internal ulong Pass;
    internal ulong Revision;
}

/// <summary>Reusable observations whose edges become live only at native acceptance.</summary>
internal sealed class ReactiveConsumer(ManagedSession session, ViewBase owner, ulong source = 0, ulong artifact = 0)
{
    [ThreadStatic] internal static ReactiveConsumer? Current;
    [ThreadStatic] internal static bool Comparing;
    internal readonly ViewBase Owner = owner;
    internal readonly ulong Source = source;
    internal readonly ulong Artifact = artifact;
    internal ApplicationExecution Execution => session.ReactiveExecution;
    internal bool Accepted { get; private set; }
    private bool _invalidated;
    private ulong _pass;
    private Dictionary<ISignal, DependencyEdge>? _edges;

    internal ReadScope Begin()
    {
        _pass = checked(_pass + 1);
        var previous = Current;
        Current = this;
        return new ReadScope(previous);
    }

    internal void Read(ISignal signal)
    {
        var edges = _edges ??= new(ReferenceEqualityComparer.Instance);
        if (!edges.TryGetValue(signal, out var edge))
        {
            edge = new DependencyEdge(signal, this);
            edges.Add(signal, edge);
        }
        edge.Pass = _pass;
        edge.Revision = signal.Revision;
    }

    internal void Commit()
    {
        Accepted = true;
        _invalidated = false;
        var changed = false;
        if (_edges is not null)
        {
            foreach (var (signal, edge) in _edges)
            {
                if (edge.Pass != _pass)
                {
                    signal.Detach(edge);
                    _edges.Remove(signal);
                }
                else
                {
                    if (!edge.Attached)
                        signal.Attach(edge);
                    changed |= edge.Revision != signal.Revision;
                }
            }
        }
        if (changed)
            Invalidate();
    }

    internal void Invalidate()
    {
        if (_invalidated || !Accepted)
            return;
        _invalidated = true;
        session.InvalidateReactiveConsumer(this);
    }

    internal void Abort()
    {
        if (_edges is null)
            return;
        foreach (var (signal, edge) in _edges)
            if (!edge.Attached)
                _edges.Remove(signal);
    }

    internal void Dispose()
    {
        if (_edges is not null)
        {
            foreach (var edge in _edges.Values)
                edge.Signal.Detach(edge);
            _edges.Clear();
        }
        Accepted = false;
    }

    internal readonly struct ReadScope(ReactiveConsumer? previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
