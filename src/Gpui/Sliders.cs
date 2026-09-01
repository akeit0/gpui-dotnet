using System.Diagnostics;
using System.Text;

namespace Gpui;

/// <summary>Orientation of a native slider.</summary>
public enum SliderAxis : uint
{
    Horizontal = 0,
    Vertical = 1,
}

/// <summary>Mapping used between the slider position and its numeric value.</summary>
public enum SliderScale : uint
{
    Linear = 0,
    Logarithmic = 1,
}

/// <summary>Kind of event emitted by a native slider.</summary>
public enum SliderEventKind : ushort
{
    Changed = 1,
    Released = 2,
}

/// <summary>A single slider value or an ordered range of two values.</summary>
public readonly struct SliderValue : IEquatable<SliderValue>
{
    private readonly float _start;
    private readonly float _end;
    private readonly bool _isRange;

    public SliderValue(float value)
    {
        ValidateFinite(value, nameof(value));
        _start = 0;
        _end = value;
        _isRange = false;
    }

    public SliderValue(float start, float end)
    {
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        if (start > end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                "Range start must not exceed end."
            );
        }

        _start = start;
        _end = end;
        _isRange = true;
    }

    public static SliderValue Single(float value) => new(value);

    public static SliderValue Range(float start, float end) => new(start, end);

    public bool IsRange => _isRange;
    public bool IsSingle => !_isRange;
    public float Start => _isRange ? _start : 0;
    public float End => _end;

    public bool Equals(SliderValue other) =>
        _isRange == other._isRange && _start == other._start && _end == other._end;

    public override bool Equals(object? obj) => obj is SliderValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_start, _end, _isRange);

    public static bool operator ==(SliderValue left, SliderValue right) => left.Equals(right);

    public static bool operator !=(SliderValue left, SliderValue right) => !left.Equals(right);

    public override string ToString() => _isRange ? $"{Start}..{End}" : End.ToString();

    private static void ValidateFinite(float value, string paramName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, "Slider values must be finite.");
        }
    }
}

/// <summary>Initial and behavioral configuration for a retained native slider.</summary>
public readonly struct SliderOptions
{
    private readonly bool _initialized;

    public SliderOptions(
        float min = 0,
        float max = 100,
        float step = 1,
        float value = 0,
        SliderAxis axis = SliderAxis.Horizontal,
        bool disabled = false,
        SliderScale scale = SliderScale.Linear
    )
        : this(min, max, step, new SliderValue(value), axis, disabled, scale) { }

    public SliderOptions(
        float min,
        float max,
        float step,
        SliderValue value,
        SliderAxis axis = SliderAxis.Horizontal,
        bool disabled = false,
        SliderScale scale = SliderScale.Linear
    )
    {
        ValidateRange(min, max, step, axis, scale);
        Min = min;
        Max = max;
        Step = step;
        InitialValue = value;
        Axis = axis;
        Disabled = disabled;
        Scale = scale;
        _initialized = true;
    }

    public float Min { get; }
    public float Max { get; }
    public float Step { get; }
    public SliderValue InitialValue { get; }
    public SliderAxis Axis { get; }
    public bool Disabled { get; }
    public SliderScale Scale { get; }

    internal bool HasInitialValue => _initialized;
    internal float EffectiveMin => _initialized ? Min : 0;
    internal float EffectiveMax => _initialized ? Max : 100;
    internal float EffectiveStep => _initialized ? Step : 1;
    internal SliderValue EffectiveInitialValue => _initialized ? InitialValue : default;
    internal SliderAxis EffectiveAxis => _initialized ? Axis : SliderAxis.Horizontal;
    internal bool EffectiveDisabled => _initialized && Disabled;
    internal SliderScale EffectiveScale => _initialized ? Scale : SliderScale.Linear;

    private static void ValidateRange(
        float min,
        float max,
        float step,
        SliderAxis axis,
        SliderScale scale
    )
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || min >= max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(min),
                "Slider min must be finite and less than max."
            );
        }
        if (!float.IsFinite(step) || step <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                "Slider step must be finite and positive."
            );
        }
        if ((uint)axis > (uint)SliderAxis.Vertical)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }
        if ((uint)scale > (uint)SliderScale.Logarithmic)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        if (scale == SliderScale.Logarithmic && min <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(min),
                "Logarithmic sliders require min > 0."
            );
        }
    }
}

/// <summary>
/// Snapshot of a slider interaction. Changed is raised continuously during pointer movement;
/// Released is raised once after the active pointer interaction ends.
/// </summary>
public readonly struct SliderEvent
{
    private readonly float _start;
    private readonly float _end;
    private readonly bool _isRange;

    internal SliderEvent(SliderEventKind kind, float start, float end, bool isRange, ulong revision)
    {
        Kind = kind;
        _start = start;
        _end = end;
        _isRange = isRange;
        Revision = revision;
    }

    public SliderEventKind Kind { get; }
    public bool IsRange => _isRange;
    public float Start => _isRange ? _start : 0;
    public float End => _end;
    public SliderValue Value => _isRange ? new SliderValue(_start, _end) : new SliderValue(_end);
    public ulong Revision { get; }
}

/// <summary>Imperative handle for a retained native slider declared by the same View.</summary>
[DebuggerDisplay("{DebuggerView,nq}")]
public readonly struct SliderController
{
    private readonly ViewBase? _owner;
    private readonly byte[]? _utf8Key;

    internal SliderController(ViewBase owner, string key)
    {
        _owner = owner;
        _utf8Key = Encoding.UTF8.GetBytes(key);
    }

    internal SliderController(ViewBase owner, ReadOnlySpan<byte> utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key.ToArray();
    }

    internal SliderController(ViewBase owner, byte[] utf8Key)
    {
        _owner = owner;
        _utf8Key = utf8Key;
    }

    public bool IsBound => _utf8Key is not null;
    internal ReadOnlySpan<byte> Utf8KeySpan => _utf8Key;
    public bool IsDefault => _owner is null;

    public void SetValue(float value) => SetValue(new SliderValue(value));

    public void SetValue(SliderValue value)
    {
        var start = BitConverter.SingleToUInt32Bits(value.Start);
        var end = BitConverter.SingleToUInt32Bits(value.End);
        Owner.DispatchResourceCommand(
            new ResourceCommand(
                ResourceKind.Slider,
                ResourceCommandKind.SliderSetValue,
                null,
                start | ((ulong)end << 32),
                value.IsRange ? 1UL : 0UL,
                Utf8Key: Utf8KeyArray
            )
        );
    }

    private string DebuggerView =>
        _utf8Key is null ? "unbound"
        : ResourceKeys.TryDecodeAutoKey(_utf8Key, out var id) ? $"auto:{id}"
        : "explicit";

    private ViewBase Owner =>
        _owner ?? throw new InvalidOperationException("Default SliderController cannot be used.");

    private byte[] Utf8KeyArray =>
        _utf8Key ?? throw new InvalidOperationException("Default SliderController cannot be used.");
}
