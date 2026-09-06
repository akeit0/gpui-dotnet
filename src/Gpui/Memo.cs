using Gpui.Interop.Internal;

namespace Gpui;

/// <summary>A View-owned, single-entry cache of pure data. Read Signals before calling Get.</summary>
public sealed class Memo<TInput, TResult> : IOwnedCache where TInput : IEquatable<TInput>
{
    private readonly ViewOwnership _owner;
    private TInput _input = default!;
    private TResult _result = default!;
    private bool _hasValue;
    private bool _calculating;

    internal Memo(ViewOwnership owner) => _owner = owner;

    public TResult Get(TInput input, Func<TInput, TResult> calculate)
    {
        _owner.AssertAccess();
        ArgumentNullException.ThrowIfNull(calculate);
        if (_calculating) throw new InvalidOperationException("A memo cannot recursively evaluate itself.");
        var previous = ReactiveConsumer.Comparing;
        ReactiveConsumer.Comparing = true;
        _calculating = true;
        try
        {
            if (_hasValue && EqualityComparer<TInput>.Default.Equals(_input, input)) return _result;
            var result = calculate(input);
            _input = input;
            _result = result;
            _hasValue = true;
            return result;
        }
        finally { _calculating = false; ReactiveConsumer.Comparing = previous; }
    }

    void IOwnedCache.Clear()
    {
        _input = default!;
        _result = default!;
        _hasValue = false;
    }
}
