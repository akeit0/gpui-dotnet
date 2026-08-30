using static Gpui.Colors;
using static Gpui.Units;

namespace Gpui.Net.Tests;

public sealed class SemanticRenderTests
{
    [Fact]
    public void RepresentativeTreePassesManagedValidation()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var root = ui.VStack(
                ui.Text("hello"u8),
                ui.Button("save", "Save"u8).Padding(Px(8)).Background(Rgb(37, 99, 235)),
                ui.Image("asset.svg"u8).Fit(ImageFit.Cover).Grayscale()
            )
            .Gap(Px(6));

        arena.Validate(root);

        var stats = arena.GetStats();
        Assert.Equal(5, stats.Nodes);
        Assert.Equal(4, stats.Children);
        Assert.True(stats.Ops >= 5);
    }

    [Fact]
    public void ElementCannotEscapeItsRenderGeneration()
    {
        using var arena = new RenderArenaOwner();
        var first = arena.BeginRender();
        var stale = first.Div();
        var second = arena.BeginRender();
        var root = second.Div();

        Assert.Throws<InvalidOperationException>(() => root.Child(stale));
    }

    [Fact]
    public void InvalidImageFitIsRejectedAtTheManagedBoundary()
    {
        using var arena = new RenderArenaOwner();
        var ui = arena.BeginRender();
        var image = ui.Image("asset.svg"u8);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            image.Fit((ImageFit)99)
        );
    }
}
