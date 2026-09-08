using System.Text;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void FocusTargetReusesIdentityAndRoutesCommandsThroughItsOwner()
    {
        var view = new FocusProbe();
        using var fixture = new SessionFixture(view);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var target = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.FocusTarget);
        var key = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(arena.Utf8 + target.A, (int)target.B));
        Assert.Equal((ushort)ComponentId.Div, arena.Nodes[target.Node].Component);
        Assert.Equal(1, arena.NodeLength); // Declaring focus adds no wrapper.
        Assert.Equal(0, fixture.Complete(revision));
        var calls = fixture.CaptureResourceCommands();
        Exception? failure = null;
        var worker = new Thread(() => failure = Record.Exception(view.Target.Focus));
        worker.Start();
        worker.Join();
        Assert.Null(failure);
        var call = Assert.Single(calls);
        Assert.Equal(view.Runtime.RuntimeViewHandle, call.Owner);
        Assert.Equal(new ResourceCommand(ResourceKind.Focus, ResourceCommandKind.FocusTargetFocus,
            key, 0, 0, ""), call.Command);

        view.TabStop = true;
        view.Invalidate();
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
        target = Assert.Single(ops, op => op.Code == (ushort)OpCode.FocusTarget);
        Assert.Equal(key, Encoding.UTF8.GetString(new ReadOnlySpan<byte>(arena.Utf8 + target.A, (int)target.B)));
        Assert.Equal(1UL, Assert.Single(ops, op => op.Code == (ushort)OpCode.FocusTabStop).A);
        Assert.Equal(0, fixture.Complete(revision));
        view.Target.Blur();
        Assert.Equal(ResourceCommandKind.FocusTargetBlur, calls[1].Command.Command);
        fixture.Session.Stop();
        Assert.Throws<InvalidOperationException>(view.Target.Focus);
        Assert.Throws<InvalidOperationException>(default(FocusController).Focus);
    }

    [Fact]
    public void FocusTargetCannotBeDeclaredByAnotherView()
    {
        var first = new FocusProbe();
        using var fixture = new SessionFixture(first);
        fixture.Render();
        var second = new FocusProbe { Target = first.Target };
        using var other = new SessionFixture(second);
        Assert.NotEqual(0, other.NativePublish(out _, out _));
        Assert.NotNull(other.Session.Failure);
    }

    private sealed class FocusProbe : ProbeView
    {
        internal FocusController Target;
        internal bool TabStop;
        protected override Element Render(ref RenderContext ui) => ui.FocusTarget(ref Target, ui.Div(), TabStop);
    }
}
