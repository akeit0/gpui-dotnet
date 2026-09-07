using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void InputGalleryRendersFieldsWithAndWithoutHelpText()
    {
        var application = new GpuiApplication();
        var spec = InputGalleryView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<InputGalleryView>(spec), window);

        Assert.Equal(0, fixture.NativePublish(out var revision));
        Assert.Null(fixture.Session.Failure);
        Assert.Equal(0, fixture.Complete(revision));

        application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
        fixture.RenderFromNative();
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void InputGalleryCanToggleSliderColorsAndChangeTheme()
    {
        var application = new GpuiApplication();
        var spec = InputGalleryView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<InputGalleryView>(spec), window);

        for (var phase = 0; phase < 4; phase++)
        {
            Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
            var nodes = new ReadOnlySpan<NodeRecord>(arena.Nodes, arena.NodeLength);
            var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
            var button = -1;
            for (var index = 0; index < nodes.Length; index++)
            {
                ref readonly var node = ref nodes[index];
                if (node.Component == (ushort)ComponentId.Button &&
                    new ReadOnlySpan<byte>(arena.Utf8 + node.DataOffset, (int)node.DataLength)
                        .SequenceEqual("toggle-slider-style"u8))
                    button = index;
            }
            Assert.True(button >= 0);
            var click = Assert.Single(ops,
                op => op.Node == (uint)button && op.Code == (ushort)OpCode.OnClick).A;
            var colors = ops.Where(op => op.Code is
                >= (ushort)OpCode.SliderTrackRgba and <= (ushort)OpCode.SliderThumbBorderRgba).ToArray();
            if (phase % 2 == 0)
            {
                Assert.Equal(4, colors.Length);
                Assert.Equal(application.Theme.Colors.Success.Rgba,
                    Assert.Single(colors, op => op.Code == (ushort)OpCode.SliderFillRgba).A);
            }
            else Assert.Empty(colors);
            Assert.Equal(0, fixture.Complete(revision));
            Assert.Equal(0, fixture.Click(click));
            if (phase == 1)
                application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
        }
        Assert.Null(fixture.Session.Failure);
    }
}
