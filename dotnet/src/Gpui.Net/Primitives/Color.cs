using System.Runtime.CompilerServices;

namespace Gpui;

public readonly struct Color
{
    // Packed as 0xRRGGBBAA so the IR is explicit and platform-independent.
    public readonly uint Rgba;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Color(uint rgba) => Rgba = rgba;
}

public static class Colors
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Rgb(byte r, byte g, byte b) =>
        new(((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Rgba(byte r, byte g, byte b, byte a) =>
        new(((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a);
}
