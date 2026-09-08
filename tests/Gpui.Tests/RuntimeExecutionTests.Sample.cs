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
        using var fixture = new SessionFixture(
            null,
            application,
            new RootViewDeclaration<InputGalleryView>(spec),
            window
        );

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
        using var fixture = new SessionFixture(
            null,
            application,
            new RootViewDeclaration<InputGalleryView>(spec),
            window
        );

        for (var phase = 0; phase < 4; phase++)
        {
            Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
            var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
            var click = SampleButtonClick(arena, "toggle-slider-style"u8);
            var colors = ops.Where(op =>
                    op.Code
                        is (ushort)OpCode.SliderTrackRgba
                            or (ushort)OpCode.SliderFillRgba
                            or (ushort)OpCode.SliderThumbRgba
                            or (ushort)OpCode.SliderThumbBorderRgba
                )
                .ToArray();
            if (phase % 2 == 0)
            {
                Assert.Equal(4, colors.Length);
                Assert.Equal(
                    application.Theme.Colors.Success.Rgba,
                    Assert.Single(colors, op => op.Code == (ushort)OpCode.SliderFillRgba).A
                );
            }
            else
                Assert.Empty(colors);
            Assert.Equal(0, fixture.Complete(revision));
            Assert.Equal(0, fixture.Click(click));
            if (phase == 1)
                application.SetTheme(GpuiTheme.CreateDefault(GpuiThemeAppearance.Dark));
        }
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void TableSampleHeaderStyleSurvivesSortAndThemeChanges()
    {
        var application = new GpuiApplication();
        var spec = TableView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(
            null,
            application,
            new RootViewDeclaration<TableView>(spec),
            window
        );

        for (var phase = 0; phase < 3; phase++)
        {
            application.SetTheme(
                GpuiTheme.CreateDefault(
                    phase == 1 ? GpuiThemeAppearance.Dark : GpuiThemeAppearance.Light
                )
            );
            Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
            var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
            var click = SampleButtonClick(arena, "sort-service"u8);
            Assert.Equal(
                application.Theme.Colors.InfoBackground.Rgba,
                Assert.Single(ops, op => op.Code == (ushort)OpCode.TableHeaderBackgroundRgba).A
            );
            Assert.Equal(
                application.Theme.Colors.Info.Rgba,
                Assert.Single(ops, op => op.Code == (ushort)OpCode.TableHeaderTextRgba).A
            );
            Assert.Equal(
                application.Theme.Colors.BorderFocused.Rgba,
                Assert.Single(ops, op => op.Code == (ushort)OpCode.TableHeaderBorderRgba).A
            );
            Assert.Equal(
                (ulong)phase + 1,
                Assert.Single(ops, op => op.Code == (ushort)OpCode.ListContentRevision).A
            );
            Assert.Equal(0, fixture.Complete(revision));
            Assert.Equal(0, fixture.Click(click));
        }
        Assert.Null(fixture.Session.Failure);
    }

    private static ulong SampleButtonClick(RenderArena arena, ReadOnlySpan<byte> key)
    {
        var nodes = new ReadOnlySpan<NodeRecord>(arena.Nodes, arena.NodeLength);
        var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength);
        for (var index = 0; index < nodes.Length; index++)
        {
            ref readonly var node = ref nodes[index];
            if (
                node.Component != (ushort)ComponentId.Button
                || !new ReadOnlySpan<byte>(
                    arena.Utf8 + node.DataOffset,
                    (int)node.DataLength
                ).SequenceEqual(key)
            )
                continue;
            foreach (ref readonly var op in ops)
                if (op.Node == (uint)index && op.Code == (ushort)OpCode.OnClick)
                    return op.A;
        }
        throw new InvalidOperationException("Sample button has no click binding.");
    }
}
