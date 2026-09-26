# Scroll and virtual collections

Scroll, List, and Table retain viewport state natively. Virtual items are batched element snapshots,
not mounted child Views. See [Components](COMPONENTS.md) for the authoring model.

The optional [component Tree](EXTENSIONS.md) also virtualizes visible rows natively. It receives
one bounded preorder metadata batch rather than per-row managed render callbacks. Native expansion
and keyboard cursor state survive ordinary managed renders; application selection remains a
separate stable ID. This local-data contract does not replace List/Table's batched datasource for
large or remote collections.

Native ownership is split between two modules. `crates/gpui-dotnet/src/collections/` owns
collection behavior: `engine.rs` retains `ListState`, item batches, measurements, and revision
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

Use `ScrollController` only for imperative operations such as `ScrollTo`, `ScrollToTop`,
`ScrollToBottom`, `ScrollToLeft`, and `ScrollToRight`. `ScrollToLeft` moves to the left edge
while preserving the vertical offset; `ScrollToRight` moves to the measured right edge. A growing scroll viewport should be inside a parent that can shrink; the native
materializer applies the required minimum-height behavior to growing flex items.

The adapter preserves semantic scrollbar width, optional gutter placement, stable identity, and
the existing native smoothing of discrete wheel deltas. List and Table seed GPUI `ListState` with
the declared estimated item height, so the same foundation scrollbar can use the native list
handle and still target unmeasured virtual items without managed calls. A native maintenance layer
restores those hints after width changes before the scrollbar reads the range. Precise trackpad
deltas remain on GPUI's direct scroll path.

## Retained List

`ui.List` combines:

- a stable resource key or ref-bound `ListController`;
- `ListDataSource(count, contentRevision)`;
- a source-generated `[GpuiListItem]` renderer token;
- viewport, batching, alignment, estimated-height, and scrollbar options.

Rust owns `ListState`, estimated heights for unmeasured items, actual visible-item measurements,
active-item keyboard navigation, and up to four aligned item batches. The managed renderer returns
one synthetic root containing exactly the requested number of item roots. Actual measurements
replace their hints, allowing native scroll geometry to converge without measuring the full list.

Page Up/Down scroll by the current viewport height, clamped to the native scroll range.
Paging preserves partial-item offsets, including within an item taller than the viewport, and places
the native keyboard cursor on the first visible item. It uses the current viewport even when wheel
scrolling has moved the old cursor offscreen. Up/Down then move that cursor by one item; Home/End
move it to the first/last item and reveal it. Keyboard navigation cancels pending wheel smoothing.
Paging uses measured heights and estimates for unseen items without requesting managed items in the
key handler. Hidden or not-yet-laid-out viewports do not page.

The active cursor is a native navigation position, separate from application selection and item
activation. Left mouse-down on a visible item updates it and focuses the collection before child
handlers run; children may still take focus or consume the event. Existing item/child click bindings
remain intact. Arrow and paging keys do not synthesize clicks or change application selection.
Applications continue to own selected-item state and selected-item styling.

Bind `.OnSelectionRequested(view, static (owner, e) => owner.SelectItem(e))` on List or Table for
single-item selection requests. An unmodified primary single press requests its item unless a child
consumes mouse-down. Unmodified Space requests the native cursor when the collection itself has
focus; held repeats and keys intended for text input do not request selection. Enter and double
press remain activation gestures. Modified presses, range selection, toggling, and selection that
follows arrow navigation have no built-in policy.

`ListSelectionEvent` carries `Index`, optional item-root `ItemId`, optional `ContentRevision`, and
`Source` (`Pointer` or `Keyboard`). The request does not update native selection state. Applications
may accept or ignore it; resolve `ItemId` against current data before acting on a retained event
whose revision is stale. Updating application selection independently does not move the native
cursor or emit an event. Item and child click bindings still run independently, so avoid binding the
same selection update to both an item click and the collection request.

Render accepted selection through application-owned styles. The table sample uses
`SampleStyles.CollectionItem(theme, selected)`, an `IGpuiElementStyle<DivTag>` recipe using the
theme's selected background, border, and inherited text roles. Its rows are plain containers, so
presses leave focus on the table for Space and Enter. After a change, refresh the old and new item
ranges through `ListController.RefreshRanges`; that also invalidates the owner so header selection
labels update. Keep content revision stable for these targeted refreshes. Theme changes refresh
item batches automatically. This presentation needs no retained View per item or native selection
store, and application code can choose its own colors, indicators, and sizing.

Bind `.OnActivated(view, static (owner, e) => owner.OpenItem(e))` on List or Table to opt into
activation. Unmodified Enter activates the native cursor when the collection itself has focus;
held repeats and keys intended for text input do not activate. An unmodified primary-button double
press activates its item unless a child consumes mouse-down. Focused child controls keep their own
Enter behavior. Activation does not synthesize clicks or change selection, and ordinary item click
bindings still run independently.

`ListActivationEvent` carries `Index`, optional item-root `ItemId`, optional `ContentRevision`, and
`Source` (`Pointer` or `Keyboard`) from the accepted datasource. Revision zero is distinct from an
absent revision. Keyboard activation or selection may request one aligned item batch to resolve an uncached item's
identity; navigation alone does not. The event owns its scalar data, and callbacks run through the
normal View event boundary after native resource borrows are released.

Keep `contentRevision` stable when a managed render cannot change any item output. Increment it when
item content, styling, or height can change. Theme changes invalidate batches automatically.

The revision must also cover filter and sort inputs: changing to a different projection with
the same item count still requires invalidation. Refreshing a range does not recompute a managed
memo or replace immutable records held by an item source. Update that source before issuing a
targeted refresh. `RefreshRanges` invalidates the owning View, not other store subscribers; Rust
discards intersecting cached batches and remeasures the affected items, not necessarily one item.

Native adapters that use `gpui-base` read the same application theme through the projected global
foundation theme. Product variants still flatten into semantic operations; they do not become
foundation theme types or cross the ABI.

Item renderer restrictions:

- synchronous and side-effect-free;
- elements only—no managed child views;
- no nested Scroll, List, Table, Input, Slider, or deferred layer;
- at most 512 items in one native request;
- event payloads should use an index or stable model ID rather than per-item closures.

Declare `.ItemId(id)` on an item root when interactive state should survive structural splices. ID
zero is reserved. An `OnClick` binding without an explicit payload receives that model ID.
An explicit payload of zero means "unset" in the click encoding, so an item that declares an
`ItemId` substitutes the model ID for it: keep explicit item payloads nonzero (offset by one
when zero is a valid model value) or omit the item `ItemId` when positional identity suffices.

Child element keys are scoped beneath their item root, so item-local keys such as `"like"` may
repeat across distinct items. Use stable item identity and event payloads for application actions.

`ListController` supports `ScrollToItem`, `Refresh`, `RefreshRanges`, `Splice`, and `Reset`.
Structural commands preserve unaffected measurements and item batches when their declared result
matches the next managed snapshot.

`ListOptions(orientation: Horizontal, estimatedItemExtent: ...)` lays items out left-to-right
as a virtualized strip instead of stacking them top-to-bottom. Batching, the keyboard cursor,
selection/activation requests, splice/refresh/reset hints, and content/projection revisions keep
their vertical semantics; only the scroll axis changes. The extent seeds unmeasured item widths
and converges to per-item measurements as items render; omitting it defaults to 160 px widths
(40 px heights vertically), and a viewport-height change remeasures widths
because items were measured against the old height. `alignment` maps to start (`Top`) or end
(`Bottom`) anchoring: `Bottom` pins the viewport to the end edge until an explicit scroll, wheel,
drag, key, or controller movement replaces it. `overdraw` extends along the horizontal axis, and
`ScrollToItem` reveals its item with the minimum horizontal movement.

Horizontal navigation uses Left/Right for one item, Home/End for the ends, and PageUp/PageDown
for one viewport width; Up/Down are left unconsumed so they can leave the strip. A discrete
vertical wheel maps onto the horizontal axis when it carries no horizontal delta, and precise
trackpad deltas apply directly. Items measure with unconstrained (max-content) width and the
viewport height, so prefer fixed item widths: percentage widths resolve against max-content and
may collapse. Tables stay vertical; use a horizontal List for strips, carousels, and galleries.

The cursor belongs to the retained List/Table resource, so cache eviction, content refresh, theme
changes, and layout-only rebuilds do not reset it. Accepted `Splice` hints also move it with surviving
items: inserting/removing earlier items shifts its index. Removing the active item chooses the first
replacement or successor at the splice start, falling back to the final item. An empty list has no
active cursor; inserting into it starts at the first item. `Reset`, a count change without matching
hints, or invalid hints reset it to the first item. Pointer/keyboard handlers from a superseded item
declaration cannot change the new cursor before the next paint.

`ItemId` preserves element state and event payload identity; it is not a complete native datasource
index. Native cursor preservation follows valid splices and does not search unseen items for a moved
ID. Use `Reset` for arbitrary reorder/replacement whose identity cannot be expressed by surviving
splice ranges. A remove-then-insert sequence treats the removed active item as deleted.

For declarative projection replacement, pass `projectionRevision` as the optional third argument
to `ListDataSource(count, contentRevision, projectionRevision)`. Changing this stamp resets cursor,
scroll position, measurements, and item batches in the same accepted snapshot, even when count and
content revision are unchanged. Adding or removing the stamp also resets an existing resource;
zero is valid. Omission preserves the existing command-based contract. A projection change
overrides all queued positional commands, including splices and scroll requests for the old order.
Keep the stamp stable for content-only edits and identity changes described by valid splices.
Applications own the stamp and model selection; native code does not build an ID-to-index map.

## Retained Table

`ui.Table` uses the List collection engine and adds declarative `TableColumn[]` metadata. Rust materializes
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
