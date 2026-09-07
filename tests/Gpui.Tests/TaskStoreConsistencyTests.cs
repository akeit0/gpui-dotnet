extern alias TaskBoard;

using Board = TaskBoard::Gpui;

namespace Gpui.Tests;

public sealed class TaskStoreConsistencyTests
{
    [Fact]
    public void EstimateCommitUsesTaskRevisionAndRejectsASecondStaleWriter()
    {
        var store = new Board.TaskStore();
        var original = store.Tasks[0];
        var unrelated = store.Tasks[1];
        store.RenameTask(unrelated.Id, "Other task changed");
        var notifications = 0;
        using var subscription = store.Subscribe(() => notifications++);
        var revision = store.Revision;
        var activity = store.Activity.Count;

        Assert.True(store.TrySetEstimate(original.Id, original.Revision, 100));
        Assert.Equal(original.Revision + 1, store.Find(original.Id)!.Revision);
        Assert.Equal(revision + 1, store.Revision);
        Assert.Equal(activity + 1, store.Activity.Count);
        Assert.Equal(1, notifications);
        Assert.False(store.TrySetEstimate(original.Id, original.Revision, 30));
        Assert.Equal(100, store.Find(original.Id)!.EstimateHours);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ChangingATaskBackToItsOriginalValueDoesNotRestoreWriteAuthority()
    {
        var store = new Board.TaskStore();
        var original = store.Tasks[0];
        store.RenameTask(original.Id, "Temporary title");
        store.RenameTask(original.Id, original.Title);
        var restored = store.Find(original.Id)!;
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Revision + 2, restored.Revision);
        Assert.False(store.TrySetEstimate(original.Id, original.Revision, 100));
        Assert.Same(restored, store.Find(original.Id));
        store.RemoveTask(original.Id);
        Assert.False(store.TrySetEstimate(original.Id, restored.Revision, 100));
    }

    [Fact]
    public void EqualNormalizedEditsDoNotInvalidateWorkOrAppendActivity()
    {
        var store = new Board.TaskStore();
        var original = store.Tasks[0];
        var revision = store.Revision;
        var activity = store.Activity.Count;
        var notifications = 0;
        using var subscription = store.Subscribe(() => notifications++);
        store.RenameTask(original.Id, "  " + original.Title + "  ");
        store.SetAssignee(original.Id, "  " + original.Assignee + "  ");
        store.SetPriority(original.Id, original.Priority);
        store.SetStatus(original.Id, original.Status);
        store.SetEstimate(original.Id, original.EstimateHours);
        Assert.True(store.TrySetEstimate(original.Id, original.Revision, original.EstimateHours));
        Assert.Same(original, store.Find(original.Id));
        Assert.Equal(revision, store.Revision);
        Assert.Equal(activity, store.Activity.Count);
        Assert.Equal(0, notifications);
        Assert.True(store.TrySetEstimate(original.Id, original.Revision, 100));
    }

    [Fact]
    public void EveryTaskMutationRevokesThePreviousRevision()
    {
        var store = new Board.TaskStore();
        var original = store.Tasks[0];
        Action[] edits = [
            () => store.RenameTask(original.Id, "New title"),
            () => store.SetAssignee(original.Id, "New assignee"),
            () => store.SetStatus(original.Id, Board.TaskStatus.Review),
            () => store.SetPriority(original.Id, Board.TaskPriority.High),
            () => store.SetEstimate(original.Id, 100),
            () => store.ToggleCompleted(original.Id),
            () => store.AddAttachment(original.Id, "notes.txt"),
        ];
        foreach (var edit in edits)
        {
            var previous = store.Find(original.Id)!;
            edit();
            Assert.Equal(previous.Revision + 1, store.Find(original.Id)!.Revision);
            Assert.False(store.TrySetEstimate(original.Id, previous.Revision, 70));
        }
        Assert.Empty(original.Attachments);
        Assert.Equal("notes.txt", Assert.Single(store.Find(original.Id)!.Attachments));
    }

    [Fact]
    public void TaskCollectionCannotBypassRevisionTrackingAndDuplicatesStartIndependentVersions()
    {
        var store = new Board.TaskStore();
        Assert.Throws<NotSupportedException>(() => ((IList<Board.TaskItem>)store.Tasks).Clear());
        var original = store.Tasks[0];
        store.RenameTask(original.Id, "Edited before duplication");
        store.DuplicateTask(original.Id);
        var copy = store.Tasks[^1];
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(1UL, copy.Revision);
        Assert.True(store.TrySetEstimate(copy.Id, copy.Revision, 100));
        Assert.Equal(original.EstimateHours, store.Find(original.Id)!.EstimateHours);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidEstimateDoesNotConsumeRevisionOrMutateModel(float hours)
    {
        var store = new Board.TaskStore();
        var task = store.Tasks[0];
        var revision = store.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => store.TrySetEstimate(task.Id, task.Revision, hours));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SetEstimate(task.Id, hours));
        Assert.Same(task, store.Find(task.Id));
        Assert.Equal(revision, store.Revision);
    }
}
