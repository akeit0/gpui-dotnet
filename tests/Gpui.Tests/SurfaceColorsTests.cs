using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe class SurfaceColorsTests
{
    private static readonly InteractionColors Palette = new(
        new(new(0x11223300), new(0x44556680)),
        new(new(0x77889940), new(0xAABBCCFF)),
        new(new(0xDDEEFF80), new(0x12345600))
    );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceAndLiteralSettersFollowDeclarationOrder(bool surfaceLast)
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = ui.Div();
        var literal = new Color(0xFEDCBA40);
        if (surfaceLast)
        {
            root = root.Background(literal).TextColor(literal).Surface(Palette.Normal);
        }
        else
        {
            root = root.Surface(Palette.Normal).Background(literal).TextColor(literal);
        }

        arena.Validate(root);
        Assert.Equal(
            surfaceLast ? Palette.Normal.Background : literal,
            LastColor(arena, OpCode.BackgroundRgba)
        );
        Assert.Equal(
            surfaceLast ? Palette.Normal.Foreground : literal,
            LastColor(arena, OpCode.TextRgba)
        );
    }

    [Fact]
    public void PaintPreservesAlphaAndOverridesEveryStateAtItsDeclarationPosition()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = ui.Button("paint", "label").Paint(default).Style(new PaintStyle(Palette));

        arena.Validate(root);
        AssertPalette(arena, Palette);
        Assert.Equal(12, arena.GetStats().Ops);
        for (var index = 0; index < arena.NativeArena->OpLength; index++)
        {
            var op = arena.NativeArena->Ops[index];
            Assert.Equal(ValueKind.U32, (ValueKind)op.ValueKind);
            Assert.Equal(0ul, op.B);
        }
    }

    [Fact]
    public void LaterOverridesAffectOnlyTheirOwnPropertyAndState()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var foreground = new Color(0x99887766);
        var hoverBackground = new Color(0x55443322);
        var root = ui.Button("paint", "label")
            .Paint(Palette)
            .TextColor(foreground)
            .HoverBackground(hoverBackground);

        arena.Validate(root);
        AssertPalette(
            arena,
            Palette with
            {
                Normal = Palette.Normal with { Foreground = foreground },
                Hover = Palette.Hover with { Background = hoverBackground },
            }
        );
    }

    [Fact]
    public void OmittingPaintInTheNextSnapshotRemovesAllOfItsDeclarations()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        arena.Validate(ui.Button("paint", "label").Paint(Palette));
        ui = arena.BeginRender();
        arena.Validate(ui.Button("paint", "label").Surface(Palette.Normal));

        Assert.Equal(2, arena.GetStats().Ops);
        Assert.Equal(Palette.Normal.Background, LastColor(arena, OpCode.BackgroundRgba));
        Assert.Equal(Palette.Normal.Foreground, LastColor(arena, OpCode.TextRgba));
    }

    private static void AssertPalette(RenderArenaOwner arena, InteractionColors colors)
    {
        Assert.Equal(colors.Normal.Background, LastColor(arena, OpCode.BackgroundRgba));
        Assert.Equal(colors.Normal.Foreground, LastColor(arena, OpCode.TextRgba));
        Assert.Equal(colors.Hover.Background, LastColor(arena, OpCode.HoverBackgroundRgba));
        Assert.Equal(colors.Hover.Foreground, LastColor(arena, OpCode.HoverTextRgba));
        Assert.Equal(colors.Pressed.Background, LastColor(arena, OpCode.ActiveBackgroundRgba));
        Assert.Equal(colors.Pressed.Foreground, LastColor(arena, OpCode.ActiveTextRgba));
    }

    private static Color LastColor(RenderArenaOwner arena, OpCode code)
    {
        for (var index = arena.NativeArena->OpLength - 1; index >= 0; index--)
        {
            var op = arena.NativeArena->Ops[index];
            if ((OpCode)op.Code == code)
            {
                return new Color((uint)op.A);
            }
        }
        throw new InvalidOperationException($"Missing operation {code}.");
    }

    private readonly record struct PaintStyle(InteractionColors Colors)
        : IGpuiElementStyle<ButtonTag>
    {
        public Element<ButtonTag> Apply(Element<ButtonTag> button) => button.Paint(Colors);
    }
}
