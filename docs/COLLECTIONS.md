# Scroll and virtual collections

Scroll, List, and Table retain viewport state natively. Virtual rows are batched element snapshots,
not mounted child Views. See [Components](COMPONENTS.md) for the authoring model.

Native ownership is split between two modules. `crates/gpui-dotnet/src/collections/` owns
collection behavior: `engine.rs` retains `ListState`, row batches, measurements, and revision
reconciliation; `cursor.rs` owns the active index and epoch rules; `configuration.rs` decodes
List/Table declarations and table columns; `events.rs` delivers activation/selection requests;
`registry.rs` owns which List/Table engines exist, their pre-declaration command queue, and
artifact-sorting for invalidation. `ResourceStore` (`resources.rs`) coordinates retained
resources: lookup by identity, creation with accepted configuration, command routing, theme and
artifact invalidation, presence publication, and retirement. List and Table share one collection
engine; only their column metadata differs.

## Retained Scroll

`ui.Scroll` declares content, axis, smooth-scrolling behavior, and scrollbar options. Rust retains
the `ScrollHandle` and consumes wheel and trackpad input. Foundation `Scrollbar` owns track/thumb
painting, hit testing, track clicks, and dragging. Managed code is not notified for scroll deltas.

Use `ScrollController` only for imperative operations such as `ScrollTo`, `ScrollToTop`, and
`ScrollToBottom`. A growing scroll viewport should be inside a parent that can shrink; the native
materializer applies the required minimum-height behavior to growing flex items.

The adapter preserves semantic scrollbar width, optional gutter placement, stable identity, and
the existing native smoothing of discrete wheel deltas. List and Table seed GPUI `ListState` with
the declared estimated item height, so the same foundation scrollbar can use the native list
handle and still target unmeasured virtual rows without managed calls. A native maintenance layer
restores those hints after width changes before the scrollbar reads the range. Precise trackpad
deltas remain on GPUI's direct scroll path.

## Retained List

`ui.List` combines:

- a stable resource key or ref-bound `ListController`;
- `ListDataSource(count, contentRevision)`;
- a source-generated `[GpuiListItem]` renderer token;
- viewport, batching, alignment, estimated-height, and scrollbar options.

Rust owns `ListState`, estimated heights for unmeasured rows, actual visible-row measurements,
active-row keyboard navigation, and up to four aligned row batches. The managed renderer returns
one synthetic root containing exactly the requested number of row roots. Actual measurements
replace their hints, allowing native scroll geometry to converge without measuring the full list.

Page Up/Down scroll by the current row viewport height, clamped to the native scroll range.
Paging preserves partial-row offsets, including within a row taller than the viewport, and places
the native keyboard cursor on the first visible row. It uses the current viewport even when wheel
scrolling has moved the old cursor offscreen. Up/Down then move that cursor by one row; Home/End
move it to the first/last row and reveal it. Keyboard navigation cancels pending wheel smoothing.
Paging uses measured heights and estimates for unseen rows without requesting managed rows in the
key handler. Hidden or not-yet-laid-out viewports do not page.

The active cursor is a native navigation position, separate from application selection and row
activation. Left mouse-down on a visible row updates it and focuses the collection before child
handlers run; children may still take focus or consume the event. Existing row/child click bindings
remain intact. Arrow and paging keys do not synthesize clicks or change application selection.
Applications continue to own selected-item state and selected-row styling.

Bind `.OnSelectionRequested(view, static (owner, e) => owner.SelectItem(e))` on List or Table for
single-row selection requests. An unmodified primary single press requests its row unless a child
consumes mouse-down. Unmodified Space requests the native cursor when the collection itself has
focus; held repeats and keys intended for text input do not request selection. Enter and double
press remain activation gestures. Modified presses, range selection, toggling, and selection that
follows arrow navigation have no built-in policy.

`ListSelectionEvent` carries `Index`, optional row-root `ItemId`, optional `ContentRevision`, and
`Source` (`Pointer` or `Keyboard`). The request does not update native selection state. Applications
may accept or ignore it; resolve `ItemId` against current data before acting on a retained event
whose revision is stale. Updating application selection independently does not move the native
cursor or emit an event. Row and child click bindings still run independently, so avoid binding the
same selection update to both a row click and the collection request.

Render accepted selection through application-owned styles. The table sample uses
`SampleStyles.CollectionRow(theme, selected)`, an `IGpuiElementStyle<DivTag>` recipe using the
theme's selected background, border, and inherited text roles. Its rows are plain containers, so
presses leave focus on the table for Space and Enter. After a change, refresh the old and new row
ranges through `ListController.RefreshRanges`; that also invalidates the owner so header selection
labels update. Keep content revision stable for these targeted refreshes. Theme changes refresh
row batches automatically. This presentation needs no retained View per row or native selection
store, and application code can choose its own colors, indicators, and sizing.

Bind `.OnActivated(view, static (owner, e) => owner.OpenItem(e))` on List or Table to opt into
activation. Unmodified Enter activates the native cursor when the collection itself has focus;
held repeats and keys intended for text input do not activate. An unmodified primary-button double
press activates its row unless a child consumes mouse-down. Focused child controls keep their own
Enter behavior. Activation does not synthesize clicks or change selection, and ordinary row click
bindings still run independently.

`ListActivationEvent` carries `Index`, optional row-root `ItemId`, optional `ContentRevision`, and
`Source` (`Pointer` or `Keyboard`) from the accepted datasource. Revision zero is distinct from an
absent revision. Keyboard activation or selection may request one aligned row batch to resolve an uncached row's
identity; navigation alone does not. The event owns its scalar data, and callbacks run through the
normal View event boundary after native resource borrows are released.

Keep `contentRevision` stable when a managed render cannot change any row output. Increment it when
row content, styling, or height can change. Theme changes invalidate batches automatically.

The revision must also cover filter and sort inputs: changing to a different projection with
the same item count still requires invalidation. Refreshing a range does not recompute a managed
memo or replace immutable records held by a row source. Update that source before issuing a
targeted refresh. `RefreshRanges` invalidates the owning View, not other store subscribers; Rust
discards intersecting cached batches and remeasures the affected items, not necessarily one row.

Native adapters that use `gpui-base` read the same application theme through the projected global
foundation theme. Product variants still flatten into semantic operations; they do not become
foundation theme types or cross the ABI.

Row renderer restrictions:

- synchronous and side-effect-free;
- elements only—no managed child views;
- no nested Scroll, List, Table, Input, Slider, or deferred layer;
- at most 512 rows in one native request;
- event payloads should use an index or stable model ID rather than per-row closures.

Declare `.ItemId(id)` on a row root when interactive state should survive structural splices. ID
zero is reserved. An `OnClick` binding without an explicit payload receives that model ID.

Child element keys are scoped beneath their row root, so row-local keys such as `"like"` may
repeat across distinct rows. Use stable row identity and event payloads for application actions.

`ListController` supports `ScrollToItem`, `Refresh`, `RefreshRanges`, `Splice`, and `Reset`.
Structural commands preserve unaffected measurements and row batches when their declared result
matches the next managed snapshot.

The cursor belongs to the retained List/Table resource, so cache eviction, content refresh, theme
changes, and layout-only rebuilds do not reset it. Accepted `Splice` hints also move it with surviving
items: inserting/removing earlier rows shifts its index. Removing the active item chooses the first
replacement or successor at the splice start, falling back to the final row. An empty list has no
active cursor; inserting into it starts at the first row. `Reset`, a count change without matching
hints, or invalid hints reset it to the first row. Pointer/keyboard handlers from a superseded row
declaration cannot change the new cursor before the next paint.

`ItemId` preserves element state and event payload identity; it is not a complete native datasource
index. Native cursor preservation follows valid splices and does not search unseen rows for a moved
ID. Use `Reset` for arbitrary reorder/replacement whose identity cannot be expressed by surviving
splice ranges. A remove-then-insert sequence treats the removed active item as deleted.

For declarative projection replacement, pass `projectionRevision` as the optional third argument
to `ListDataSource(count, contentRevision, projectionRevision)`. Changing this stamp resets cursor,
scroll position, measurements, and row batches in the same accepted snapshot, even when count and
content revision are unchanged. Adding or removing the stamp also resets an existing resource;
zero is valid. Omission preserves the existing command-based contract. A projection change
overrides all queued positional commands, including splices and scroll requests for the old order.
Keep the stamp stable for content-only edits and identity changes described by valid splices.
Applications own the stamp and model selection; native code does not build an ID-to-index map.

## Retained Table

`ui.Table` uses the List row engine and adds declarative `TableColumn[]` metadata. Rust materializes
the header and applies the same column widths and alignment to `ui.TableCell(column, ...)` nodes.

`.Header(...)` supplies one normal managed content element per column, in declaration order. Call it
once; no header children means the native strip uses `TableColumn.Header` labels. Both validators
reject a nonzero header count that differs from the number of columns. Header content supports
ordinary buttons, icons, typed styles, and View composition; it is outside virtual row snapshots.
The native strip preserves column widths/alignment and scrollbar gutter, with a minimum height of
32 pixels that grows for taller content. `TableOptions(showHeader: false)` hides either header form.
Header controls own their focus and keyboard activation; table navigation runs only while the table
itself has focus.

`HeaderBackground`, `HeaderTextColor`, and `HeaderBorderColor` compose with
`IGpuiElementStyle<TableTag>`. Background and the one-pixel bottom border span the full strip,
including its scrollbar gutter; header cells still exclude the gutter to align with rows. Header
text color is inherited by both column labels and custom content, while explicit child colors win.
Omitted colors use the current theme's element background, muted text, and border variant. The last
declaration for each color wins and preserves alpha; omitting an override on a later render restores
the theme default. Header paint is separate from column metadata, so changing it does not invalidate
row batches or reset the retained cursor or scroll position.

Sorting belongs to the application: a header button changes model order and content revision, then
calls `ListController.Reset(count)` for an arbitrary reorder. Keep selection by model identity.
Header content is separate from the column metadata used to reconcile row layout. See the
[Table sample](../samples/Gpui.Sample/Views/TableView.cs) for a sortable service header and stable
selection, and the [Input sample](../samples/Gpui.Sample/Views/InputGalleryView.cs) and
[sample styles](../samples/Gpui.Sample/SampleStyles.cs) for composed fields with help/error text.

Rows keep List semantics, including batching, model identity, keyboard navigation, refresh, and
splice behavior. A changed column declaration invalidates row batches because cell layout changes.
Page Up/Down use the row viewport below the header, so header height is excluded from a page.
Managed row content should use a horizontal container; Table does not infer a row layout from plain
Div children.
