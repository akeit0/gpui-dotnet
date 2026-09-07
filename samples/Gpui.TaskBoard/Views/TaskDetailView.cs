using System.Runtime.CompilerServices;
using static Gpui.Units;

namespace Gpui;

/// <summary>
/// Props for the detail pane. The same View serves as an embedded child and as an
/// independent window root, sharing the live <see cref="TaskStore"/> reference.
/// </summary>
internal readonly record struct TaskDetailProps(TaskStore Store, long TaskId);

/// <summary>
/// Inspector for one task. Edits commit to the store from events; the store subscription
/// (accepted effect) rerenders this View wherever it is mounted. Native input values are
/// synchronized from accepted props through an effect, so Render builds no key strings.
/// Render allocates nothing on the heap: ref-bound controllers, static handlers with
/// payloads, stack-span collection expressions, one small inline buffer for the
/// variable-length attachment list, and arena-direct interpolated text.
/// </summary>
[GpuiView]
internal sealed partial class TaskDetailView : View<TaskDetailProps>
{
    [InlineArray(7)]
    private struct AttachBuffer
    {
        private Element _element;
    }

    private static readonly SliderOptions EstimateSliderOptions = new(min: 0, max: 40, step: 1);

    private readonly Effect<NoProps> _watch;
    private readonly Effect<TaskDetailProps> _sync;
    private readonly WorkScope _work;
    private readonly GpuiApplication _application;
    private InputController _title;
    private InputController _assignee;
    private SliderController _estimate;
    private string _suggestion = "Suggest an estimate from the title length.";

    public TaskDetailView(ViewConstruction construction, TaskDetailProps initialProps)
        : base(construction)
    {
        _application = construction.Application;
        _work = construction.Work;
        _watch = construction.Effect<NoProps>(WatchStore);
        _sync = construction.Effect<TaskDetailProps>(SyncInputs);
    }

    private void WatchStore(EffectScope scope, NoProps input)
    {
        var store = CommittedProps.Store;
        scope.Own(store.Subscribe(scope.Bind(this, static view => view.Invalidate())));
    }

    private void SyncInputs(EffectScope scope, TaskDetailProps input)
    {
        // Accepted effect: safe to command accepted resources before they materialize.
        // Runs only when the props value changes (new task selected), never per keystroke,
        // because live edits keep the same store reference and task id.
        var task = input.Store.Find(input.TaskId);
        if (task is null)
        {
            return;
        }
        _title.SetValue(task.Title);
        _assignee.SetValue(task.Assignee);
        _estimate.SetValue(task.EstimateHours);
    }

    private void SuggestEstimate()
    {
        var task = CommittedProps.Store.Find(CommittedProps.TaskId);
        if (task is null)
        {
            return;
        }
        _suggestion = "Estimating…";
        _work.Start(
            this,
            task.Title,
            static async (title, lifetime) =>
            {
                await Task.Delay(350, lifetime).ConfigureAwait(false);
                return Math.Clamp(1 + title.Length / 8, 1, 16);
            },
            static (view, hours) =>
            {
                var current = view.CommittedProps.Store.Find(view.CommittedProps.TaskId);
                if (current is null)
                {
                    return;
                }
                view._suggestion = $"Suggested {hours}h for “{current.Title}”. Applied.";
                view.CommittedProps.Store.SetEstimate(current.Id, hours);
            },
            static (view, failure) => view._suggestion = $"Estimate failed: {failure.Message}"
        );
    }

    private void OpenInWindow()
    {
        var props = CommittedProps;
        var window = _application.OpenWindow(
            TaskDetailView.Spec(props),
            new GpuiWindowOptions
            {
                Title = $"Task #{props.TaskId}",
                Width = 480,
                Height = 640,
            }
        );
        var task = props.Store.Find(props.TaskId);
        if (task is not null)
        {
            window.SetTitle($"Task #{task.Id} — {task.Title}");
        }
    }

    private void SetStatus(ulong payload) =>
        CommittedProps.Store.SetStatus(CommittedProps.TaskId, (TaskStatus)payload);

    private void SetPriority(ulong payload) =>
        CommittedProps.Store.SetPriority(CommittedProps.TaskId, (TaskPriority)payload);

    private static string StatusId(TaskStatus status) =>
        status switch
        {
            TaskStatus.Todo => "detail-status-0",
            TaskStatus.InProgress => "detail-status-1",
            TaskStatus.Review => "detail-status-2",
            _ => "detail-status-3",
        };

    private static string PriorityId(TaskPriority priority) =>
        priority switch
        {
            TaskPriority.Low => "detail-priority-0",
            TaskPriority.Medium => "detail-priority-1",
            _ => "detail-priority-2",
        };

    private static string PriorityLabel(TaskPriority priority) =>
        priority switch
        {
            TaskPriority.Low => "Low",
            TaskPriority.Medium => "Medium",
            TaskPriority.High => "High",
            _ => "Medium",
        };

    internal static string PriorityLabelFor(TaskPriority priority) => PriorityLabel(priority);

    private static Element StatusButton(
        ref RenderContext ui,
        TaskDetailView view,
        TaskItem task,
        GpuiTheme theme,
        TaskStatus status
    ) =>
        ui.Button(
                StatusId(status),
                ui.Text(BoardStyles.StatusLabel(status))
                    .TextColor(task.Status == status ? theme.Colors.TextOnAccent : theme.Colors.Text)
            )
            .OnClick(view, static (v, e) => v.SetStatus(e.Payload), (ulong)status)
            .Style(
                BoardStyles.Button(
                    theme,
                    task.Status == status ? BoardButtonVariant.Primary : BoardButtonVariant.Standard
                )
            );

    private static Element PriorityButton(
        ref RenderContext ui,
        TaskDetailView view,
        TaskItem task,
        TaskPriority priority
    ) =>
        ui.Radio(PriorityId(priority), ui.Text(PriorityLabel(priority)))
            .Checked(task.Priority == priority)
            .OnClick(view, static (v, e) => v.SetPriority(e.Payload), (ulong)priority)
            .Padding(Px(6));

    protected override Element Render(in TaskDetailProps props, ref RenderContext ui)
    {
        ui.Effect(_watch, default);
        ui.Effect(_sync, props);
        var theme = ui.Theme;
        var task = props.Store.Find(props.TaskId);
        if (task is null)
        {
            return ui.VStack(
                    ui.Text("No task selected")
                        .FontSize(Px(theme.Typography.Heading))
                        .TextColor(theme.Colors.Text),
                    ui.Text("Select a row in the table, or press Enter to open it in a window.")
                        .FontSize(Px(theme.Typography.Detail))
                        .TextColor(theme.Colors.TextMuted)
                )
                .Gap(Px(8))
                .Padding(Px(18))
                .Grow()
                .Background(theme.Colors.SurfaceBackground)
                .BorderWidth(Px(1))
                .BorderColor(theme.Colors.BorderVariant)
                .Radius(Px(10));
        }

        Span<Element> statuses =
        [
            StatusButton(ref ui, this, task, theme, TaskStatus.Todo),
            StatusButton(ref ui, this, task, theme, TaskStatus.InProgress),
            StatusButton(ref ui, this, task, theme, TaskStatus.Review),
            StatusButton(ref ui, this, task, theme, TaskStatus.Done),
        ];

        Span<Element> priorities =
        [
            PriorityButton(ref ui, this, task, TaskPriority.Low),
            PriorityButton(ref ui, this, task, TaskPriority.Medium),
            PriorityButton(ref ui, this, task, TaskPriority.High),
        ];

        // Variable-length content keeps one small inline buffer; capped, never a List.
        AttachBuffer attachments = default;
        var shown = Math.Min(task.Attachments.Length, 6);
        for (var i = 0; i < shown; i++)
        {
            attachments[i] = ui.Text($"• {task.Attachments[i]}").TextColor(theme.Colors.Text);
        }
        var attachmentCount = shown;
        if (task.Attachments.Length == 0)
        {
            attachments[0] = ui.Text("Drop files here to attach them.")
                .FontSize(Px(theme.Typography.Detail))
                .TextColor(theme.Colors.TextMuted);
            attachmentCount = 1;
        }
        else if (task.Attachments.Length > shown)
        {
            attachments[shown] = ui.Text($"…and {task.Attachments.Length - shown} more")
                .FontSize(Px(theme.Typography.Detail))
                .TextColor(theme.Colors.TextMuted);
            attachmentCount = shown + 1;
        }
        Span<Element> attachmentSpan = attachments;
        var attachmentList = ui.VStack(attachmentSpan[..attachmentCount]).Gap(Px(2));

        return ui.VStack(
                ui.HStack(
                        ui.VStack(
                                ui.Text($"Task #{task.Id}")
                                    .FontSize(Px(theme.Typography.Caption))
                                    .TextColor(theme.Colors.TextMuted),
                                ui.Text(task.Title)
                                    .FontSize(Px(theme.Typography.Title))
                                    .TextColor(theme.Colors.Text)
                            )
                            .Gap(Px(2)),
                        ui.Spacer(),
                        ui.Badge(
                                ui.Text(BoardStyles.StatusLabel(task.Status))
                                    .FontSize(Px(theme.Typography.Caption))
                                    .TextColor(BoardStyles.StatusColor(task.Status, theme.Colors))
                            )
                            .Background(BoardStyles.StatusBackground(task.Status, theme.Colors))
                            .Padding(Px(7))
                    )
                    .ItemsCenter(),
                ui.Input(ref _title, new Utf8InputOptions(placeholder: "Task title"u8))
                    .Style(BoardStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.CommittedProps.Store.RenameTask(view.CommittedProps.TaskId, e.Value))
                    .OnSubmitted(this, static (view, e) => view.CommittedProps.Store.RenameTask(view.CommittedProps.TaskId, e.Value))
                    .Width(Percent(100)),
                ui.HStack(statuses).Gap(Px(6)).Wrap(FlexWrap.Wrap),
                ui.Checkbox("detail-completed", "Completed")
                    .Checked(task.Completed)
                    .OnClick(this, static (view, _) => view.CommittedProps.Store.ToggleCompleted(view.CommittedProps.TaskId))
                    .Padding(Px(6)),
                ui.HStack(priorities).Gap(Px(4)).ItemsCenter(),
                ui.VStack(
                        ui.Text($"Estimate: {task.EstimateHours:0}h")
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextMuted),
                        ui.Slider(ref _estimate, EstimateSliderOptions)
                            .Style(BoardStyles.Estimate(theme))
                            .OnChanged(this, static (view, e) => view.CommittedProps.Store.SetEstimate(view.CommittedProps.TaskId, e.End))
                            .Width(Percent(100)),
                        ui.Text(_suggestion)
                            .FontSize(Px(theme.Typography.Detail))
                            .TextColor(theme.Colors.TextAccent)
                    )
                    .Gap(Px(6)),
                ui.Input(ref _assignee, new Utf8InputOptions(placeholder: "Assignee"u8))
                    .Style(BoardStyles.Field(theme))
                    .OnSubmitted(this, static (view, e) => view.CommittedProps.Store.SetAssignee(view.CommittedProps.TaskId, e.Value))
                    .Width(Percent(100)),
                attachmentList
                    .Padding(Px(10))
                    .Width(Percent(100))
                    .Background(theme.Colors.ElementHover)
                    .Radius(Px(8))
                    .OnFileDrop(
                        this,
                        static (view, drop) =>
                        {
                            var dropProps = view.CommittedProps;
                            foreach (var path in drop.Paths)
                            {
                                dropProps.Store.AddAttachment(dropProps.TaskId, Path.GetFileName(path));
                            }
                        }
                    ),
                ui.HStack(
                        ui.Button("detail-suggest", "Suggest estimate")
                            .OnClick(this, static (view, _) => view.SuggestEstimate())
                            .Style(BoardStyles.Button(theme)),
                        ui.Button("detail-window", "Open in window")
                            .OnClick(this, static (view, _) => view.OpenInWindow())
                            .Style(BoardStyles.Button(theme)),
                        ui.Button("detail-duplicate", "Duplicate")
                            .OnClick(this, static (view, e) => view.CommittedProps.Store.DuplicateTask(view.CommittedProps.TaskId))
                            .Style(BoardStyles.Button(theme, BoardButtonVariant.Danger))
                    )
                    .Gap(Px(8))
                    .Wrap(FlexWrap.Wrap)
            )
            .Gap(Px(12))
            .Padding(Px(16))
            .Grow()
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.BorderVariant)
            .Radius(Px(10))
            .OnKeyDown(
                this,
                static (view, key) =>
                {
                    if (!key.IsHeld && key.Matches("d", control: true))
                    {
                        view.CommittedProps.Store.DuplicateTask(view.CommittedProps.TaskId);
                    }
                }
            );
    }
}
