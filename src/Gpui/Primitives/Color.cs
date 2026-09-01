using System.Runtime.CompilerServices;

namespace Gpui;

public readonly struct Color : IEquatable<Color>
{
    // Packed as 0xRRGGBBAA so the IR is explicit and platform-independent.
    public readonly uint Rgba;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color(uint rgba) => Rgba = rgba;

    public bool Equals(Color other) => Rgba == other.Rgba;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => Rgba.GetHashCode();

    public override string ToString() => $"#{Rgba:X8}";

    public Color WithAlpha(byte alpha) => new((Rgba & 0xFFFFFF00u) | alpha);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}

public static class Colors
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Rgb(byte r, byte g, byte b) =>
        new(((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Rgba(byte r, byte g, byte b, byte a) =>
        new(((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a);

    /// <summary>
    /// Parses a CSS-style hexadecimal color. Supports <c>#RGB</c>, <c>#RGBA</c>,
    /// <c>#RRGGBB</c>, <c>#RRGGBBAA</c>, and the literal <c>transparent</c>.
    /// </summary>
    public static Color Hex(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var text = value.Trim();
        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return Rgba(0, 0, 0, 0);
        }

        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length is not (3 or 4 or 6 or 8))
        {
            throw new FormatException(
                $"Color '{value}' must use #RGB, #RGBA, #RRGGBB, or #RRGGBBAA format."
            );
        }

        Span<byte> channels = stackalloc byte[4];
        var channelCount = text.Length <= 4 ? text.Length : text.Length / 2;
        for (var index = 0; index < channelCount; index++)
        {
            var offset = text.Length <= 4 ? index : index * 2;
            var high = ParseHexDigit(text[offset]);
            var low = text.Length <= 4 ? high : ParseHexDigit(text[offset + 1]);
            channels[index] = (byte)((high << 4) | low);
        }

        if (channelCount is 3)
        {
            channels[3] = byte.MaxValue;
        }

        return Rgba(channels[0], channels[1], channels[2], channels[3]);
    }

    private static byte ParseHexDigit(char value)
    {
        var digit = value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1,
        };
        if (digit < 0)
        {
            throw new FormatException($"'{value}' is not a hexadecimal color digit.");
        }
        return (byte)digit;
    }
}
