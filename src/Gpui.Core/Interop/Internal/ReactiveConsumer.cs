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
    internal ISignal? Signal = signal;
    internal readonly ReactiveConsumer Consumer = consumer;
    internal DependencyEdge? Previous;
    internal DependencyEdge? Next;
    internal bool Attached;
    internal ulong Pass;
    internal ulong Revision;
}

/// <summary>Reusable observations whose edges become live only at native acceptance.</summary>
internal sealed class ReactiveConsumer(
    ManagedSession session,
    ViewBase owner,
    ulong source = 0,
    ulong artifact = 0
)
{
    [ThreadStatic]
    internal static ReactiveConsumer? Current;

    [ThreadStatic]
    internal static bool Comparing;
    internal readonly ViewBase Owner = owner;
    internal readonly ulong Source = source;
    internal readonly ulong Artifact = artifact;
    internal ApplicationExecution Execution => session.ReactiveExecution;
    internal bool Accepted { get; private set; }
    private bool _invalidated;
    private ulong _pass;

    // One representation: a dense array for small sets, replaced by a dictionary on growth.
    private object? _edges;
    private int _edgeCount;
    private const int LinearLookupLimit = 64;

    // Detached storage belongs to this consumer and never retains an unused Signal.
    private DependencyEdge? _spareEdges;
    private int _spareCount;
    private const int MaxSpareEdges = 8;

    internal ReadScope Begin()
    {
        _pass = checked(_pass + 1);
        var previous = Current;
        Current = this;
        return new ReadScope(previous);
    }

    internal void Read(ISignal signal)
    {
        DependencyEdge? edge = null;
        if (_edges is Dictionary<ISignal, DependencyEdge> dictionary)
            dictionary.TryGetValue(signal, out edge);
        else if (_edges is DependencyEdge[] edges)
        {
            for (var index = 0; index < _edgeCount; index++)
            {
                if (ReferenceEquals(edges[index].Signal, signal))
                {
                    edge = edges[index];
                    break;
                }
            }
        }
        edge ??= AddEdge(signal);
        edge.Pass = _pass;
        edge.Revision = signal.Revision;
    }

    private DependencyEdge AddEdge(ISignal signal)
    {
        DependencyEdge edge;
        if (_spareEdges is { } spare)
        {
            _spareEdges = spare.Next;
            _spareCount--;
            spare.Next = null;
            spare.Signal = signal;
            edge = spare;
        }
        else
            edge = new DependencyEdge(signal, this);

        if (_edges is Dictionary<ISignal, DependencyEdge> dictionary)
            dictionary.Add(signal, edge);
        else
        {
            var edges = (DependencyEdge[]?)_edges;
            if (_edgeCount == LinearLookupLimit)
            {
                dictionary = new(_edgeCount + 1, ReferenceEqualityComparer.Instance);
                for (var index = 0; index < _edgeCount; index++)
                    dictionary.Add(edges![index].Signal!, edges[index]);
                dictionary.Add(signal, edge);
                _edges = dictionary;
                _edgeCount = 0;
            }
            else
            {
                if (edges is null || _edgeCount == edges.Length)
                {
                    Array.Resize(ref edges, edges is null ? 4 : edges.Length * 2);
                    _edges = edges;
                }
                edges[_edgeCount++] = edge;
            }
        }
        return edge;
    }

    internal void Commit()
    {
        Accepted = true;
        _invalidated = false;
        var changed = false;
        if (_edges is Dictionary<ISignal, DependencyEdge> dictionary)
        {
            foreach (var (signal, edge) in dictionary)
            {
                if (edge.Pass != _pass)
                {
                    signal.Detach(edge);
                    dictionary.Remove(signal);
                    Recycle(edge);
                }
                else
                {
                    if (!edge.Attached)
                        signal.Attach(edge);
                    changed |= edge.Revision != signal.Revision;
                }
            }
        }
        else if (_edges is DependencyEdge[] edges)
        {
            var kept = 0;
            for (var index = 0; index < _edgeCount; index++)
            {
                var edge = edges[index];
                var signal = edge.Signal!;
                if (edge.Pass != _pass)
                {
                    signal.Detach(edge);
                    Recycle(edge);
                }
                else
                {
                    edges[kept++] = edge;
                    if (!edge.Attached)
                        signal.Attach(edge);
                    changed |= edge.Revision != signal.Revision;
                }
            }
            Array.Clear(edges, kept, _edgeCount - kept);
            _edgeCount = kept;
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
        if (_edges is Dictionary<ISignal, DependencyEdge> dictionary)
        {
            foreach (var (signal, edge) in dictionary)
                if (!edge.Attached)
                {
                    dictionary.Remove(signal);
                    Recycle(edge);
                }
            return;
        }
        if (_edges is not DependencyEdge[] edges)
            return;
        var kept = 0;
        for (var index = 0; index < _edgeCount; index++)
        {
            var edge = edges[index];
            if (!edge.Attached)
            {
                Recycle(edge);
            }
            else
                edges[kept++] = edge;
        }
        Array.Clear(edges, kept, _edgeCount - kept);
        _edgeCount = kept;
    }

    private void Recycle(DependencyEdge edge)
    {
        edge.Signal = null;
        edge.Pass = edge.Revision = 0;
        if (_spareCount == MaxSpareEdges)
            return;
        edge.Next = _spareEdges;
        _spareEdges = edge;
        _spareCount++;
    }

    internal void Dispose()
    {
        if (_edges is Dictionary<ISignal, DependencyEdge> dictionary)
        {
            foreach (var edge in dictionary.Values)
            {
                edge.Signal!.Detach(edge);
                edge.Signal = null;
            }
        }
        else if (_edges is DependencyEdge[] edges)
        {
            for (var index = 0; index < _edgeCount; index++)
            {
                var edge = edges[index];
                edge.Signal!.Detach(edge);
                edge.Signal = null;
            }
        }
        _edges = null;
        _edgeCount = 0;
        _spareEdges = null;
        _spareCount = 0;
        Accepted = false;
    }

    internal readonly struct ReadScope(ReactiveConsumer? previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }
}
