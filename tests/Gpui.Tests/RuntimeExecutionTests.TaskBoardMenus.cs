extern alias TaskBoard;

using System.Reflection;
using Gpui.Interop;
using Board = TaskBoard::Gpui;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Theory]
    [InlineData("ctx-open")]
    [InlineData("ctx-duplicate")]
    [InlineData("ctx-toggle")]
    [InlineData("ctx-delete")]
    public void TaskBoardContextMenuActsOnClickedTaskAndPreservesSelection(string action)
    {
        var application = new GpuiApplication();
        var spec = Board.TaskBoardShellView.Spec();
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<Board.TaskBoardShellView>(spec), window);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var select = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ListOnSelectionRequested).A;
        Assert.Equal(0, fixture.Complete(revision));
        var view = fixture.Session.RootView;
        var store = PrivateSampleField<Board.TaskStore>(view, "_store");
        var selected = store.Tasks[0];
        var target = store.Tasks[1];
        var count = store.Tasks.Count;
        Assert.Equal(0, fixture.Control(select, (ushort)ListEventKind.SelectionRequested,
            SelectionPayload(0, (ulong)selected.Id), 2, 1));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        var request = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ListOnContextMenuRequested).A;
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(0, fixture.Control(request, (ushort)ListEventKind.ContextMenuRequested,
            RowMenuPayload(0, (ulong)target.Id, 99), 2, 1));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        var text = System.Text.Encoding.UTF8.GetString(arena.Utf8, arena.Utf8Length);
        Assert.Contains(target.Title, text);
        Assert.Contains(target.Completed ? "Mark incomplete" : "Mark complete", text);
        var click = SampleButtonClick(arena, System.Text.Encoding.UTF8.GetBytes(action));
        var operation = Assert.Single(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.OnClick && op.A == click);
        Assert.Equal((ulong)target.Id, operation.B);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(selected.Id, PrivateSampleField<long>(view, "_selectedId"));
        Assert.Equal(0, fixture.Click(click, operation.B));
        Assert.Equal(0, fixture.NativePublish(out revision, out arena));
        Assert.DoesNotContain(new ReadOnlySpan<OpRecord>(arena.Ops, arena.OpLength).ToArray(),
            op => op.Code == (ushort)OpCode.ContextMenuRowAnchor);

        if (action == "ctx-delete")
        {
            text = System.Text.Encoding.UTF8.GetString(arena.Utf8, arena.Utf8Length);
            Assert.Contains($"“{target.Title}” will be removed.", text);
            Assert.Equal(target, store.Find(target.Id));
            click = SampleButtonClick(arena, "delete-confirm"u8);
            Assert.Equal(0, fixture.Complete(revision));
            Assert.Equal(0, fixture.Click(click));
            fixture.RenderFromNative();
            Assert.Null(store.Find(target.Id));
        }
        else
        {
            Assert.Equal(0, fixture.Complete(revision));
            switch (action)
            {
                case "ctx-open":
                    var windows = PrivateSampleField<Dictionary<ulong, GpuiWindow>>(application, "_windows");
                    var opened = Assert.Single(windows.Values, value => value != window);
                    Assert.Equal($"Task #{target.Id} — {target.Title}", opened.Snapshot.Title);
                    break;
                case "ctx-duplicate":
                    Assert.Equal(count + 1, store.Tasks.Count);
                    Assert.StartsWith(target.Title, store.Tasks[^1].Title);
                    Assert.Equal(target, store.Find(target.Id));
                    break;
                case "ctx-toggle":
                    Assert.Equal(!target.Completed, store.Find(target.Id)!.Completed);
                    break;
            }
        }
        Assert.Equal(selected, store.Find(selected.Id));
        Assert.Equal(selected.Id, PrivateSampleField<long>(view, "_selectedId"));
        Assert.Null(fixture.Session.Failure);
    }

    private static T PrivateSampleField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
