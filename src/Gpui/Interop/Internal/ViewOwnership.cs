namespace Gpui.Interop.Internal;

internal interface IOwnedCache
{
    void Clear();
}

/// <summary>Storage whose lifetime begins before application construction.</summary>
internal sealed class ViewOwnership
{
    internal GpuiWindow? Window;

    [ThreadStatic]
    internal static bool Constructing;
    internal ViewBase View { get; private set; } = null!;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly ApplicationExecution? _execution = ApplicationExecution.Current;
    private List<IDisposable>? _cleanup;
    private List<IOwnedCache>? _caches;
    internal List<IViewEffect>? Effects;
    internal ulong Pass;
    internal bool Retired { get; private set; }
    internal bool ConstructionComplete { get; set; }

    internal void Bind(ViewBase view)
    {
        AssertAccess();
        if (View is not null || ConstructionComplete)
            throw new InvalidOperationException(
                "A construction context belongs to exactly one new View."
            );
        View = view;
    }

    internal void AssertAccess()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(
                "Owned View facilities require the application thread."
            );
        if (
            _execution is not null
            && ApplicationExecution.Current is { } current
            && !ReferenceEquals(_execution, current)
        )
            throw new InvalidOperationException(
                "An owned facility cannot be used by another application."
            );
        ObjectDisposedException.ThrowIf(Retired, this);
    }

    internal T Own<T>(T resource)
        where T : IDisposable
    {
        AssertAccess();
        ArgumentNullException.ThrowIfNull(resource);
        (_cleanup ??= []).Add(resource);
        return resource;
    }

    internal Memo<TInput, TResult> Memo<TInput, TResult>()
        where TInput : IEquatable<TInput>
    {
        AssertAccess();
        var memo = new Memo<TInput, TResult>(this);
        (_caches ??= []).Add(memo);
        return memo;
    }

    internal Effect<TInput> Effect<TInput>(Action<EffectScope, TInput> setup)
        where TInput : IEquatable<TInput>
    {
        AssertAccess();
        var effect = new Effect<TInput>(this, setup);
        (Effects ??= []).Add(effect);
        return effect;
    }

    internal void ClearCaches()
    {
        if (_caches is not null)
            foreach (var cache in _caches)
                cache.Clear();
        if (Effects is not null)
            foreach (var effect in Effects)
                effect.CodeChanged();
    }

    internal void Retire()
    {
        if (Retired)
            return;
        Retired = true;
        Window = null;
        List<Exception>? failures = null;
        if (Effects is not null)
            for (var i = Effects.Count - 1; i >= 0; i--)
                try
                {
                    Effects[i].Retire();
                }
                catch (Exception e)
                {
                    (failures ??= []).Add(e);
                }
        Effects = null;
        if (_caches is not null)
            foreach (var cache in _caches)
                cache.Clear();
        _caches = null;
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
        if (failures is not null)
            throw new AggregateException(failures);
    }

    internal void RevokeEffects()
    {
        if (Effects is not null)
            foreach (var effect in Effects)
                effect.Revoke();
    }
}
