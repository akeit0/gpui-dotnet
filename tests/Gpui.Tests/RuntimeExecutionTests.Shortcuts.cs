extern alias TaskBoard;

using Gpui.Interop;
using Board = TaskBoard::Gpui;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void ShortcutDispatchInvokesCommandAndValidatesEmptyPacket()
    {
        var view = new ShortcutProbe();
        using var fixture = new SessionFixture(view);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var op = Assert.Single(
            new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.OnShortcut
        );
        Assert.Equal((ulong)ShortcutKey.S | ((ulong)ShortcutModifiers.Primary << 16), op.B);
        Assert.Equal(0, fixture.Complete(revision));
        var kind = (ushort)ShortcutEventKind.Invoked;
        Assert.Equal(0, fixture.Control(op.A, kind, []));
        Assert.Equal(1, view.Count);
        Assert.Equal(-112, fixture.Control(op.A, kind, [1]));
        Assert.Equal(-112, fixture.Control(op.A, kind, [], 1));
        Assert.Equal(-112, fixture.Control(op.A, kind, [], 0, 1));
        Assert.Equal(1, view.Count);
        Assert.Null(fixture.Session.Failure);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(65537u, 0u)]
    [InlineData(1u, 64u)]
    [InlineData(1u, 33u)]
    [InlineData(1u, 40u)]
    [InlineData(1u, 0u)]
    [InlineData(1u, 4u)]
    [InlineData(1u, 2u)]
    [InlineData(42u, 0u)]
    [InlineData(76u, 0u)]
    public void ShortcutRejectsUnknownKeysAndAmbiguousPrimaryModifiers(uint key, uint modifiers)
    {
        Assert.Throws<ArgumentException>(() =>
            default(ShortcutOptions).Pack(
                new Shortcut((ShortcutKey)key, (ShortcutModifiers)modifiers)
            )
        );
    }

    [Fact]
    public void TaskBoardDeclaresPageAndDialogCommands()
    {
        var app = new GpuiApplication();
        var spec = Board.TaskBoardShellView.Spec();
        var window = app.OpenWindow(spec);
        using var fixture = new SessionFixture(
            null,
            app,
            new RootViewDeclaration<Board.TaskBoardShellView>(spec),
            window
        );
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
        var create = Assert.Single(
            ops,
            op => op.Code == (ushort)OpCode.OnShortcut && (op.B & 0xffff) == (ulong)ShortcutKey.N
        );
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(0, fixture.Control(create.A, (ushort)ShortcutEventKind.Invoked, []));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        var submit = Assert.Single(
            new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op =>
                op.Code == (ushort)OpCode.OnShortcut && (op.B & 0xffff) == (ulong)ShortcutKey.Enter
        );
        Assert.Equal((ulong)ShortcutModifiers.Primary, (submit.B >> 16) & 63);
        Assert.Equal((ushort)ComponentId.Overlay, arena.Nodes[submit.Node].Component);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Null(fixture.Session.Failure);
    }

    private sealed class ShortcutProbe : ProbeView
    {
        internal int Count;

        protected override Element Render(ref RenderContext ui) =>
            ui.Div()
                .OnShortcut(
                    this,
                    new(ShortcutKey.S, ShortcutModifiers.Primary),
                    static view => view.Count++
                );
    }
}
