using System.Diagnostics;
using System.Runtime.CompilerServices;
using static Gpui.Units;

namespace Gpui;

internal enum BoardDialog
{
    None,
    New,
    Delete,
    Settings,
}

/// <summary>
/// Root View of the TaskBoard sample: a team project tracker built from production
/// primitives (virtual table, dock, retained inputs/slider, overlays, menus, effects).
/// Ref-bound controllers, static handlers, stack-span composition, and arena-direct text
/// reduce render allocations. Projection rebuilds allocate and advance a separate identity
/// stamp when row order changes, so native positional state follows accepted declarations.
/// </summary>
[GpuiView]
internal sealed partial class TaskBoardShellView : View
{
    private enum TaskMenuAction
    {
        Open,
        Duplicate,
        ToggleComplete,
        Delete,
    }

    [InlineArray(9)]
    private struct ProjectBuffer
    {
        private Element _element;
    }

    [InlineArray(2)]
    private struct RegionBuffer
    {
        private Element _element;
    }

    private static readonly TableColumn[] TaskColumns =
    [
        new("status", "Status", 96, TableColumnWidth.Pixels, TableColumnAlignment.Center),
        new("task", "Task", 0.5f, TableColumnWidth.Fraction),
        new("assignee", "Assignee", 120),
        new("est", "Est", 64, TableColumnWidth.Pixels, TableColumnAlignment.Right),
        new("priority", "Priority", 96, TableColumnWidth.Pixels, TableColumnAlignment.Center),
    ];

    private static readonly TableOptions TasksTableOptions = new(
        batchSize: 48,
        overdraw: 320,
        estimatedItemHeight: 44,
        scrollbarGutter: true
    );

    private static readonly TooltipOptions TaskTooltipOptions = new(
        placement: TooltipPlacement.Right,
        alignment: TooltipAlignment.Center,
        showDelay: TimeSpan.FromMilliseconds(700),
        hideDelay: TimeSpan.FromMilliseconds(150),
        gap: 12,
        margin: 12
    );

    private static readonly ListOptions ActivityListOptions = new(
        batchSize: 32,
        overdraw: 200,
        estimatedItemHeight: 28
    );

    private static readonly ScrollOptions SidebarScrollOptions = new(
        showScrollbar: true,
        scrollbarGutter: true
    );

    private static readonly string[] NewProjectIds =
    [
        "new-project-0",
        "new-project-1",
        "new-project-2",
        "new-project-3",
        "new-project-4",
        "new-project-5",
        "new-project-6",
        "new-project-7",
    ];

    private static readonly string[] StatusOptionIds =
    [
        "status-option-0",
        "status-option-1",
        "status-option-2",
        "status-option-3",
        "status-option-4",
    ];

    private static readonly string[] SortOptionIds =
    [
        "sort-option-0",
        "sort-option-1",
        "sort-option-2",
    ];

    private static readonly string[] SortHeaderIds =
    [
        "sort-header-0",
        "sort-header-1",
        "sort-header-2",
    ];

    private static readonly string[] NewStatusIds =
    [
        "new-status-0",
        "new-status-1",
        "new-status-2",
        "new-status-3",
    ];

    private static readonly string[] NewPriorityIds =
    [
        "new-priority-0",
        "new-priority-1",
        "new-priority-2",
    ];

    private readonly TaskStore _store = new();
    private readonly GpuiApplication _application;
    private readonly GpuiWindow _window;
    private readonly Signal<string> _searchText = new(string.Empty);
    private readonly Signal<bool> _onlyOpen = new(false);
    private readonly Memo<BoardFilter, List<TaskItem>> _visible;
    private readonly Effect<NoProps> _storeWatch;
    private readonly Effect<NoProps> _menus;
    private readonly WorkScope _work;
    private DockController _dock;
    private readonly ScrollController _sidebar;
    private readonly HashSet<string> _closedPanels = new(StringComparer.Ordinal);

    private InputController _searchBox;
    private ListController _tasks;
    private ListController _activity;

    private string _projectId = "all";
    private TaskStatus? _status;
    private BoardSort _sort = BoardSort.Title;
    private bool _ascending = true;
    private long _selectedId = -1;
    private ListContextMenuEvent? _taskMenu;
    private ListTooltipEvent? _taskTooltip;
    private ulong _tableRevision = 1;
    private ulong _projectionRevision = 1;
    private GpuiMenu[] _menuBar = [];
    private BoardDialog _dialog = BoardDialog.None;

    private string _newTitle = string.Empty;
    private string _newAssignee = string.Empty;
    private int _newProject = 1;
    private TaskStatus _newStatus = TaskStatus.Todo;
    private TaskPriority _newPriority = TaskPriority.Medium;
    private string _newError = string.Empty;
    private long _deleteTarget = -1;

    private bool _syncing;
    private string _syncMessage = "Not synced yet.";
    private long _syncStart;
    private List<TaskItem> _rows = [];

    public TaskBoardShellView(ViewConstruction construction)
        : base(construction)
    {
        _application = construction.Application;
        _window = construction.Window;
        _visible = construction.Memo<BoardFilter, List<TaskItem>>();
        _work = construction.Work;
        _dock = construction.CreateDockController("board-dock");
        _sidebar = construction.CreateScrollController("board-sidebar");
        _storeWatch = construction.Effect<NoProps>(WatchStore);
        _menus = construction.Effect<NoProps>(InstallMenus);
    }

    // ----- effects -----

    private void WatchStore(EffectScope scope, NoProps input) =>
        scope.Own(_store.Subscribe(scope.Bind(this, static view => view.OnStoreChanged())));

    private void OnStoreChanged()
    {
        _tableRevision++;
        Invalidate();
    }

    private void InstallMenus(EffectScope scope, NoProps input)
    {
        _menuBar = CreateMenuBar(scope);
        _application.SetMenuBar(_menuBar);
        Invalidate();
    }

    private GpuiMenu[] CreateMenuBar(EffectScope scope) =>
        [
            new GpuiMenu(
                "Board",
                GpuiMenuItem.Command(
                    "New task…",
                    scope.Bind(this, static view => view.OpenNewDialog())
                ),
                GpuiMenuItem.Command(
                    "Open selected in window",
                    scope.Bind(this, static view => view.OpenSelectedInWindow())
                ),
                GpuiMenuItem.Separator(),
                GpuiMenuItem.Command(
                    "Close window",
                    scope.Bind(this, static view => view.CloseWindow())
                )
            ),
            new GpuiMenu(
                "View",
                GpuiMenuItem.Command("Sync now", scope.Bind(this, static view => view.SyncNow())),
                GpuiMenuItem.Command(
                    "Toggle light/dark theme",
                    scope.Bind(this, static view => view.ToggleTheme())
                ),
                GpuiMenuItem.Command(
                    "Toggle open-only",
                    scope.Bind(this, static view => view.ToggleOnlyOpen())
                ),
                GpuiMenuItem.Separator(),
                GpuiMenuItem.Command(
                    "Board settings…",
                    scope.Bind(this, static view => view.ToggleSettings())
                )
            ),
            new GpuiMenu(
                "Task",
                GpuiMenuItem.Command(
                    "Duplicate selected",
                    scope.Bind(this, static view => view.DuplicateSelected())
                ),
                GpuiMenuItem.Command(
                    "Toggle selected complete",
                    scope.Bind(this, static view => view.ToggleSelectedComplete())
                ),
                GpuiMenuItem.Command(
                    "Delete selected…",
                    scope.Bind(this, static view => view.DeleteSelected())
                )
            ),
        ];

    // ----- event-time mutators (never called from Render) -----

    private void CloseWindow() => _window.Close();

    private void ToggleTheme() =>
        _application.SetTheme(
            _application.Theme.Appearance == GpuiThemeAppearance.Dark
                ? TaskBoardThemes.Light
                : TaskBoardThemes.Dark
        );

    private void SelectProject(ulong payload)
    {
        if (payload == 0)
        {
            _projectId = "all";
        }
        else
        {
            var index = checked((int)payload) - 1;
            if ((uint)index >= (uint)_store.Projects.Count)
            {
                return;
            }
            _projectId = _store.Projects[index].Id;
        }
        _tableRevision++;
        Invalidate();
    }

    private void SetStatusFilter(ulong payload)
    {
        _status = payload == 0 ? null : (TaskStatus)(payload - 1);
        _tableRevision++;
        Invalidate();
    }

    private void SetSort(ulong payload)
    {
        var sort = (BoardSort)payload;
        if (_sort == sort)
        {
            _ascending = !_ascending;
        }
        else
        {
            _sort = sort;
            _ascending = true;
        }
        _tableRevision++;
        Invalidate();
    }

    private void SetSearch(string value)
    {
        _searchText.Value = value;
        _tableRevision++;
    }

    private void ClearSearch()
    {
        if (_searchBox.IsBound)
        {
            _searchBox.SetValue(string.Empty);
        }
        _searchText.Value = string.Empty;
        _tableRevision++;
        Invalidate();
    }

    private void FocusSearch()
    {
        if (_searchBox.IsBound)
        {
            _searchBox.Focus();
        }
    }

    private void ScrollSidebarTop() => _sidebar.ScrollToTop();

    private void SetOnlyOpen(bool value)
    {
        _onlyOpen.Value = value;
        _tableRevision++;
    }

    private void ToggleOnlyOpen() => SetOnlyOpen(!_onlyOpen.Value);

    private void OpenNewDialog()
    {
        _newTitle = string.Empty;
        _newAssignee = string.Empty;
        _newProject = 1;
        _newStatus = TaskStatus.Todo;
        _newPriority = TaskPriority.Medium;
        _newError = string.Empty;
        _dialog = BoardDialog.New;
        Invalidate();
    }

    private void SetNewProject(ulong payload)
    {
        _newProject = Math.Clamp(checked((int)payload), 1, Math.Max(1, _store.Projects.Count));
        Invalidate();
    }

    private void SetNewStatus(ulong payload)
    {
        _newStatus = (TaskStatus)payload;
        Invalidate();
    }

    private void SetNewPriority(ulong payload)
    {
        _newPriority = (TaskPriority)payload;
        Invalidate();
    }

    private void CreateTask()
    {
        if (string.IsNullOrWhiteSpace(_newTitle))
        {
            _newError = "Give the task a title before creating it.";
            Invalidate();
            return;
        }
        var projectIndex = Math.Clamp(_newProject - 1, 0, _store.Projects.Count - 1);
        var task = _store.AddTask(
            _store.Projects[projectIndex].Id,
            _newTitle,
            _newStatus,
            _newPriority,
            _newAssignee
        );
        _selectedId = task.Id;
        _dialog = BoardDialog.None;
        _newTitle = string.Empty;
        _newAssignee = string.Empty;
        _newError = string.Empty;
        Invalidate();
    }

    private void CancelDialog()
    {
        _dialog = BoardDialog.None;
        Invalidate();
    }

    private void ToggleSettings()
    {
        _dialog = _dialog == BoardDialog.Settings ? BoardDialog.None : BoardDialog.Settings;
        Invalidate();
    }

    private void DeleteSelected() => RequestDelete(_selectedId);

    private void RequestDelete(long id)
    {
        if (_store.Find(id) is null)
        {
            return;
        }
        _deleteTarget = id;
        _dialog = BoardDialog.Delete;
        Invalidate();
    }

    private void ConfirmDelete()
    {
        _store.RemoveTask(_deleteTarget);
        if (_selectedId == _deleteTarget)
        {
            _selectedId = -1;
        }
        _deleteTarget = -1;
        _dialog = BoardDialog.None;
        Invalidate();
    }

    private void DuplicateSelected()
    {
        if (_selectedId >= 0)
        {
            _store.DuplicateTask(_selectedId);
        }
    }

    private void ToggleSelectedComplete()
    {
        if (_selectedId >= 0)
        {
            _store.ToggleCompleted(_selectedId);
        }
    }

    private void OpenSelectedInWindow()
    {
        if (_selectedId >= 0)
        {
            OpenTaskInWindow(_selectedId);
        }
    }

    private void OpenTaskInWindow(long id)
    {
        var task = _store.Find(id);
        if (task is null)
        {
            return;
        }
        var window = _application.OpenWindow(
            TaskDetailView.Spec(new TaskDetailProps(_store, id)),
            new GpuiWindowOptions
            {
                Title = $"Task #{id}",
                Width = 480,
                Height = 640,
            }
        );
        window.SetTitle($"Task #{task.Id} — {task.Title}");
    }

    private void CloseInsights() => _dock.ClosePanel("insights");

    private void ReopenPanels()
    {
        _closedPanels.Clear();
        Invalidate();
    }

    private void SyncNow()
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        _syncStart = Stopwatch.GetTimestamp();
        _work.StartLatest(
            this,
            _store.Revision,
            static async (revision, lifetime) =>
            {
                // No Task.Run: the delay never blocks a thread, so produce it inline.
                await Task.Delay(800, lifetime).ConfigureAwait(false);
                return revision;
            },
            static (view, revision) =>
            {
                view._syncing = false;
                view._syncMessage = $"Synced rev {revision} at {DateTime.Now:HH:mm:ss}.";
                view.Invalidate();
            },
            static (view, failure) =>
            {
                view._syncing = false;
                view._syncMessage = $"Sync failed: {failure.Message}";
                view.Invalidate();
            }
        );
        Invalidate();
    }

    private void SelectTask(ListSelectionEvent e)
    {
        if (e.ItemId is not { } id)
        {
            return;
        }
        var next = checked((long)id);
        if (next == _selectedId || _store.Find(next) is null)
        {
            return;
        }
        var previousIndex = IndexOf(_selectedId);
        _selectedId = next;
        var nextIndex = IndexOf(next);
        // Targeted refresh keeps native measurements; the content revision stays stable.
        if (previousIndex >= 0 && nextIndex >= 0)
        {
            _tasks.RefreshRanges((previousIndex, 1), (nextIndex, 1));
        }
        else if (nextIndex >= 0)
        {
            _tasks.Refresh(nextIndex, 1);
        }
        else
        {
            Invalidate();
        }
    }

    private void ActivateTask(ListActivationEvent e)
    {
        if (e.ItemId is { } id)
        {
            OpenTaskInWindow(checked((long)id));
        }
    }

    private void RequestTaskMenu(ListContextMenuEvent request)
    {
        if (request.ItemId > long.MaxValue || _store.Find((long)request.ItemId) is null)
            return;
        _taskMenu = request;
        _taskTooltip = null;
        Invalidate();
    }

    private void RequestTaskTooltip(ListTooltipEvent request)
    {
        if (request.ItemId > long.MaxValue || _store.Find((long)request.ItemId) is null)
            return;
        _taskTooltip = request;
        Invalidate();
    }

    private void RunTaskMenuAction(ulong payload, TaskMenuAction action)
    {
        _taskMenu = null;
        var id = checked((long)payload);
        if (_store.Find(id) is not null)
        {
            switch (action)
            {
                case TaskMenuAction.Open:
                    OpenTaskInWindow(id);
                    break;
                case TaskMenuAction.Duplicate:
                    _store.DuplicateTask(id);
                    break;
                case TaskMenuAction.ToggleComplete:
                    _store.ToggleCompleted(id);
                    break;
                case TaskMenuAction.Delete:
                    RequestDelete(id);
                    break;
            }
        }
        Invalidate();
    }

    private int IndexOf(long id)
    {
        if (id < 0)
        {
            return -1;
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private void OnDockPanelClosed(DockEvent e)
    {
        if (e.Kind == DockEventKind.PanelClosed && e.PanelId.Length != 0)
        {
            _closedPanels.Add(e.PanelId);
            Invalidate();
        }
    }

    // ----- virtual rows (element-only snapshots; no retained resources) -----

    [GpuiListItem]
    private Element TaskRow(int index, ref RenderContext ui)
    {
        var task = _rows[index];
        var theme = ui.Theme;
        var selected = task.Id == _selectedId;
        var statusColor = BoardStyles.StatusColor(task.Status, theme.Colors);
        return ui.Div(
                ui.HStack(
                        ui.TableCell(
                                0,
                                ui.Div(
                                        ui.Text(
                                                $"{(task.Completed ? "✓ " : string.Empty)}{BoardStyles.StatusLabel(task.Status)}"
                                            )
                                            .FontSize(Px(theme.Typography.Caption))
                                            .TextColor(statusColor)
                                    )
                                    .Background(
                                        BoardStyles.StatusBackground(task.Status, theme.Colors)
                                    )
                                    .Padding(Px(4))
                                    .Radius(Px(8))
                            )
                            .PaddingX(Px(8)),
                        ui.TableCell(
                                1,
                                ui.Text(task.Title).FontSize(Px(theme.Typography.BodySmall))
                            )
                            .PaddingX(Px(8))
                            // Keep the popup beside the column, independent of title length.
                            .RowTooltipTarget(),
                        ui.TableCell(
                                2,
                                ui.Text(task.Assignee)
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .PaddingX(Px(8)),
                        ui.TableCell(
                                3,
                                ui.Text($"{task.EstimateHours:0}h")
                                    .FontSize(Px(theme.Typography.Detail))
                                    .TextColor(theme.Colors.TextMuted)
                            )
                            .PaddingX(Px(8)),
                        ui.TableCell(4, PriorityCell(ref ui, task.Priority)).PaddingX(Px(8))
                    )
                    .Width(Percent(100))
            )
            .ItemId(checked((ulong)task.Id))
            .Width(Percent(100))
            .Style(BoardStyles.Row(theme, selected));
    }

    private static Element PriorityCell(ref RenderContext ui, TaskPriority priority)
    {
        var colors = ui.Theme.Colors;
        var color =
            priority == TaskPriority.High ? colors.Error
            : priority == TaskPriority.Medium ? colors.Warning
            : colors.Success;
        return ui.Text(TaskDetailView.PriorityLabelFor(priority))
            .FontSize(Px(ui.Theme.Typography.Detail))
            .TextColor(color);
    }

    [GpuiListItem]
    private Element ActivityRow(int index, ref RenderContext ui)
    {
        var theme = ui.Theme;
        return ui.Div(
                ui.Text(_store.Activity[index])
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .PaddingX(Px(10))
            .PaddingY(Px(3));
    }

    // ----- render -----

    protected override Element Render(ref RenderContext ui)
    {
        ui.Effect(_storeWatch, default);
        ui.Effect(_menus, default);
        var theme = ui.Theme;

        // Signals are read while assembling the memo input, never inside the calculation.
        var rows = _visible.Get(
            new BoardFilter(
                _store,
                _store.Revision,
                _projectId,
                new BoardQuery(_searchText.Value),
                _status,
                _sort,
                _ascending,
                _onlyOpen.Value
            ),
            static input => TaskStore.ApplyFilter(input)
        );
        // Pure projection-cache bookkeeping: content edits retain native cursor identity;
        // membership/order changes reset it in the same accepted snapshot.
        if (!ReferenceEquals(rows, _rows))
        {
            var sameOrder = rows.Count == _rows.Count;
            for (var index = 0; sameOrder && index < rows.Count; index++)
                sameOrder = rows[index].Id == _rows[index].Id;
            if (!sameOrder)
                _projectionRevision = checked(_projectionRevision + 1);
            _rows = rows;
        }

        var content = ui.VStack(
                RenderToolbar(ref ui),
                ui.HStack(RenderSidebar(ref ui), RenderDock(ref ui))
                    .Gap(Px(12))
                    .Grow()
                    .Width(Percent(100)),
                RenderStatusBar(ref ui)
            )
            .Gap(Px(10))
            .Padding(Px(14))
            .Grow()
            .Width(Percent(100))
            .Height(Percent(100))
            .Background(theme.Colors.Background)
            .TextColor(theme.Colors.Text)
            .OnShortcut(
                this,
                new(ShortcutKey.N, ShortcutModifiers.Primary),
                static view => view.OpenNewDialog()
            )
            .OnShortcut(
                this,
                new(ShortcutKey.F, ShortcutModifiers.Primary),
                static view => view.FocusSearch()
            )
            .OnShortcut(
                this,
                new(ShortcutKey.S, ShortcutModifiers.Primary),
                static view => view.SyncNow(),
                new(enabled: !_syncing)
            )
            .OnShortcut(
                this,
                new(ShortcutKey.D, ShortcutModifiers.Primary),
                static view => view.DeleteSelected(),
                new(enabled: _store.Find(_selectedId) is not null)
            );

        if (
            _dialog == BoardDialog.None
            && _taskTooltip is { } tooltipRequest
            && _store.Find((long)tooltipRequest.ItemId) is { } tooltipTask
        )
        {
            content = content.Child(
                ui.RowTooltip(
                    "task-tooltip",
                    tooltipRequest,
                    ui.VStack(
                            ui.Text(tooltipTask.Title).FontWeight(600),
                            ui.Text($"Assignee: {tooltipTask.Assignee}"),
                            ui.Text($"Status: {BoardStyles.StatusLabel(tooltipTask.Status)}"),
                            ui.Text($"Estimate: {tooltipTask.EstimateHours:0.#}h")
                        )
                        .Gap(Px(5))
                        .Padding(Px(12))
                        .Width(Px(300))
                        .Surface(new(theme.Colors.ElevatedSurfaceBackground, theme.Colors.Text))
                        .BorderColor(theme.Colors.Border)
                        .BorderWidth(Px(1))
                        .Radius(Px(8))
                )
            );
        }

        if (
            _dialog == BoardDialog.None
            && _taskMenu is { } request
            && _store.Find((long)request.ItemId) is { } menuTask
        )
        {
            content = content.Child(RenderTaskMenu(ref ui, request, menuTask));
        }

        if (_dialog != BoardDialog.None)
        {
            Element overlay = _dialog switch
            {
                BoardDialog.New => NewTaskDialog(ref ui),
                BoardDialog.Delete => DeleteDialog(ref ui),
                _ => SettingsSheet(ref ui),
            };
            content = ui.VStack(content, overlay).Grow().Height(Percent(100));
        }
        return GpuiTitleBar.RenderWindow(ref ui, "TaskBoard — Team Projects", _menuBar, content);
    }

    private Element RenderToolbar(ref RenderContext ui)
    {
        var theme = ui.Theme;
        return ui.HStack(
                ui.Input(ref _searchBox, new Utf8InputOptions(placeholder: "Search tasks…"u8))
                    .Style(BoardStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.SetSearch(e.Value))
                    .OnSubmitted(this, static (view, e) => view.SetSearch(e.Value))
                    .Grow()
                    .MinWidth(Px(160)),
                ui.Button("clear-search", "Clear")
                    .OnClick(this, static (view, _) => view.ClearSearch())
                    .Style(BoardStyles.Button(theme)),
                StatusFilterMenu(ref ui),
                SortMenu(ref ui),
                ui.Dynamic(_syncing, SyncLabel(ref ui)),
                ui.Tooltip(
                    "tip-new",
                    ui.Button("new-task", "New task")
                        .OnClick(this, static (view, _) => view.OpenNewDialog())
                        .Style(BoardStyles.Button(theme, BoardButtonVariant.Primary)),
                    ui.Text("Create a task (Ctrl/⌘+N)")
                        .FontSize(Px(theme.Typography.Caption))
                        .TextColor(theme.Colors.TitleBarText)
                        .Padding(Px(8))
                        .Background(theme.Colors.TitleBarBackground)
                        .Radius(Px(6))
                ),
                ui.Tooltip(
                    "tip-sync",
                    ui.Button("sync-now", "Sync")
                        .OnClick(this, static (view, _) => view.SyncNow())
                        .Style(BoardStyles.Button(theme)),
                    ui.Text("Simulate background sync (Ctrl/⌘+S)")
                        .FontSize(Px(theme.Typography.Caption))
                        .TextColor(theme.Colors.TitleBarText)
                        .Padding(Px(8))
                        .Background(theme.Colors.TitleBarBackground)
                        .Radius(Px(6))
                ),
                ui.Button(
                        "toggle-theme",
                        theme.Appearance == GpuiThemeAppearance.Dark ? "Light" : "Dark"
                    )
                    .OnClick(this, static (view, _) => view.ToggleTheme())
                    .Style(BoardStyles.Button(theme)),
                ui.Button("open-settings", "Settings")
                    .OnClick(this, static (view, _) => view.ToggleSettings())
                    .Style(BoardStyles.Button(theme))
            )
            .Gap(Px(8))
            .ItemsCenter()
            .Wrap(FlexWrap.Wrap);
    }

    private Element SyncLabel(ref RenderContext ui)
    {
        var theme = ui.Theme;
        if (!_syncing)
        {
            return ui.Text(_syncMessage)
                .FontSize(Px(theme.Typography.Detail))
                .TextColor(theme.Colors.TextMuted);
        }
        var progress = Math.Clamp(Stopwatch.GetElapsedTime(_syncStart).TotalSeconds / 0.8, 0, 1);
        var dots = 1 + (int)(progress * 3);
        var label = dots switch
        {
            1 => "Syncing.",
            2 => "Syncing..",
            3 => "Syncing...",
            _ => "Syncing....",
        };
        return ui.Text(label)
            .FontSize(Px(theme.Typography.Detail))
            .TextColor(theme.Colors.TextAccent);
    }

    private static string StatusLabel(TaskStatus? status) =>
        status is null ? "All statuses" : BoardStyles.StatusLabel(status.Value);

    private Element StatusFilterMenu(ref RenderContext ui)
    {
        var theme = ui.Theme;
        Span<Element> options =
        [
            FilterOption(ref ui, this, theme, null, 0),
            FilterOption(ref ui, this, theme, TaskStatus.Todo, 1),
            FilterOption(ref ui, this, theme, TaskStatus.InProgress, 2),
            FilterOption(ref ui, this, theme, TaskStatus.Review, 3),
            FilterOption(ref ui, this, theme, TaskStatus.Done, 4),
        ];
        return ui.PopoverMenu(
            "status-filter",
            ui.Button("status-filter-trigger", StatusLabel(_status))
                .Style(BoardStyles.Button(theme)),
            ui.VStack(options)
                .Gap(Px(2))
                .Padding(Px(6))
                .Width(Px(200))
                .Background(theme.Colors.ElevatedSurfaceBackground)
                .BorderWidth(Px(1))
                .BorderColor(theme.Colors.Border)
                .Radius(Px(7))
        );
    }

    private static Element FilterOption(
        ref RenderContext ui,
        TaskBoardShellView view,
        GpuiTheme theme,
        TaskStatus? status,
        ulong payload
    )
    {
        var selected = view._status == status;
        return ui.Button(StatusOptionIds[payload], StatusLabel(status))
            .OnClick(view, static (v, e) => v.SetStatusFilter(e.Payload), payload)
            .Style(BoardStyles.Button(theme, BoardButtonVariant.Navigation, selected))
            .Width(Percent(100));
    }

    private Element SortMenu(ref RenderContext ui)
    {
        var theme = ui.Theme;
        Span<Element> options =
        [
            SortOption(ref ui, this, theme, "Title", BoardSort.Title),
            SortOption(ref ui, this, theme, "Priority", BoardSort.Priority),
            SortOption(ref ui, this, theme, "Estimate", BoardSort.Estimate),
        ];
        return ui.PopoverMenu(
            "sort-menu",
            ui.Button("sort-trigger", SortLabel(_sort, _ascending))
                .Style(BoardStyles.Button(theme)),
            ui.VStack(options)
                .Gap(Px(2))
                .Padding(Px(6))
                .Width(Px(200))
                .Background(theme.Colors.ElevatedSurfaceBackground)
                .BorderWidth(Px(1))
                .BorderColor(theme.Colors.Border)
                .Radius(Px(7))
        );
    }

    private static string SortLabel(BoardSort sort, bool ascending) =>
        (sort, ascending) switch
        {
            (BoardSort.Priority, true) => "Priority ↑",
            (BoardSort.Priority, false) => "Priority ↓",
            (BoardSort.Estimate, true) => "Estimate ↑",
            (BoardSort.Estimate, false) => "Estimate ↓",
            (_, true) => "Title ↑",
            (_, false) => "Title ↓",
        };

    private static Element SortOption(
        ref RenderContext ui,
        TaskBoardShellView view,
        GpuiTheme theme,
        string label,
        BoardSort sort
    ) =>
        ui.Button(SortOptionIds[(int)sort], label)
            .OnClick(view, static (v, e) => v.SetSort(e.Payload), (ulong)sort)
            .Style(BoardStyles.Button(theme, BoardButtonVariant.Navigation, view._sort == sort))
            .Width(Percent(100));

    private Element RenderSidebar(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var projects = _store.Projects;
        ProjectBuffer buffer = default;
        var allProjectsStyle = BoardStyles.Button(
            theme,
            BoardButtonVariant.Navigation,
            _projectId == "all"
        );
        buffer[0] = ui.Button(
                "project-all",
                ui.HStack(
                        ui.Text("All projects"),
                        ui.Spacer(),
                        ui.Text($"{_store.Tasks.Count:N0}").Style(allProjectsStyle.SecondaryContent)
                    )
                    .Width(Percent(100))
                    .ItemsCenter()
            )
            .OnClick(this, static (view, e) => view.SelectProject(e.Payload), 0)
            .Style(allProjectsStyle)
            .Width(Percent(100));
        var count = 1;
        var limit = Math.Min(projects.Count, 7);
        for (var i = 0; i < limit; i++)
        {
            var project = projects[i];
            var selected = _projectId == project.Id;
            var projectStyle = BoardStyles.Button(theme, BoardButtonVariant.Navigation, selected);
            buffer[count++] = ui.Button(
                    project.Id,
                    ui.HStack(
                            ui.Text(project.Name),
                            ui.Spacer(),
                            ui.Text($"{_store.CountForProject(project.Id):N0}")
                                .Style(projectStyle.SecondaryContent)
                        )
                        .Width(Percent(100))
                        .ItemsCenter()
                )
                .OnClick(
                    this,
                    static (view, e) => view.SelectProject(e.Payload),
                    checked((ulong)i + 1)
                )
                .Style(projectStyle)
                .Width(Percent(100));
        }
        if (projects.Count > limit)
        {
            buffer[count++] = ui.Text($"…and {projects.Count - limit} more")
                .FontSize(Px(theme.Typography.Detail))
                .TextColor(theme.Colors.TextMuted);
        }
        Span<Element> projectSpan = buffer;
        var projectList = ui.VStack(projectSpan[..count]).Gap(Px(4));

        var sidebar = ui.VStack(
                ui.Text("PROJECTS")
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextMuted),
                projectList,
                ui.Divider(),
                ui.Text("FILTERS")
                    .FontSize(Px(theme.Typography.Caption))
                    .TextColor(theme.Colors.TextMuted),
                ui.Checkbox("filter-open", "Open only")
                    .Checked(_onlyOpen.Value)
                    .OnClick(this, static (view, _) => view.ToggleOnlyOpen())
                    .Padding(Px(6)),
                ui.Button("sidebar-top", "Back to top")
                    .OnClick(this, static (view, _) => view.ScrollSidebarTop())
                    .Style(BoardStyles.Button(theme, BoardButtonVariant.Navigation))
                    .Width(Percent(100)),
                ui.Spacer(),
                ui.Text("Ctrl+N new · Ctrl+F search · Ctrl+D delete")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(8))
            .Padding(Px(12))
            .Width(Px(220))
            .Height(Percent(100))
            .Background(theme.Colors.PanelBackground)
            .TextColor(theme.Colors.Text);

        return ui.Scroll("board-sidebar", ScrollAxis.Vertical, SidebarScrollOptions, sidebar)
            .Width(Px(220))
            .Height(Percent(100))
            .Shrink(0);
    }

    private Element RenderTaskMenu(
        ref RenderContext ui,
        ListContextMenuEvent request,
        TaskItem task
    )
    {
        var theme = ui.Theme;
        return ui.RowContextMenu(
            "tasks-context",
            request,
            ui.VStack(
                    ui.Text(task.Title).FontWeight(600).Padding(Px(6)),
                    ui.Button("ctx-open", "Open in window")
                        .OnClick(
                            this,
                            static (view, e) =>
                                view.RunTaskMenuAction(e.Payload, TaskMenuAction.Open),
                            request.ItemId
                        )
                        .Style(BoardStyles.Button(theme))
                        .Width(Percent(100)),
                    ui.Button("ctx-duplicate", "Duplicate")
                        .OnClick(
                            this,
                            static (view, e) =>
                                view.RunTaskMenuAction(e.Payload, TaskMenuAction.Duplicate),
                            request.ItemId
                        )
                        .Style(BoardStyles.Button(theme))
                        .Width(Percent(100)),
                    ui.Button("ctx-toggle", task.Completed ? "Mark incomplete" : "Mark complete")
                        .OnClick(
                            this,
                            static (view, e) =>
                                view.RunTaskMenuAction(e.Payload, TaskMenuAction.ToggleComplete),
                            request.ItemId
                        )
                        .Style(BoardStyles.Button(theme))
                        .Width(Percent(100)),
                    ui.Button("ctx-delete", "Delete…")
                        .OnClick(
                            this,
                            static (view, e) =>
                                view.RunTaskMenuAction(e.Payload, TaskMenuAction.Delete),
                            request.ItemId
                        )
                        .Style(BoardStyles.Button(theme, BoardButtonVariant.Danger))
                        .Width(Percent(100))
                )
                .Gap(Px(2))
                .Padding(Px(6))
                .Width(Px(260))
                .Surface(new(theme.Colors.ElevatedSurfaceBackground, theme.Colors.Text))
                .BorderWidth(Px(1))
                .BorderColor(theme.Colors.Border)
                .Radius(Px(8))
        );
    }

    private Element RenderDock(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var table = ui.Table(
                ref _tasks,
                new ListDataSource(_rows.Count, _tableRevision, _projectionRevision),
                Rows.TaskRow,
                TasksTableOptions,
                TaskColumns
            )
            .Header(
                ui.Text("Status").TextColor(theme.Colors.Text),
                SortHeader(ref ui, this, theme, BoardSort.Title),
                ui.Text("Assignee").TextColor(theme.Colors.Text),
                SortHeader(ref ui, this, theme, BoardSort.Estimate),
                SortHeader(ref ui, this, theme, BoardSort.Priority)
            )
            .OnSelectionRequested(this, static (view, e) => view.SelectTask(e))
            .OnActivated(this, static (view, e) => view.ActivateTask(e))
            .OnContextMenuRequested(this, static (view, e) => view.RequestTaskMenu(e))
            .OnTooltipRequested(
                this,
                static (view, e) => view.RequestTaskTooltip(e),
                TaskTooltipOptions
            )
            .Grow()
            .Width(Percent(100))
            .Style(BoardStyles.Table(theme));

        var tasksContent = ui.VStack(table).Grow().Width(Percent(100)).Height(Percent(100));

        var tasksPanel = ui.DockPanel(
            "tasks",
            "Tasks",
            tasksContent,
            new DockPanelOptions(closable: false)
        );

        Element center;
        if (_closedPanels.Contains("insights"))
        {
            center = ui.DockTabs(0, [tasksPanel]);
        }
        else
        {
            var insightsPanel = ui.DockPanel(
                "insights",
                "Insights",
                ui.Child("insights", InsightsView.Spec(new InsightsProps(_store, _store.Revision)))
            );
            center = ui.DockTabs(0, [tasksPanel, insightsPanel]);
        }

        var activityList = ui.List(
                ref _activity,
                new ListDataSource(_store.Activity.Count, _store.Revision),
                Rows.ActivityRow,
                ActivityListOptions
            )
            .Grow()
            .Width(Percent(100));
        var bottom = ui.DockRegion(
                DockSide.Bottom,
                ui.DockTabs(
                    0,
                    [
                        ui.DockPanel(
                            "activity",
                            "Activity",
                            activityList,
                            new DockPanelOptions(closable: false)
                        ),
                    ]
                )
            )
            .InitialSize(170);

        Element<DockAreaTag> dock;
        if (_closedPanels.Contains("detail"))
        {
            RegionBuffer single = default;
            single[0] = bottom;
            Span<Element> singleSpan = single;
            dock = ui.DockArea(ref _dock, "board-dock", center, singleSpan[..1]);
        }
        else
        {
            var right = ui.DockRegion(
                    DockSide.Right,
                    ui.DockTabs(
                        0,
                        [
                            ui.DockPanel(
                                "detail",
                                "Details",
                                ui.Child(
                                    "detail",
                                    TaskDetailView.Spec(new TaskDetailProps(_store, _selectedId))
                                )
                            ),
                        ]
                    )
                )
                .InitialSize(340);
            RegionBuffer regions = default;
            regions[0] = right;
            regions[1] = bottom;
            Span<Element> regionSpan = regions;
            dock = ui.DockArea(ref _dock, "board-dock", center, regionSpan);
        }

        return dock.OnDockPanelClosed(this, static (view, e) => view.OnDockPanelClosed(e))
            .Grow()
            .Height(Percent(100));
    }

    private static Element SortHeader(
        ref RenderContext ui,
        TaskBoardShellView view,
        GpuiTheme theme,
        BoardSort sort
    )
    {
        var active = view._sort == sort;
        // Short constant labels: a full-size button recipe overflows narrow columns.
        var title = (sort, active, view._ascending) switch
        {
            (BoardSort.Priority, true, true) => "Prio ↑",
            (BoardSort.Priority, true, false) => "Prio ↓",
            (BoardSort.Estimate, true, true) => "Est ↑",
            (BoardSort.Estimate, true, false) => "Est ↓",
            (BoardSort.Title, true, true) => "Task ↑",
            (BoardSort.Title, true, false) => "Task ↓",
            (BoardSort.Priority, _, _) => "Prio",
            (BoardSort.Estimate, _, _) => "Est",
            _ => "Task",
        };
        return ui.Button(SortHeaderIds[(int)sort], title)
            .OnClick(view, static (v, e) => v.SetSort(e.Payload), (ulong)sort)
            .Style(BoardStyles.HeaderButton(theme, active));
    }

    private Element RenderStatusBar(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var open = 0;
        foreach (var task in _store.Tasks)
        {
            if (!task.Completed)
            {
                open++;
            }
        }
        return ui.HStack(
                ui.Text(
                        $"{_rows.Count:N0} shown · {_store.Tasks.Count:N0} total · {open:N0} open · rev {_store.Revision:N0}"
                    )
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Spacer(),
                ui.Text(_selectedId < 0 ? "No selection" : $"Selected #{_selectedId}")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextAccent),
                ui.Button("status-close-insights", "Close Insights")
                    .OnClick(this, static (view, _) => view.CloseInsights())
                    .Style(BoardStyles.Button(theme))
            )
            .Gap(Px(10))
            .ItemsCenter();
    }

    private Element NewTaskDialog(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var projects = _store.Projects;
        ProjectBuffer buffer = default;
        var count = 0;
        var limit = Math.Min(projects.Count, NewProjectIds.Length);
        for (var i = 0; i < limit; i++)
        {
            var index = i;
            var selected = _newProject == index + 1;
            buffer[count++] = ui.Radio(NewProjectIds[index], ui.Text(projects[index].Name))
                .Checked(selected)
                .OnClick(
                    this,
                    static (view, e) => view.SetNewProject(e.Payload),
                    checked((ulong)index + 1)
                )
                .Padding(Px(4));
        }
        Span<Element> projectSpan = buffer;
        var projectRadios = ui.HStack(projectSpan[..count]).Gap(Px(6)).Wrap(FlexWrap.Wrap);

        Span<Element> statusRadios =
        [
            NewStatusRadio(ref ui, this, theme, TaskStatus.Todo),
            NewStatusRadio(ref ui, this, theme, TaskStatus.InProgress),
            NewStatusRadio(ref ui, this, theme, TaskStatus.Review),
            NewStatusRadio(ref ui, this, theme, TaskStatus.Done),
        ];
        Span<Element> priorityRadios =
        [
            NewPriorityRadio(ref ui, this, theme, TaskPriority.Low),
            NewPriorityRadio(ref ui, this, theme, TaskPriority.Medium),
            NewPriorityRadio(ref ui, this, theme, TaskPriority.High),
        ];

        var panel = ui.VStack(
                ui.Text("New task")
                    .FontSize(Px(theme.Typography.Heading))
                    .TextColor(theme.Colors.Text),
                ui.Input("new-title", new InputOptions(placeholder: "What needs doing?"))
                    .Style(BoardStyles.Field(theme, _newError.Length != 0 && _newTitle.Length == 0))
                    .OnChanged(this, static (view, e) => view.SetNewTitle(e.Value))
                    .OnSubmitted(this, static (view, _) => view.CreateTask())
                    .Width(Percent(100)),
                ui.Text("Project")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                projectRadios,
                ui.Text("Status")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(statusRadios).Gap(Px(6)).Wrap(FlexWrap.Wrap),
                ui.Text("Priority")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(priorityRadios).Gap(Px(6)),
                ui.Input("new-assignee", new InputOptions(placeholder: "Assignee"))
                    .Style(BoardStyles.Field(theme))
                    .OnChanged(this, static (view, e) => view.SetNewAssignee(e.Value))
                    .Width(Percent(100)),
                ui.Text(_newError)
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.Error),
                ui.HStack(
                        ui.Spacer(),
                        ui.Button("new-cancel", "Cancel")
                            .OnClick(this, static (view, _) => view.CancelDialog())
                            .Style(BoardStyles.Button(theme)),
                        ui.Button("new-create", "Create task")
                            .OnClick(this, static (view, _) => view.CreateTask())
                            .Style(BoardStyles.Button(theme, BoardButtonVariant.Primary))
                    )
                    .Gap(Px(8))
                    .ItemsCenter()
            )
            .Gap(Px(10))
            .Padding(Px(20))
            .Width(Px(460))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(14));

        return ui.Dialog(
                "new-task-dialog",
                panel,
                new OverlayOptions(margin: 24, backdrop: theme.Colors.Background.WithAlpha(150))
            )
            .OnShortcut(
                this,
                new(ShortcutKey.Enter, ShortcutModifiers.Primary),
                static view => view.CreateTask()
            )
            .OnDismiss(this, static (view, _) => view.CancelDialog());
    }

    private void SetNewTitle(string value) => _newTitle = value;

    private void SetNewAssignee(string value) => _newAssignee = value;

    private static Element NewStatusRadio(
        ref RenderContext ui,
        TaskBoardShellView view,
        GpuiTheme theme,
        TaskStatus status
    ) =>
        ui.Radio(NewStatusIds[(int)status], ui.Text(BoardStyles.StatusLabel(status)))
            .Checked(view._newStatus == status)
            .OnClick(view, static (v, e) => v.SetNewStatus(e.Payload), (ulong)status)
            .Padding(Px(4));

    private static Element NewPriorityRadio(
        ref RenderContext ui,
        TaskBoardShellView view,
        GpuiTheme theme,
        TaskPriority priority
    ) =>
        ui.Radio(NewPriorityIds[(int)priority], ui.Text(TaskDetailView.PriorityLabelFor(priority)))
            .Checked(view._newPriority == priority)
            .OnClick(view, static (v, e) => v.SetNewPriority(e.Payload), (ulong)priority)
            .Padding(Px(4));

    private Element DeleteDialog(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var task = _store.Find(_deleteTarget);
        var panel = ui.VStack(
                ui.Text("Delete task?")
                    .FontSize(Px(theme.Typography.Heading))
                    .TextColor(theme.Colors.Text),
                ui.Text(
                        task is null
                            ? "The task is already gone."
                            : $"“{task.Title}” will be removed."
                    )
                    .FontSize(Px(theme.Typography.BodySmall))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(
                        ui.Spacer(),
                        ui.Button("delete-cancel", "Keep")
                            .OnClick(this, static (view, _) => view.CancelDialog())
                            .Style(BoardStyles.Button(theme)),
                        ui.Button("delete-confirm", "Delete")
                            .OnClick(this, static (view, _) => view.ConfirmDelete())
                            .Style(BoardStyles.Button(theme, BoardButtonVariant.Danger))
                    )
                    .Gap(Px(8))
                    .ItemsCenter()
            )
            .Gap(Px(12))
            .Padding(Px(20))
            .Width(Px(400))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(14));

        return ui.Dialog(
                "delete-task-dialog",
                panel,
                new OverlayOptions(margin: 24, backdrop: theme.Colors.Background.WithAlpha(150))
            )
            .OnDismiss(this, static (view, _) => view.CancelDialog());
    }

    private Element SettingsSheet(ref RenderContext ui)
    {
        var theme = ui.Theme;
        var panel = ui.VStack(
                ui.HStack(
                        ui.Text("Board settings")
                            .FontSize(Px(theme.Typography.Heading))
                            .TextColor(theme.Colors.Text),
                        ui.Spacer(),
                        ui.Button("settings-close", "Close")
                            .OnClick(this, static (view, _) => view.ToggleSettings())
                            .Style(BoardStyles.Button(theme))
                    )
                    .ItemsCenter(),
                ui.Text("Theme")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.HStack(
                        ui.Radio("settings-light", ui.Text("Light"))
                            .Checked(theme.Appearance == GpuiThemeAppearance.Light)
                            .OnClick(this, static (view, _) => view.SetLightTheme())
                            .Padding(Px(6)),
                        ui.Radio("settings-dark", ui.Text("Dark"))
                            .Checked(theme.Appearance == GpuiThemeAppearance.Dark)
                            .OnClick(this, static (view, _) => view.SetDarkTheme())
                            .Padding(Px(6))
                    )
                    .Gap(Px(6)),
                ui.Checkbox("settings-open-only", "Show open tasks only")
                    .Checked(_onlyOpen.Value)
                    .OnClick(this, static (view, _) => view.ToggleOnlyOpen())
                    .Padding(Px(6)),
                ui.HStack(
                        ui.Button("settings-reopen", "Restore closed panels")
                            .OnClick(this, static (view, _) => view.ReopenPanels())
                            .Style(BoardStyles.Button(theme)),
                        ui.Button("settings-sync", "Sync now")
                            .OnClick(this, static (view, _) => view.SyncNow())
                            .Style(BoardStyles.Button(theme, BoardButtonVariant.Primary))
                    )
                    .Gap(Px(8))
                    .Wrap(FlexWrap.Wrap),
                ui.Divider(),
                ui.Text("Shortcuts")
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted),
                ui.Text(
                        "Ctrl+N new · Ctrl+F search · Ctrl+S sync · Ctrl+D delete · Enter opens selection"
                    )
                    .FontSize(Px(theme.Typography.Detail))
                    .TextColor(theme.Colors.TextMuted)
            )
            .Gap(Px(12))
            .Padding(Px(20))
            .Width(Px(380))
            .Height(Percent(100))
            .Background(theme.Colors.SurfaceBackground)
            .BorderWidth(Px(1))
            .BorderColor(theme.Colors.Border)
            .Radius(Px(14));

        return ui.Sheet("board-settings", panel, SheetSide.Right)
            .OnDismiss(this, static (view, _) => view.ToggleSettings());
    }

    private void SetLightTheme() => _application.SetTheme(TaskBoardThemes.Light);

    private void SetDarkTheme() => _application.SetTheme(TaskBoardThemes.Dark);
}
