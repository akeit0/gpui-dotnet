# TaskBoard — Team Project Tracker

A practical GPUI.NET application sample: a team task board with projects, a virtual
task table, an inspector, insights, an activity log, dialogs, a settings sheet, menus,
and background sync. It is the counterpart to `Gpui.Sample`: where the gallery shows
one component per page, TaskBoard composes them the way a shipped app would.

Run from the repository root:

```sh
dotnet run --project samples/Gpui.TaskBoard
dotnet run --project samples/Gpui.TaskBoard -- --dark
```

## Tour

- **Sidebar** — retained `Scroll` with project buttons (live counts), an "Open only"
  `Checkbox`, and a Back-to-top `ScrollController` command.
- **Toolbar** — retained search `Input` (focus it with Ctrl+F), status/sort
  `PopoverMenu` filters, `Tooltip` buttons, a `Dynamic` sync indicator, theme toggle,
  and the settings `Sheet`.
- **Tasks tab** — virtual `Table` (180 seeded rows) with sortable header buttons,
  single-selection requests, double-click/Enter activation, and a right-click
  `ContextMenu` (open, duplicate, toggle, delete).
- **Insights tab** — retained child `View<InsightsProps>` with a memo-cached
  summary, a flex distribution bar, and a native vector `Drawing` chart.
- **Details pane** — retained child `View<TaskDetailProps>` in a collapsible Dock
  right region: title/assignee inputs that commit on Enter, status buttons, priority
  radios, a retained estimate `Slider`, file-drop attachments, a `WorkScope`
  estimate suggestion, and open-in-window.
- **Activity region** — second virtual `List` (bottom Dock region) fed by the same
  store revision, proving two row engines coexist on one View.
- **Dialogs** — modal new-task and delete-confirmation `Dialog` overlays with
  validation, all dismissed natively via backdrop/Escape plus `OnDismiss`.
- **Hot keys** — root `OnKeyDown` observer: Ctrl+N, Ctrl+F, Ctrl+S, Ctrl+D.
  Focused inputs win first; the board only observes the rest.
- **Menus/title bar** — `GpuiMenu[]` installed from an accepted effect and rendered
  through `GpuiTitleBar.RenderWindow` (native chrome on macOS).

## Architecture notes

- `Models/TaskStore.cs` owns all domain state, a monotonic `Revision`, and a
  subscribe/notify contract. Views never own task data.
- `TaskBoardShellView` owns filter state, selection, dialogs, controllers, two
  `Signal`s, one `Memo<BoardFilter, List<TaskItem>>`, two effects (store watch,
  menu install), and one `WorkScope` (sync). Table cache validity is explicit:
  store mutations bump the revision through the subscription, local filter edits
  bump it in the handler, and selection changes use targeted `RefreshRanges`
  without touching the revision.
  A separate projection revision compares stable row IDs after memo rebuilds, resetting the native
  cursor and scroll position for filter/reorder changes while preserving them for content edits.
- `TaskDetailView` doubles as an embedded child and an independent window root
  from the same `Spec`, sharing the live store. Native input/slider values are
  synchronized from accepted immutable field snapshots through an effect (`SyncInputs`).
  Unrelated changes preserve local drafts; an external change to the same field wins.
  Selecting another task discards uncommitted drafts. Estimate work snapshots the target
  and rejects results after intervening model edits.
- Render code uses allocation-conscious mechanisms: ref-bound controllers, static
  event handlers with `ulong` payloads, stack-span collection expressions, one
  small inline buffer for the variable-length project/attachment lists, static
  option snapshots, and arena-direct interpolated text. See `FINDINGS.md` for
  what the framework made easy and what is still missing. Projection rebuilds and
  capturing lookup predicates still allocate; no end-to-end zero-allocation claim is made.
