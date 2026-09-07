namespace Gpui.Tests;

internal static class ContrastAssert
{
    internal static void OpaqueText(Color foreground, Color background, string context)
    {
        static double Luminance(Color color)
        {
            Assert.Equal(255u, color.Rgba & 255);
            static double Linear(uint value)
            {
                var channel = value / 255.0;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.Rgba >> 24)
                + 0.7152 * Linear((color.Rgba >> 16) & 255)
                + 0.0722 * Linear((color.Rgba >> 8) & 255);
        }
        var first = Luminance(foreground);
        var second = Luminance(background);
        var ratio = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        Assert.True(ratio >= 4.5, $"{context}: {foreground} on {background} has contrast {ratio:F2}.");
    }
}
