# Components and retained resources

GPUI.NET exposes semantic components. C# builders write component IDs, typed operations, child
records, and UTF-8 data into the render arena. Rust chooses the concrete GPUI implementation through
the component's `NativeAdapter`.

The native application initializes the `gpui-base` behavior foundation. Button,
Checkbox, and Radio use foundation primitives for activation, focus, keyboard, accessibility, and
disabled behavior. Dock wears a small in-repo skin over the foundation layout engine, so the
default host links no styled component facade. Other adapters remain direct GPUI or GPUI.NET
implementations until their behavior families meet the migration parity criteria. Broad
`gpui-component` facilities link only into custom hosts that select them, such as the optional
editor host.

## Component classes

There are three implementation classes:

1. Snapshot components are rebuilt from the retained snapshot, such as Div, Text, Button, Badge,
   Divider, Image, and deferred-layer declarations.
2. Retained resources preserve mutable native state across managed renders: Scroll, List, Table,
   Input, Slider, and Dock.
3. Managed compositions combine existing primitives without adding an ABI component. Dialog,
   Sheet, and the shared title-bar helper use this model.

Optional native families use a fourth boundary: a typed schema assembly wraps the generic
NativeExtension node, while a custom host links the matching Rust provider at build time. The base
managed and native packages contain no extension-specific component contract. See
[EXTENSIONS.md](EXTENSIONS.md).

Choose the simplest class that satisfies the behavior. A component needs a retained resource only
when interaction state must survive independently from managed renders.

## Styling

Styled elements support the generated fluent operations declared by `bindings/schema.json`,
including layout, dimensions, uniform and per-side/axis margins, padding, and gaps, min/max
sizes, flex basis/shrink/wrap, container alignment, backgrounds, borders, text color, and
typography. Interactive
elements additionally support native hover and active paint operations.

The application theme supplies semantic tokens:

```csharp
var card = ui.VStack(content)
    .Padding(Px(16))
    .Background(ui.Theme.Colors.SurfaceBackground)
    .BorderColor(ui.Theme.Colors.BorderVariant)
    .TextColor(ui.Theme.Colors.Text);
```

Native controls receive a resolved subset of the same theme. Product variants remain in the
application:

```csharp
internal readonly record struct PrimaryButtonStyle(GpuiTheme Theme)
    : IGpuiElementStyle<ButtonTag>
{
    public Element<ButtonTag> Apply(Element<ButtonTag> button) =>
        button
            .Background(Theme.Colors.Accent)
            .HoverBackground(Theme.Colors.AccentHover)
            .ActiveBackground(Theme.Colors.AccentActive)
            .TextColor(Theme.Colors.TextOnAccent);
}
```

`.Style(value)` invokes the typed recipe and returns the normal element builder. A later fluent call
can override a value. Do not add application variant enums or style objects to the native ABI.

## Snapshot components

### Layout and content

- `Div`, `VStack`, and `HStack` form layout containers.
- `Text` carries UTF-8 content and inherits text styles normally.
- `Spacer` consumes available flex space.
- `Divider` provides a themed one-pixel separator by default.

### Interactive and display

- `Button`, `Checkbox`, and `Radio` use `gpui-base` behavior with stable GPUI.NET element identity.
  Their descendant Text content supplies the accessible name. `Disabled(bool)` prevents pointer and
  keyboard activation and removes the control from focus traversal.
- `Checkbox` and `Radio` use the controlled `Checked(bool)` operation. Foundation change requests
  translate back into the existing managed click callback; the next managed snapshot remains
  authoritative.
- `Badge` provides minimal native defaults that managed styling can override.
- `Image` uses GPUI's decoder and cache. Its data is a filesystem path; presentation supports
  object fit and grayscale.
- `Drawing` owns ordered `Path` children and paints them through GPUI's native path tessellator.
  `ViewBox` independently maps each coordinate axis into the final layout bounds, making plot
  geometry responsive without a managed paint callback. Stroke widths stay in
  device-independent pixels. `Circle` uses the smaller ViewBox axis scale for both radii so point
  markers remain circular; `Ellipse` scales each radius independently. A `Path` must be attached
  directly to exactly one `Drawing`.
- `Dynamic` is a transparent one-child wrapper. While active, native GPUI schedules one managed
  render per display frame for the wrapper's owning View. Multiple wrappers for one View are
  deduplicated. The application remains responsible for time, interpolation, and stopping.

### Observer key and mouse events (hot keys)

`Div`, `Button`, `Checkbox`, and `Radio` support observer `OnKeyDown`, `OnKeyUp`,
`OnMouseDown`, `OnMouseUp`, `OnModifiersChanged`, `OnHover`, `OnMouseDownOut`, `OnMouseUpOut`,
`OnMouseMove`, `OnScrollWheel`, and `OnFileDrop` bindings through the `key_mouse` capability. They reuse the
existing `control_event` channel (kinds 9-19) and never consume the native event: Rust forwards
the key name (plus modifiers and held-repeat), the mouse position/button/click-count (plus
modifiers), the bare modifier state, hover transitions, movement/wheel deltas, or dropped file
paths without calling
`stop_propagation`, moving focus, or blocking default handling.
Focused Input editing, Slider keys, List/Table navigation, Overlay Escape, and menu triggers
therefore win first; a bound element only observes events that bubble to it. Movement and wheel
events are only published while bound, so unregistered elements cost nothing; registered
handlers must stay cheap.

For window-wide hot keys, bind on the root container and match exactly:

```csharp
protected override Element Render(ref RenderContext ui) =>
    ui.VStack(ui.Text("Save with Ctrl+S"))
        .OnKeyDown(this, (view, key) =>
        {
            if (key.Matches("s", control: true))
            {
                view.Save();
            }
        });
```

`KeyEvent.Matches` compares the key name ordinal-ignore-case and requires exact modifiers, so
`Ctrl+Shift+S` does not match `Ctrl+S`. Holding a key produces OS key-repeat `Down` events with
`IsHeld` set, so one-shot hot-key actions should guard with `!key.IsHeld`. Modifier-only presses
(e.g. holding Ctrl alone) never produce key events in GPUI; track them with `OnModifiersChanged`,
which reports the current modifiers. Mouse movement never
crosses the ABI; only discrete
down/up for opted-in elements do. These bindings are render-pass declarations like `OnClick`
(pure `Render`, state changes in the handler plus `Invalidate()`), and they are invalid inside
virtualized List/Table row snapshots, which have no mounted View lifetime.

Image failures materialize a themed fallback. URI loading is not part of the current component
contract.

Use `Dynamic` for application-defined visual state such as chart interpolation. Continuous native
interaction such as scrolling, text editing, and slider dragging should remain in its retained
native resource.

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

Keep `contentRevision` stable when a managed render cannot change any row output. Increment it when
row content, styling, or height can change. Theme changes invalidate batches automatically.

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

`ListController` supports `ScrollToItem`, `Refresh`, `RefreshRanges`, `Splice`, and `Reset`.
Structural commands preserve unaffected measurements and row batches when their declared result
matches the next managed snapshot.

## Retained Table

`ui.Table` uses the List row engine and adds declarative `TableColumn[]` metadata. Rust materializes
the header and applies the same column widths and alignment to `ui.TableCell(column, ...)` nodes.

Rows keep List semantics, including batching, model identity, keyboard navigation, refresh, and
splice behavior. A changed column declaration invalidates row batches because cell layout changes.
Managed row content should use a horizontal container; Table does not infer a row layout from plain
Div children.

## Retained Input

`ui.Input` is a native single-line editor. GPUI owns:

- current UTF-8 value and revision;
- focus, caret, selection, and horizontal reveal;
- clipboard commands;
- IME composition and password masking.

Bindings are opt-in: `OnChanged`, `OnSubmitted`, and `OnFocusChanged`. Without a binding, native
editing does not cross into managed code. `Utf8InputOptions`, `InputEvent.Utf8Value`, and UTF-8
controller overloads avoid unnecessary UTF-16 allocation. `InputEvent.Value` decodes lazily.

`InputController` supports `Focus`, `Blur`, `SelectAll`, and `SetValue`. The declarative initial
value is consumed only when the native keyed resource is created.

The retained GPUI.NET engine remains authoritative after comparison with the foundation Input.
Foundation `InputState` uses a Rope-backed editor and emits change notifications without a value or
revision; adapting it to the existing callback packet would materialize the full value for each
subscribed change and require separate revision bookkeeping. The retained engine instead exposes
its contiguous UTF-8 value directly to the synchronous callback while preserving native IME,
Unicode selection, clipboard, password, focus, and controller behavior. Its root declares the
`TextInput` accessibility role.

## Retained Slider

`ui.Slider` supports single values and ranges, horizontal or vertical orientation, linear or
logarithmic mapping, bounds, and step size. GPUI owns pointer drag and keyboard interaction.

`OnChanged` fires for value changes; `OnReleased` marks the end of a pointer or keyboard
interaction. `SliderController.SetValue` updates retained native state without synthesizing an
interaction event.

The retained GPUI.NET engine remains authoritative after comparison with the foundation Slider.
It supports snapshot-time configuration reconciliation, focus and keyboard interaction, range
thumb selection, release events for pointer and keyboard input, and controller updates without
synthetic events. The foundation Slider does not yet cover that contract. The retained root uses
the same slider role, numeric value, bounds, step, and orientation accessibility metadata.

## Retained Dock

`ui.DockArea(key, center)` declares one retained native Dock. Build its center layout from
`ui.DockSplit(axis, ...)`, `ui.DockTabs(activeIndex, ...)`, and
`ui.DockPanel(id, title, content)`. The overload accepting a region span adds at most one
`ui.DockRegion(side, content, options)` for each of `Left`, `Bottom`, and `Right`; region content is
a split or tab group. Panel IDs must be unique across the entire area. A direct child of a split or
a side-region root can use `.InitialSize(pixels)`; the value seeds the native layout and is not a
continuously controlled size. Region options likewise seed initial open state and declare whether
the region can collapse.

The locally skinned foundation Dock owns tab activation, reordering and cross-group moves,
split and side-region resizing, side-region collapse, focus, close/zoom affordances, drop targeting,
and clean-frame painting. Panel content remains a normal semantic element subtree and may contain a
framework-owned child View or nested retained resource. Rust materializes that content from the
last retained managed snapshot; it does not invoke managed rendering during a clean native frame.

The declaration is authoritative when its structure changes. Axes, initial sizes, active indices,
panel IDs, region placement, or container topology replace the corresponding native layout.
Changes to panel titles, options, content, or region collapsibility update retained state without
resetting native tab moves, split sizes, or open state.

Two coarse events cross the boundary through render-bound bindings on the area:
`OnDockLayoutChanged` fires for native interaction and for declarative or controller-driven
structural changes (debounce with `DockEvent.Revision`; it carries no payload), and
`OnDockPanelClosed` fires with the panel id when a panel leaves natively through the chrome or
`DockController.ClosePanel`. Panels removed by declaration or pruned by layout import do not fire
close events. A natively closed panel stays closed (tombstoned) until the declaration drops its
id, so snapshots cannot resurrect it; dropping the id and re-adding it installs fresh.

`DockController`, bound to the area key, offers `ClosePanel`, `SetRegionOpen`, `ImportLayout`,
and `ExportLayout`. Commands queue until the next committed snapshot materializes the area and
apply after the declaration. Tab activation stays declarative through `DockTabs` activeIndex:
the foundation exposes no node-stable activation handle, so there is no controller activate
operation in this slice.

`ExportLayout` delivers the authoritative native layout as JSON through the layout binding; the
export is dropped when nothing is bound. The document is a GPUI.NET envelope,
`{"format":1,"layout":{...}}`, whose nested layout is the foundation's opaque persisted state:
imports accept only the current envelope and reject bare foundation documents or unknown
formats rather than guessing. Panel leaves inside the nested layout follow the managed
layout-leaf contract: `panel_name` is `"GpuiDotnetPanel"` and the info value carries the
declaration panel id as `{"id":...}`; titles, flags, and content always come from the live
declaration, never from the document. `ImportLayout` restores structure (splits, sizes, active
tabs, region placement and open state) from such a document while panel content, titles, and
options always come from the live declaration, joined by panel id. Persisted panels unknown to
the declaration are pruned; declared panels missing from the document are appended to the center,
so an import never silently drops live content; lock state always comes from the declaration.
Tiles subtrees have no managed declaration and are skipped on import. Tab chrome carries no
accessibility roles yet; that belongs to the planned accessibility pass.

## Deferred layers

Deferred layers paint relative to the window rather than the local layout tree:

- `Overlay`: generic modal or non-modal content with placement, priority, backdrop, and dismissal
- `Dialog`: centered modal Overlay composition
- `Sheet`: edge-aligned Overlay composition
- `Tooltip`: delayed trigger-relative content with side flipping and viewport clamping
- `ContextMenu`: pointer-anchored right-click content
- `PopoverMenu`: trigger-attached left-click content with menu switching

Rust owns geometry, input interception, deterministic stacking, focus entry/restoration, and
dismissal. Managed code owns visuals and actions. Deferred layers can contain normal child views and
retained controls, but cannot appear inside virtualized rows.

Tooltip and PopoverMenu delegate trigger measurement and viewport-aware positioning to
`gpui-base` Popup/Positioner. ContextMenu uses the same Positioner for pointer-corner placement and
viewport clamping. PopoverMenu and ContextMenu delegate their open/focus/restoration lifecycle to
foundation PopoverState. Modal Overlay containers register with the foundation FocusTrapElement;
Dialog and Sheet inherit that behavior because they are managed Overlay compositions.

GPUI.NET retains tooltip timing, menu-group switching, overlay placement and backdrop rendering,
priority arbitration, topmost dismissal guards, and managed dismissal callback routing. The
foundation Sheet host couples Escape and backdrop closing and does not expose the independent
semantic options or ordering needed by the generic Overlay contract.

## Window chrome

`WindowControlArea` marks Div or Button nodes as native Drag, Minimize, Maximize, or Close regions.
These are hit-test semantics, not managed click callbacks.

`GpuiTitleBar.RenderWindow` is a managed composition that consumes `GpuiMenu[]`. It preserves the
native macOS title/menu path by default and supplies minimal managed menus and window controls where
app-side chrome is appropriate. Applications may force the managed macOS path or build title bars
manually from the same primitives.

## Adding a component

For a snapshot component:

1. Add the component and operations to `bindings/schema.json`.
2. Run the semantic generator.
3. Add or update the Rust adapter and materializer behavior.
4. Add managed validation, native validation, and materialization tests.
5. Add a small sample route only when it demonstrates behavior not covered elsewhere.
6. Update this document and [ABI.md](ABI.md) if the wire contract changed.

For a retained resource, also define stable identity, configuration reconciliation, command/event
semantics, teardown, pending-command behavior, and the high-frequency ownership boundary before
adding public API.

For an optional component family, keep its typed managed contract in a separate schema assembly,
register that schema in `bindings/extensions.json`, and link its Rust provider into an explicit
custom host. See [EXTENSIONS.md](EXTENSIONS.md).
