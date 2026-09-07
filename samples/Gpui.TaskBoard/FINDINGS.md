# TaskBoard findings — API, functionality, and documentation gaps

Built while writing `samples/Gpui.TaskBoard`. Each item was hit directly; severity
is judged by how much application code it took to work around.

## What worked well

- One `View<TProps>` (`TaskDetailView`) serves as both an embedded Dock child and
  an independent window root from the same `Spec`. No duplication.
- Accepted effects commanding retained resources pre-materialization (`SyncInputs`
  pushing title/assignee/estimate into a freshly selected task) solved the
  per-task input identity problem with zero render-time key management.
- Static handlers + `ulong` click payloads, `RefreshRanges` selection updates, and
  `contentRevision` discipline kept all `Render` paths heap-free without friction.
- `GpuiTitleBar.RenderWindow` + effect-installed `GpuiMenu[]` covered the full
  menu/title story in ~30 lines.

## Gaps

1. **No controlled-input helper.** Live-committing every keystroke (`RenameTask`
   on `OnChanged`) bumps the store revision and evicts table batches per keystroke.
   Draft-then-commit needs hand-rolled per-task draft state. A revision-aware
   binding helper (see `docs/NEXT_STEPS.md` input section) would remove the
   tradeoff. Workaround in sample: live commit + `SetValueIfCurrent`-style
   discipline documented in code; acceptable at 180 rows, wasteful at scale.
2. **No row-anchored menus/tooltips.** Virtual rows forbid deferred layers, so the
   table uses one collection-level `ContextMenu` acting on selection instead of
   per-row menus. Window-owned overlays anchored to stable item identities are
   roadmap work; the sample cannot do "right-click this exact row".
3. **Single-selection only.** `OnSelectionRequested`/`OnActivated` cover the
   single-row policy; range/multi-select, anchors, and modifier policies are
   application code. Fine for this sample, but a second sample needing them
   starts from zero.
4. **No column resize/reorder/visibility.** `TableColumn` is declaration-only;
   hiding the estimate column on narrow windows required no API, but letting the
   *user* resize or reorder columns has none. Roadmap already lists this.
5. **No toast/notification primitive.** Sync completion lands in the status bar
   because there is nowhere else transient to put it. Roadmap lists toasts.
6. **Per-collection revision scoping is entirely application-owned.** The task
   table and the activity log share `store.Revision`, so a title keystroke evicts
   both caches. Splitting revisions per collection is possible but manual, and
   nothing warns when a `contentRevision` is too coarse or too fine.
7. **Dock tab activation is declarative-only.** There is no controller operation
   to activate the Insights tab after creating a task (to show its effect);
   `activeIndex` must be driven from state. Roadmap notes the missing
   node-stable handle.
8. **Effect input equality with mutable stores.** `TaskDetailProps` carries a
   mutable `TaskStore` reference inside an equatable props struct; equality is
   reference-based by luck of `EqualityComparer<T>.Default`. A `[GpuiView]`
   diagnostic or documented pattern for "reference + revision" props would help.
9. **Observer key names are undiscoverable.** `KeyEvent.Matches` string names
   (e.g. `"n"`, `"Delete"`) have no in-IDE enumeration in the sample's reach;
   Ctrl+D was chosen over the Delete key partly to avoid guessing the name.
   A documented key-name table would remove the guesswork.
10. **No accessibility names for icon/compact controls.** The toolbar buttons are
    text today; compact icon buttons would need explicit accessible names and
    field relationships that the current contract does not surface (roadmap).

## Documentation nits found

- A virtualized Table (or List) inside a `ContextMenu`/`PopoverMenu` trigger needs
  an explicit fill chain: style the trigger host (`Grow` + `Width`/`Height` 100%)
  and wrap the collection in a `VStack` so `Grow` yields a real rows viewport.
  Without it the table collapses to its header strip (headers pile up, zero rows)
  while a fixed-size trigger (as in the overlay gallery) works. This fill-through-
  wrappers rule deserves a callout in `COMPONENTS.md`.
- Fixed rails (sidebar) need `Shrink(0)` or flex overflow squeezes them below
  their declared width; the dock then inherits the mess. Worth one sentence in
  the styling docs.
- The interplay "selection refresh vs. revision bump" (`RefreshRanges` keeps
  measurements, revision evicts) is documented under List but deserves a
  table-level callout: tables hit it on every sort/filter/selection change.
- `DockPanel` content accepting both child Views *and* nested retained resources
  (table + inputs inside dock panels) worked first try, but only `COMPONENTS.md`
  mentions it in one sentence; a sample reference (this one) should be linked.
