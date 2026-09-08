extern alias TaskBoard;

using Gpui.Interop;
using Board = TaskBoard::Gpui;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void RowTooltipRequestDecodesIdentityAndPlacementOutsideTheRow()
    {
        var view = new RowTooltipProbe();
        using var fixture = new SessionFixture(view);
        PublishTooltipProbe(fixture, view);
        var bytes = RowMenuPayload(51, 1051, 99);
        Assert.Equal(0, fixture.Control(view.Token, (ushort)ListEventKind.TooltipRequested, bytes, 2, 7));
        bytes.AsSpan().Clear();
        Assert.Equal(new ListTooltipEvent(51, 1051, 7, 99), view.Received);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var ops = new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray();
        Assert.Equal(99UL, Assert.Single(ops, op => op.Code == (ushort)OpCode.TooltipRowAnchor).A);
        Assert.Equal((ulong)TooltipPlacement.Right,
            Assert.Single(ops, op => op.Code == (ushort)OpCode.TooltipPlacement).A);
        Assert.Equal(700UL, Assert.Single(ops, op => op.Code == (ushort)OpCode.TooltipShowDelayMs).A);
        Assert.Equal(650UL, Assert.Single(ops, op => op.Code == (ushort)OpCode.TooltipHideDelayMs).A);
        Assert.Equal(14f, BitConverter.UInt32BitsToSingle((uint)Assert.Single(ops,
            op => op.Code == (ushort)OpCode.TooltipGapPx).A));
        Assert.Equal(12f, BitConverter.UInt32BitsToSingle((uint)Assert.Single(ops,
            op => op.Code == (ushort)OpCode.TooltipMarginPx).A));
        var collection = Assert.Single(ops, op => op.Code == (ushort)OpCode.ListOnTooltipRequested).Node;
        Assert.All(ops.Where(op => op.Code >= (ushort)OpCode.TooltipPlacement
            && op.Code <= (ushort)OpCode.TooltipMarginPx), op => Assert.Equal(collection, op.Node));
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void RowTooltipValidatesPacketAndPreservesOptionalRevision()
    {
        var view = new RowTooltipProbe();
        using var fixture = new SessionFixture(view);
        PublishTooltipProbe(fixture, view);
        var kind = (ushort)ListEventKind.TooltipRequested;
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

    [Fact]
    public void TaskBoardTooltipUsesTaskIdentityAndPreservesSelection()
    {
        var application = new GpuiApplication();
        var spec = Board.TaskBoardShellView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<Board.TaskBoardShellView>(spec), window);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var token = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ListOnTooltipRequested).A;
        Assert.Equal(0, fixture.Complete(revision));
        var view = fixture.Session.RootView;
        var selectedId = PrivateSampleField<long>(view, "_selectedId");
        var store = PrivateSampleField<Board.TaskStore>(view, "_store");
        var target = store.Tasks[1];
        Assert.Equal(0, fixture.Control(token, (ushort)ListEventKind.TooltipRequested,
            RowMenuPayload(0, (ulong)target.Id, 99), 2, 1));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        Assert.Equal(99UL, Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.TooltipRowAnchor).A);
        var text = System.Text.Encoding.UTF8.GetString(arena.Utf8, arena.Utf8Length);
        Assert.Contains(target.Title, text);
        Assert.Equal(selectedId, PrivateSampleField<long>(view, "_selectedId"));
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Null(fixture.Session.Failure);
    }

    private static void PublishTooltipProbe(SessionFixture fixture, RowTooltipProbe view)
    {
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        view.Token = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ListOnTooltipRequested).A;
        Assert.Equal(0, fixture.Complete(revision));
    }

    private sealed class RowTooltipProbe : ProbeView
    {
        internal ulong Token;
        internal ListTooltipEvent? Received;
        private ListController _list;

        protected override Element Render(ref RenderContext ui)
        {
            var list = ui.List(ref _list, new ListDataSource(100, 7), BindListRenderer(1))
                .OnTooltipRequested(this, static (view, value) =>
                {
                    view.Received = value;
                    view.Invalidate();
                }, new TooltipOptions(TooltipPlacement.Right, TooltipAlignment.End,
                    showDelay: TimeSpan.FromMilliseconds(700), hideDelay: TimeSpan.FromMilliseconds(650),
                    gap: 14, margin: 12));
            return Received is { } request
                ? ui.VStack(list, ui.RowTooltip("item-tooltip", request, ui.Text($"Item {request.ItemId}")))
                : list;
        }
    }
}
