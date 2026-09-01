using System.Runtime.CompilerServices;

namespace Gpui;

public readonly struct Pixels
{
    public readonly float Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pixels(float value) => Value = value;
}

public readonly struct Percent
{
    public readonly float Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Percent(float value) => Value = value;
}

public enum LengthUnit : byte
{
    Pixels = 1,
    Percent = 2,
}

public readonly struct Length
{
    public readonly float Value;
    public readonly LengthUnit Unit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Length(float value, LengthUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Length(Pixels value) => new(value.Value, LengthUnit.Pixels);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Length(Percent value) => new(value.Value, LengthUnit.Percent);
}

public static class Units
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Pixels Px(float value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Percent Percent(float value) => new(value);
}
