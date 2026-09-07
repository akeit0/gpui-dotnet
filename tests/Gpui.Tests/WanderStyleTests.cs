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
                ContrastAssert.OpaqueText(LastColor(ops, foregroundCode), LastColor(ops, backgroundCode),
                    $"{theme.Name}, {variant}, selected={selected}, {backgroundCode}");
            }
        }
    }

    private static Color LastColor(OpRecord[] ops, OpCode code) =>
        new((uint)ops.Last(op => op.Code == (ushort)code).A);
}
