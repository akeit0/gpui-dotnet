using System.Buffers.Binary;
using Gpui.Interop;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TableSampleRowContextMenuActionsUseItemIdentity(bool select)
    {
        var application = new GpuiApplication();
        var spec = TableView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<TableView>(spec), window);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var token = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ListOnContextMenuRequested).A;
        Assert.Equal(0, fixture.Complete(revision));
        // The displayed index deliberately differs from the stable service ID.
        Assert.Equal(0, fixture.Control(token, (ushort)ListEventKind.ContextMenuRequested,
            RowMenuPayload(4994, 6, 99), 2, 1));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        var click = SampleButtonClick(arena, select ? "select-service"u8 : "inspect-service"u8);
        var clickOp = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.OnClick && op.A == click);
        Assert.Equal(6UL, clickOp.B);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(0, fixture.Click(click, clickOp.B));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        Assert.DoesNotContain(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ContextMenuRowAnchor);
        var text = System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>(arena.Utf8, arena.Utf8Length));
        Assert.Contains(select ? "selected: svc-0005" : "Inspecting svc-0005", text);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void RowContextMenuRequestDecodesIdentityAndRendersOutsideTheRow()
    {
        var view = new RowContextMenuProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var bytes = RowMenuPayload(51, 1051, 99);
        Assert.Equal(0, fixture.Control(view.Token, (ushort)ListEventKind.ContextMenuRequested, bytes, 2, 7));
        bytes.AsSpan().Clear();
        Assert.Equal(new ListContextMenuEvent(51, 1051, 7, 99), view.Received);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
        Assert.Equal(99UL, Assert.Single(ops, op => op.Code == (ushort)OpCode.ContextMenuRowAnchor).A);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void RowContextMenuRejectsMalformedAnchorsAndPreservesOptionalRevision()
    {
        var view = new RowContextMenuProbe();
        using var fixture = new SessionFixture(view);
        fixture.Render();
        var kind = (ushort)ListEventKind.ContextMenuRequested;
        Assert.Equal(0, fixture.Control(view.Token, kind, RowMenuPayload(0, 1, 1)));
        Assert.Null(view.Received!.Value.ContentRevision);
        Assert.Equal(0, fixture.Control(view.Token, kind, RowMenuPayload(0, 1, 1), 2));
        Assert.Equal(0UL, view.Received!.Value.ContentRevision);
        Assert.Equal(-112, fixture.Control(view.Token, kind, RowMenuPayload(0, 0, 1)));
        Assert.Equal(-112, fixture.Control(view.Token, kind, RowMenuPayload(0, 1, 0)));
        Assert.Equal(-112, fixture.Control(view.Token, kind, RowMenuPayload(uint.MaxValue, 1, 1)));
        Assert.Equal(-112, fixture.Control(view.Token, kind, RowMenuPayload(0, 1, 1), 1));
        Assert.Equal(-112, fixture.Control(view.Token, kind, RowMenuPayload(0, 1, 1), 0, 1));
        Assert.Equal(-112, fixture.Control(view.Token, kind, new byte[16]));
        var reserved = RowMenuPayload(0, 1, 1);
        reserved[4] = 1;
        Assert.Equal(-112, fixture.Control(view.Token, kind, reserved));
    }

    private static byte[] RowMenuPayload(uint index, ulong itemId, ulong anchor)
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), itemId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), anchor);
        return bytes;
    }

    private sealed class RowContextMenuProbe : ProbeView
    {
        internal ulong Token;
        internal ListContextMenuEvent? Received;

        protected override Element Render(ref RenderContext ui)
        {
            Token = Runtime.Events.BindListContextMenu<RowContextMenuProbe>(static (view, value) =>
            {
                view.Received = value;
                view.Invalidate();
            });
            return Received is { } request
                ? ui.RowContextMenu("row-menu", request, ui.Text($"Service {request.ItemId}"))
                : ui.Div();
        }
    }
}
