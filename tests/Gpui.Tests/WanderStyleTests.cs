extern alias Wander;

using Gpui.Interop;
using Travel = Wander::Gpui;

namespace Gpui.Tests;

public sealed unsafe class WanderStyleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ButtonTextIsReadableInEveryVariantAndInteractionState(bool dark)
    {
        var theme = dark ? Travel.WanderThemes.Dark : Travel.WanderThemes.Light;
        using var arena = new RenderArenaOwner();
        foreach (var variant in Enum.GetValues<Travel.WanderButtonVariant>())
        foreach (var selected in new[] { false, true })
        {
            var ui = arena.BeginRender();
            arena.Validate(ui.Button("probe", "Text").Style(Travel.WanderStyles.Button(theme, variant, selected)));
            var ops = new ReadOnlySpan<OpRecord>(arena.NativeArena->Ops, arena.NativeArena->OpLength).ToArray();
            foreach (var (backgroundCode, foregroundCode) in new[]
            {
                (OpCode.BackgroundRgba, OpCode.TextRgba),
                (OpCode.HoverBackgroundRgba, OpCode.HoverTextRgba),
                (OpCode.ActiveBackgroundRgba, OpCode.ActiveTextRgba),
            })
            {
                AssertReadable(LastColor(ops, foregroundCode), LastColor(ops, backgroundCode),
                    $"{theme.Name}, {variant}, selected={selected}, {backgroundCode}");
            }
        }
    }

    private static Color LastColor(OpRecord[] ops, OpCode code) =>
        new((uint)ops.Last(op => op.Code == (ushort)code).A);

    internal static void AssertReadable(Color foreground, Color background, string context)
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
