using System.Runtime.ExceptionServices;
using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>A replacement-based value tracked by accepted View and row rendering.</summary>
public sealed class Signal<T>(T initialValue, IEqualityComparer<T>? comparer = null) : IReadOnlySignal<T>, ISignal
{
    private T _value = initialValue;
    private readonly IEqualityComparer<T> _comparer = comparer ?? EqualityComparer<T>.Default;
    private ApplicationExecution? _owner;
    private DependencyEdge? _subscribers;
    private ulong _revision;

    /// <summary>Reads the current value, tracking a dependency during rendering. Bound access is application-thread-only.</summary>
    public T Value
    {
        get
        {
            if (ReactiveConsumer.Comparing)
                throw new InvalidOperationException("Memo calculations and equality comparisons cannot read Signals. Supply their values as inputs.");
            var consumer = ReactiveConsumer.Current;
            if (consumer is not null && Volatile.Read(ref _owner) is null)
                Interlocked.CompareExchange(ref _owner, consumer.Execution, null);
            AssertAccess();
            consumer?.Read(this);
            return _value;
        }
        set => Set(value);
    }

    /// <summary>
    /// Replaces the value and invalidates accepted consumers if equality changes. Returns whether it changed.
    /// Notification failure is reported after visiting all consumers; the new value remains stored.
    /// </summary>
    public bool Set(T value)
    {
        AssertAccess();
        if (ApplicationExecution.Current?.Phase is ExecutionPhase.Render or ExecutionPhase.DemandRender
            || ReactiveConsumer.Comparing || ViewOwnership.Constructing)
            throw new InvalidOperationException("Signal mutation is not allowed during rendering or equality comparison.");
        bool equal;
        ReactiveConsumer.Comparing = true;
        try { equal = _comparer.Equals(_value, value); }
        finally { ReactiveConsumer.Comparing = false; }
        if (equal)
            return false;
        var mutation = _owner is not null && ApplicationExecution.Current is null
            ? _owner.Enter(ExecutionPhase.Event) : default;
        Exception? failure = null;
        try
        {
            _revision = checked(_revision + 1);
            _value = value;
            for (var edge = _subscribers; edge is not null; edge = edge.Next)
            {
                try
                {
                    edge.Consumer.Invalidate();
                }
                catch (Exception exception)
                {
                    // The value has changed for every subscriber, even when one window fails.
                    failure ??= exception;
                }
            }
        }
        finally
        {
            try
            {
                mutation.Dispose();
            }
            catch (Exception exception)
            {
                // Drain queued row work without replacing an earlier notification failure.
                failure ??= exception;
            }
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }

    private void AssertAccess()
    {
        var owner = Volatile.Read(ref _owner);
        if (owner is null)
            return;
        owner.AssertAccess();
        if ((ApplicationExecution.Current is { } current && !ReferenceEquals(owner, current))
            || (ReactiveConsumer.Current is { } consumer && !ReferenceEquals(owner, consumer.Execution)))
            throw new InvalidOperationException("A bound Signal cannot be used by another application.");
    }

    ulong ISignal.Revision => _revision;

    void ISignal.Attach(DependencyEdge edge)
    {
        edge.Next = _subscribers;
        if (_subscribers is not null)
            _subscribers.Previous = edge;
        _subscribers = edge;
        edge.Attached = true;
    }

    void ISignal.Detach(DependencyEdge edge)
    {
        if (!edge.Attached)
            return;
        if (edge.Previous is { } previous)
            previous.Next = edge.Next;
        else
            _subscribers = edge.Next;
        if (edge.Next is { } next)
            next.Previous = edge.Previous;
        edge.Previous = edge.Next = null;
        edge.Attached = false;
    }
}
