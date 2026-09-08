using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>Owns one accepted effect generation's registrations, queued callbacks, and work.</summary>
public sealed class EffectScope
{
    private readonly object _gate = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ViewRuntime? _runtime;
    private List<IDisposable>? _cleanup;
    private HashSet<Callback>? _callbacks;
    private CancellationTokenSource? _cancellation;
    private CancellationToken _lifetime;
    private WorkScope? _work;
    private bool _retired;

    internal EffectScope(ViewRuntime runtime) => _runtime = runtime;

    public CancellationToken Lifetime
    {
        get
        {
            lock (_gate)
                return _lifetime.CanBeCanceled
                    ? _lifetime
                    : _lifetime = _runtime is null ? new(true) : (_cancellation = new()).Token;
        }
    }

    public WorkScope Work
    {
        get
        {
            AssertAccess();
            return _work ??= new WorkScope(_runtime!.RequireActiveRoute(), Lifetime, _thread);
        }
    }

    public T Own<T>(T resource)
        where T : IDisposable
    {
        AssertAccess();
        ArgumentNullException.ThrowIfNull(resource);
        (_cleanup ??= []).Add(resource);
        return resource;
    }

    /// <summary>Creates a repeatable callback whose target is released and delivery revoked on replacement.</summary>
    public Action Bind<TState>(TState state, Action<TState> callback)
    {
        AssertAccess();
        ArgumentNullException.ThrowIfNull(callback);
        return Own(new Registration<TState>(this, state, callback)).Invoke;
    }

    private sealed class Registration<TState>(
        EffectScope scope,
        TState state,
        Action<TState> callback
    ) : IDisposable
    {
        private TState _state = state;
        private Action<TState>? _callback = callback;

        internal void Invoke()
        {
            lock (scope._gate)
                if (_callback is { } action)
                    scope.Post(_state, action);
        }

        public void Dispose()
        {
            lock (scope._gate)
            {
                _state = default!;
                _callback = null;
            }
        }
    }

    /// <summary>Queues a synchronous callback; replacement releases its state and prevents delivery.</summary>
    public void Post<TState>(TState state, Action<TState> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ApplicationExecution.AssertEffectsAllowed();
        lock (_gate)
        {
            if (_runtime is null)
                return;
            var work = new Callback<TState>(this, state, callback);
            (_callbacks ??= []).Add(work);
            try
            {
                if (!_runtime.TryPostOwned(work))
                {
                    _callbacks?.Remove(work);
                    work.Clear();
                }
            }
            catch
            {
                _callbacks?.Remove(work);
                work.Clear();
                throw;
            }
        }
    }

    private void AssertAccess()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(
                "Effect facilities require the application thread."
            );
        ObjectDisposedException.ThrowIf(_runtime is null, this);
    }

    internal void Revoke()
    {
        lock (_gate)
        {
            if (_runtime is null)
                return;
            _runtime = null;
            if (_callbacks is not null)
                foreach (var callback in _callbacks)
                    callback.Clear();
            _callbacks = null;
        }
        _work?.Revoke();
    }

    internal void Retire()
    {
        if (_retired)
            return;
        _retired = true;
        Revoke();
        List<Exception>? failures = null;
        try
        {
            _work?.Retire();
        }
        catch (Exception e)
        {
            (failures ??= []).Add(e);
        }
        _work = null;
        try
        {
            _cancellation?.Cancel();
        }
        catch (Exception e)
        {
            (failures ??= []).Add(e);
        }
        if (_cleanup is not null)
            for (var i = _cleanup.Count - 1; i >= 0; i--)
                try
                {
                    _cleanup[i].Dispose();
                }
                catch (Exception e)
                {
                    (failures ??= []).Add(e);
                }
        _cleanup = null;
        _cancellation?.Dispose();
        if (failures is not null)
            throw new AggregateException(failures);
    }

    private abstract class Callback : IIngressWork
    {
        internal abstract void Clear();
        public abstract void Invoke();
    }

    private sealed class Callback<TState>(EffectScope scope, TState state, Action<TState> callback)
        : Callback
    {
        private TState _state = state;
        private Action<TState>? _callback = callback;

        internal override void Clear()
        {
            _state = default!;
            _callback = null;
        }

        public override void Invoke()
        {
            Action<TState>? action;
            TState value;
            lock (scope._gate)
            {
                scope._callbacks?.Remove(this);
                action = _callback;
                value = _state;
                Clear();
            }
            action?.Invoke(value);
        }
    }
}
