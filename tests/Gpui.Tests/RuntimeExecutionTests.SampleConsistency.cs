extern alias TaskBoard;
extern alias Wander;

using Gpui.Interop;
using Board = TaskBoard::Gpui;
using Travel = Wander::Gpui;

namespace Gpui.Tests;

public sealed unsafe partial class RuntimeExecutionTests
{
    [Fact]
    public void FeedLikesPublishNewRecordsAndRevisionToEverySubscriber()
    {
        var store = new Travel.TravelStore();
        var filter = new Travel.ExploreFilter(store, store.Revision, default, 0, -1);
        var original = Travel.TravelStore.ApplyFilter(filter)[0];
        var notifications = 0;
        using var first = store.Subscribe(() => notifications++);
        using var second = store.Subscribe(() => notifications++);

        store.ToggleEntryLike(original.Id);
        Assert.Equal(2, notifications);
        Assert.True(store.Revision > filter.StoreRevision);
        var liked = Travel.TravelStore.ApplyFilter(filter with { StoreRevision = store.Revision })[0];
        Assert.NotSame(original, liked);
        Assert.True(liked.Liked);
        Assert.Equal(original.Likes + 1, liked.Likes);

        store.ToggleEntryLike(original.Id);
        Assert.Equal(4, notifications);
        Assert.Equal(original, store.FindEntry(original.Id));
    }

    [Fact]
    public void ProfileRepeatedUntouchedSavePreservesBioAndExternalResetSynchronizesControls()
    {
        var store = new Travel.TravelStore();
        var bio = store.ProfileBio;
        var application = new GpuiApplication();
        var spec = Travel.ProfileView.Spec(new(store));
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<Travel.ProfileView, Travel.ProfileProps>(spec), window);
        var commands = fixture.CaptureResourceCommands();

        for (var save = 0; save < 2; save++)
        {
            Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
            var click = SampleButtonClick(arena, "profile-save"u8);
            Assert.Equal(0, fixture.Complete(revision));
            Assert.Equal(0, fixture.Click(click));
            Assert.Equal(bio, store.ProfileBio);
        }

        store.SetProfile("Changed", "Changed bio");
        store.SetGoal(300);
        fixture.RenderFromNative();
        commands.Clear();
        var resetRevision = store.ResetRevision;
        store.Reset(); // The menu uses this same model entry point.
        fixture.RenderFromNative();
        Assert.True(store.ResetRevision > resetRevision);
        Assert.Contains(commands, call => call.Command.Data == store.ProfileName);
        Assert.Contains(commands, call => call.Command.Data == bio);
        Assert.Single(commands, call => call.Command.Command == ResourceCommandKind.SliderSetValue);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void TaskDetailSynchronizesOnlyTheModelFieldThatChanged()
    {
        var store = new Board.TaskStore();
        var task = store.Tasks[0];
        var application = new GpuiApplication();
        var spec = Board.TaskDetailView.Spec(new(store, task.Id));
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<Board.TaskDetailView, Board.TaskDetailProps>(spec), window);
        var commands = fixture.CaptureResourceCommands();
        fixture.RenderFromNative();
        Assert.Equal(3, commands.Count);

        commands.Clear();
        store.SetEstimate(task.Id, 100);
        fixture.RenderFromNative();
        Assert.Equal(ResourceCommandKind.SliderSetValue, Assert.Single(commands).Command.Command);

        commands.Clear();
        store.RenameTask(task.Id, "External title");
        fixture.RenderFromNative();
        Assert.Equal("External title", Assert.Single(commands).Command.Data);

        commands.Clear();
        store.SetAssignee(task.Id, "External assignee");
        fixture.RenderFromNative();
        Assert.Equal("External assignee", Assert.Single(commands).Command.Data);
        Assert.Null(fixture.Session.Failure);
    }

    [Fact]
    public void TaskEstimateRejectsAnInterveningModelEdit()
    {
        var store = new Board.TaskStore();
        var task = store.Tasks[0];
        var application = new GpuiApplication();
        var spec = Board.TaskDetailView.Spec(new(store, task.Id));
        var window = application.OpenWindow(spec);
        using var fixture = new SessionFixture(null, application,
            new RootViewDeclaration<Board.TaskDetailView, Board.TaskDetailProps>(spec), window);
        Assert.Equal(0, fixture.NativePublish(out var revision, out var arena));
        var click = SampleButtonClick(arena, "detail-suggest"u8);
        Assert.Equal(0, fixture.Complete(revision));
        Assert.Equal(0, fixture.Click(click));
        store.SetEstimate(task.Id, 100);

        Assert.True(SpinWait.SpinUntil(() =>
        {
            Assert.Equal(0, fixture.NativePublish(out var next, out var output));
            var text = System.Text.Encoding.UTF8.GetString(output.Utf8, output.Utf8Length);
            Assert.Equal(0, fixture.Complete(next));
            return text.Contains("Task changed; suggestion discarded.", StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5)));
        Assert.Equal(100, store.Find(task.Id)!.EstimateHours);
        Assert.Null(fixture.Session.Failure);
    }
}
