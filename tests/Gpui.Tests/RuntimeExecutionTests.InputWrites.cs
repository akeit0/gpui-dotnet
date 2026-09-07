using System.Buffers.Binary;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputWriteRequestsPreserveIdsAndPoliciesAcrossAnyThreadIngress(bool utf8)
    {
        using var fixture = new SessionFixture(new ProbeView());
        fixture.Render();
        var calls = fixture.CaptureResourceCommands();
        var controller = new InputController(fixture.View, "field");
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() =>
        {
            if (utf8)
                controller.SetValueIfCurrentWithResult("日本語"u8, 19, ulong.MaxValue >> 2,
                    InputSelectionPolicy.MoveToEnd, InputCompositionPolicy.CancelComposition);
            else
                controller.SetValueIfCurrentWithResult("日本語", 19, ulong.MaxValue >> 2,
                    InputSelectionPolicy.MoveToEnd, InputCompositionPolicy.CancelComposition);
        }));
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        var command = Assert.Single(calls).Command;
        Assert.Equal(ResourceCommandKind.InputSetValueIfCurrentWithResult, command.Command);
        Assert.Equal(19UL, command.A);
        Assert.Equal(ulong.MaxValue, command.B);
        Assert.Equal("日本語", command.Data);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetValueIfCurrentWithResult("", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetValueIfCurrentWithResult("", 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetValueIfCurrentWithResult("", 1, 1UL << 62));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetValueIfCurrentWithResult("", 1, 1, (InputSelectionPolicy)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetValueIfCurrentWithResult("", 1, 1, composition: (InputCompositionPolicy)2));
        Assert.Single(calls);
    }

    [Theory]
    [InlineData(InputWriteOutcome.Applied)]
    [InlineData(InputWriteOutcome.Unchanged)]
    [InlineData(InputWriteOutcome.Stale)]
    [InlineData(InputWriteOutcome.Composing)]
    public void InputWriteResultsUseTypedBindingsAndRetireWhenTheBindingIsRemoved(InputWriteOutcome outcome)
    {
        var view = new InputWriteProbe();
        using var fixture = new SessionFixture(view);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var token = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray()
            .Single(op => op.Code == (ushort)OpCode.InputOnWriteCompleted).A;
        Assert.Equal(0, fixture.Complete(revision));
        var bytes = WriteResultPayload(ulong.MaxValue >> 2, (uint)outcome);
        Assert.Equal(0, fixture.Control(token, (ushort)InputWriteEventKind.Completed, bytes, revision: ulong.MaxValue));
        Assert.Equal(new InputWriteResult(ulong.MaxValue >> 2, outcome, ulong.MaxValue), view.Result);
        Array.Fill(bytes, (byte)0xff);
        Assert.Equal(outcome, view.Result!.Value.Outcome);

        view.Observe = false;
        view.Invalidate();
        fixture.RenderFromNative();
        view.Result = null;
        Assert.Equal(0, fixture.Control(token, (ushort)InputWriteEventKind.Completed,
            WriteResultPayload(1, 0), revision: 1));
        Assert.Null(view.Result);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void InputWriteResultsRejectMalformedPacketsWithoutInvokingTheHandler()
    {
        var view = new InputWriteProbe();
        using var fixture = new SessionFixture(view);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var token = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray()
            .Single(op => op.Code == (ushort)OpCode.InputOnWriteCompleted).A;
        Assert.Equal(0, fixture.Complete(revision));
        foreach (var payload in new[] { WriteResultPayload(0, 0), WriteResultPayload(1UL << 62, 0),
            WriteResultPayload(1, 4), WriteResultPayload(1, 0)[..15] })
            Assert.Equal(-112, fixture.Control(token, (ushort)InputWriteEventKind.Completed, payload, revision: 1));
        var valid = WriteResultPayload(1, 0);
        Assert.Equal(-112, fixture.Control(token, (ushort)InputWriteEventKind.Completed, valid));
        Assert.Equal(-112, fixture.Control(token, (ushort)InputWriteEventKind.Completed, valid, flags: 1, revision: 1));
        valid[12] = 1;
        Assert.Equal(-112, fixture.Control(token, (ushort)InputWriteEventKind.Completed, valid, revision: 1));
        Assert.Null(view.Result);
        Assert.Null(fixture.Session.Failure);
    }

    private static byte[] WriteResultPayload(ulong request, uint outcome)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, request);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), outcome);
        return bytes;
    }

    private sealed class InputWriteProbe : ProbeView
    {
        internal InputWriteResult? Result;
        internal bool Observe = true;
        protected override Element Render(ref RenderContext ui)
        {
            var input = ui.Input("field", new InputOptions());
            return Observe ? input.OnWriteCompleted(this, static (view, result) => view.Result = result) : input;
        }
    }
}
