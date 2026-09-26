using Gpui.Interop.Internal;

namespace Gpui;

internal interface IViewEffect
{
    void Commit();
    void StopChanged();
    void Start();
    void CodeChanged();
    void Revoke();
    void Retire();
}

/// <summary>An explicitly identified external relationship, activated only after native acceptance.</summary>
public sealed class Effect<TInput> : IViewEffect
    where TInput : IEquatable<TInput>
{
    private readonly ViewOwnership _owner;
    private Action<EffectScope, TInput>? _setup;
    private TInput _staged = default!;
    private TInput _accepted = default!;
    private ulong _pass;
    private EffectScope? _scope;
    private bool _replace;
    private bool _start;
    private bool _codeChanged;

    internal Effect(ViewOwnership owner, Action<EffectScope, TInput> setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _owner = owner;
        _setup = setup;
    }

    internal void Declare(ViewBase owner, TInput input)
    {
        _owner.AssertAccess();
        if (!ReferenceEquals(_owner.View, owner) || _owner.Pass == 0)
            throw new InvalidOperationException(
                "Declare an effect only in its owning View's render."
            );
        if (_pass == _owner.Pass)
            throw new InvalidOperationException("An effect may be declared only once per render.");
        _pass = _owner.Pass;
        _staged = input;
    }

    void IViewEffect.Commit()
    {
        var declared = _pass == _owner.Pass;
        var previous = ReactiveConsumer.Comparing;
        ReactiveConsumer.Comparing = true;
        try
        {
            _replace =
                !declared
                || _scope is null
                || _codeChanged
                || !EqualityComparer<TInput>.Default.Equals(_accepted, _staged);
        }
        finally
        {
            ReactiveConsumer.Comparing = previous;
        }
        _start = declared && _replace;
        _accepted = declared ? _staged : default!;
        _staged = default!;
        _codeChanged = false;
    }

    void IViewEffect.StopChanged()
    {
        if (!_replace)
            return;
        var old = _scope;
        _scope = null;
        old?.Retire();
    }

    void IViewEffect.Start()
    {
        if (!_start)
            return;
        _start = false;
        _replace = false;
        // Publish ownership before user setup, so partial initialization is always cleaned up.
        _scope = new EffectScope(_owner.View.Runtime);
        _setup!(_scope, _accepted);
    }

    void IViewEffect.CodeChanged() => _codeChanged = true;

    void IViewEffect.Revoke() => _scope?.Revoke();

    void IViewEffect.Retire()
    {
        _setup = null;
        _staged = _accepted = default!;
        _start = false;
        var scope = _scope;
        _scope = null;
        scope?.Retire();
    }
}
