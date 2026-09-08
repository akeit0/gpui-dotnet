using System.Collections.Immutable;

namespace Gpui;

/// <summary>
/// Workflow state of a single task. Stored as the model; presentation mapping lives in styles.
/// </summary>
public enum TaskStatus
{
    Todo,
    InProgress,
    Review,
    Done,
}

/// <summary>Application-owned priority vocabulary. Never crosses the native ABI.</summary>
public enum TaskPriority
{
    Low,
    Medium,
    High,
}

/// <summary>One project in the sidebar. Identity is <see cref="Id"/>.</summary>
public sealed record Project(string Id, string Name);

/// <summary>One row in the task table. Identity is <see cref="Id"/> (never zero).</summary>
public sealed record TaskItem(
    long Id,
    string ProjectId,
    string Title,
    TaskStatus Status,
    TaskPriority Priority,
    string Assignee,
    float EstimateHours,
    bool Completed,
    ImmutableArray<string> Attachments
)
{
    /// <summary>Per-task edit token, advanced only by committed model changes.</summary>
    public ulong Revision { get; init; } = 1;
}

/// <summary>
/// In-memory document for the sample. Owns the revision that drives virtual-row cache
/// validity and notifies subscribers (Views subscribe through accepted effects).
/// All mutation happens on the GPUI application thread via events or owned work.
/// </summary>
public sealed class TaskStore
{
    private long _nextId = 1;
    private ulong _revision = 1;
    private event Action? Changed;
    private readonly List<TaskItem> _tasks = [];

    public List<Project> Projects { get; } = [];
    public IReadOnlyList<TaskItem> Tasks { get; }
    public List<string> Activity { get; } = [];
    public ulong Revision => _revision;

    public TaskStore()
    {
        Tasks = _tasks.AsReadOnly();
        Seed();
    }

    public IDisposable Subscribe(Action callback)
    {
        Changed += callback;
        return new Subscription(this, callback);
    }

    private void Notify(string activity)
    {
        _revision++;
        Activity.Add($"{DateTime.Now:HH:mm:ss}  {activity}");
        if (Activity.Count > 300)
        {
            Activity.RemoveRange(0, Activity.Count - 300);
        }
        Changed?.Invoke();
    }

    private sealed class Subscription(TaskStore store, Action callback) : IDisposable
    {
        public void Dispose() => store.Changed -= callback;
    }

    private int FindIndex(long id)
    {
        for (var index = 0; index < _tasks.Count; index++)
            if (_tasks[index].Id == id)
                return index;
        return -1;
    }

    public TaskItem? Find(long id)
    {
        var index = FindIndex(id);
        return index < 0 ? null : _tasks[index];
    }

    public TaskItem AddTask(
        string projectId,
        string title,
        TaskStatus status = TaskStatus.Todo,
        TaskPriority priority = TaskPriority.Medium,
        string assignee = "Unassigned",
        float estimateHours = 4
    )
    {
        var task = new TaskItem(
            _nextId++,
            projectId,
            string.IsNullOrWhiteSpace(title) ? "Untitled task" : title.Trim(),
            status,
            priority,
            string.IsNullOrWhiteSpace(assignee) ? "Unassigned" : assignee.Trim(),
            NormalizeEstimate(estimateHours),
            false,
            []
        );
        _tasks.Add(task);
        Notify($"Added “{task.Title}”");
        return task;
    }

    public void RemoveTask(long id)
    {
        var index = FindIndex(id);
        if (index < 0)
        {
            return;
        }
        var removed = Tasks[index];
        _tasks.RemoveAt(index);
        Notify($"Removed “{removed.Title}”");
    }

    public void DuplicateTask(long id)
    {
        var task = Find(id);
        if (task is null)
        {
            return;
        }
        var copy = task with { Id = _nextId++, Title = task.Title + " (copy)", Revision = 1 };
        _tasks.Add(copy);
        Notify($"Duplicated “{task.Title}”");
    }

    private void Replace(long id, Func<TaskItem, TaskItem> update, string activity)
    {
        var index = FindIndex(id);
        if (index < 0)
        {
            return;
        }
        CommitReplacement(index, update(_tasks[index]), activity);
    }

    private void CommitReplacement(int index, TaskItem updated, string activity)
    {
        var current = _tasks[index];
        if (updated == current)
            return;
        _tasks[index] = updated with { Revision = checked(current.Revision + 1) };
        Notify(activity);
    }

    public void RenameTask(long id, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }
        Replace(id, t => t with { Title = title.Trim() }, $"Renamed task #{id}");
    }

    public void SetAssignee(long id, string assignee) =>
        Replace(id, t => t with { Assignee = assignee.Trim() }, $"Reassigned task #{id}");

    public void SetStatus(long id, TaskStatus status) =>
        Replace(
            id,
            t => t with { Status = status, Completed = status == TaskStatus.Done || t.Completed },
            $"Moved task #{id} to {status}"
        );

    public void SetPriority(long id, TaskPriority priority) =>
        Replace(id, t => t with { Priority = priority }, $"Reprioritized task #{id}");

    public void SetEstimate(long id, float hours)
    {
        var normalized = NormalizeEstimate(hours);
        Replace(id, t => t with { EstimateHours = normalized }, $"Re-estimated task #{id}");
    }

    /// <summary>
    /// Applies a result only to the expected task version. Check and commit run together on the
    /// application's UI thread. Unrelated tasks do not invalidate the token. Equal values succeed
    /// without advancing revisions or appending activity; deleted or edited tasks return false.
    /// </summary>
    public bool TrySetEstimate(long id, ulong expectedRevision, float hours)
    {
        var normalized = NormalizeEstimate(hours);
        var index = FindIndex(id);
        if (index < 0 || _tasks[index].Revision != expectedRevision)
            return false;
        CommitReplacement(
            index,
            _tasks[index] with
            {
                EstimateHours = normalized,
            },
            $"Re-estimated task #{id}"
        );
        return true;
    }

    private static float NormalizeEstimate(float hours)
    {
        if (!float.IsFinite(hours))
            throw new ArgumentOutOfRangeException(nameof(hours));
        return Math.Clamp(hours, 0, 120);
    }

    public void ToggleCompleted(long id)
    {
        var task = Find(id);
        if (task is null)
        {
            return;
        }
        var completed = !task.Completed;
        Replace(
            id,
            t =>
                t with
                {
                    Completed = completed,
                    Status =
                        completed ? TaskStatus.Done
                        : t.Status == TaskStatus.Done ? TaskStatus.Todo
                        : t.Status,
                },
            completed ? $"Completed task #{id}" : $"Reopened task #{id}"
        );
    }

    public void AddAttachment(long id, string fileName) =>
        Replace(
            id,
            t => t with { Attachments = [.. t.Attachments, fileName] },
            $"Attached “{fileName}” to task #{id}"
        );

    public int CountForProject(string projectId) =>
        projectId == "all" ? Tasks.Count : Tasks.Count(t => t.ProjectId == projectId);

    /// <summary>
    /// Pure filter/sort used as the <see cref="Memo{TInput, TResult}"/> calculation.
    /// Reads only the store snapshot and the equatable filter input. Runs only when the
    /// input changes, so the list allocation is amortized across renders.
    /// </summary>
    public static List<TaskItem> ApplyFilter(BoardFilter filter)
    {
        var store = filter.Store;
        var result = new List<TaskItem>(store.Tasks.Count);
        foreach (var task in store.Tasks)
        {
            if (filter.ProjectId != "all" && task.ProjectId != filter.ProjectId)
            {
                continue;
            }
            if (filter.Status.HasValue && task.Status != filter.Status.Value)
            {
                continue;
            }
            if (filter.OnlyOpen && task.Completed)
            {
                continue;
            }
            if (
                !filter.Query.IsEmpty
                && !task.Title.Contains(filter.Query.Text, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }
            result.Add(task);
        }
        result.Sort(
            (a, b) =>
            {
                var order = filter.SortBy switch
                {
                    BoardSort.Priority => Comparer<TaskPriority>.Default.Compare(
                        a.Priority,
                        b.Priority
                    ),
                    BoardSort.Estimate => a.EstimateHours.CompareTo(b.EstimateHours),
                    _ => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
                };
                if (order == 0)
                {
                    order = a.Id.CompareTo(b.Id);
                }
                return filter.Ascending ? order : -order;
            }
        );
        return result;
    }

    private void Seed()
    {
        Projects.Add(new Project("platform", "Platform"));
        Projects.Add(new Project("mobile", "Mobile App"));
        Projects.Add(new Project("website", "Website"));
        var assignees = new[] { "Aiko", "Ben", "Chloe", "Dev", "Eri", "Farah" };
        var titles = new[]
        {
            "Fix flaky sync retry",
            "Add offline queue",
            "Review auth tokens",
            "Migrate settings screen",
            "Polish empty states",
            "Write release notes",
            "Benchmark list scrolling",
            "Harden IME paths",
            "Add keyboard shortcuts",
            "Audit focus order",
            "Shrink native payload",
            "Cache row measurements",
            "Design onboarding",
            "Localize error strings",
            "Add dark-mode snapshots",
        };
        var random = new Random(42);
        var projects = new[] { "platform", "mobile", "website" };
        for (var i = 0; i < 180; i++)
        {
            var status = (TaskStatus)(i % 7 == 6 ? 3 : i % 4);
            _tasks.Add(
                new TaskItem(
                    _nextId++,
                    projects[i % projects.Length],
                    $"{titles[i % titles.Length]} #{i + 1}",
                    status,
                    (TaskPriority)(i % 3),
                    assignees[random.Next(assignees.Length)],
                    1 + random.Next(16),
                    status == TaskStatus.Done,
                    []
                )
            );
        }
        Activity.Add($"{DateTime.Now:HH:mm:ss}  Seeded 180 tasks across 3 projects");
    }
}

/// <summary>Sort vocabulary for the task table. Application-owned, never native.</summary>
public enum BoardSort
{
    Title,
    Priority,
    Estimate,
}

/// <summary>Equatable search text so it can ride inside memo/effect inputs.</summary>
public readonly record struct BoardQuery(string Text)
{
    public bool IsEmpty => string.IsNullOrEmpty(Text);
}

/// <summary>
/// Memo input covering every dependency of the visible-row list: the store plus all local
/// filter state. The store travels inside the input so the calculation stays a static
/// lambda; Signals are read while assembling this value, never inside the calculation.
/// </summary>
public readonly record struct BoardFilter(
    TaskStore Store,
    ulong StoreRevision,
    string ProjectId,
    BoardQuery Query,
    TaskStatus? Status,
    BoardSort SortBy,
    bool Ascending,
    bool OnlyOpen
);
