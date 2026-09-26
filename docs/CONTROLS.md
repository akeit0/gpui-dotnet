# Retained controls

Input, Slider, and Dock keep interaction state in Rust while accepted declarations update their
configuration and presentation. See [Interaction](INTERACTION.md) for custom focus targets.

## Retained Input

`ui.Input` is a native single-line editor. GPUI owns:

- current UTF-8 value and revision;
- focus, caret, selection, and horizontal reveal;
- clipboard commands;
- IME composition and password masking.

Bindings are opt-in: `OnChanged`, `OnSubmitted`, and `OnFocusChanged`. Without a binding, native
editing does not cross into managed code. `Utf8InputOptions`, `InputEvent.Utf8Value`, and UTF-8
controller overloads avoid unnecessary UTF-16 allocation. `InputEvent.Value` decodes lazily.
Password inputs reject Copy and Cut without changing the clipboard, value, or selection.
Paste and ordinary editing remain available subject to disabled and read-only settings.

On macOS, Option+Left/Right moves by word, Shift+Option+Left/Right selects by word, and
Option+Backspace/Delete removes a word. Windows and Linux use Control in place of Option. Word
boundaries follow Unicode word segments without placing the caret inside a grapheme. In password
fields, a word command treats the whole value as one unit rather than exposing its boundaries.
Read-only inputs allow navigation and selection but reject deletion; disabled inputs reject both.

Double-click selects a Unicode word segment, including whitespace when clicked. Dragging after a
double-click extends by whole segments while keeping the original segment selected. Triple-click
selects the entire single line. A masked password value selects as one unit on double-click.
Pointer positions are clamped to grapheme boundaries before changing the caret or selection.

Undo uses Command+Z on macOS or Control+Z on Windows and Linux. Redo uses Command+Shift+Z on
macOS, and Control+Y or Control+Shift+Z on Windows and Linux. Adjacent typing forms one undo entry
until selection, focus, or configuration changes; other edits form separate entries. An IME
composition forms one entry when committed, and a canceled composition does not clear redo.
History is bounded to 100 entries and 4 MiB of retained text per direction, keeping at least the
most recent entry. A changed controller replacement clears history because it supplies an
authoritative value; an identical replacement preserves it. Undo and redo advance the native
revision and emit `OnChanged` when the value changes. Read-only and disabled inputs cannot replay
history, and replay is unavailable during an active IME composition.

`InputController` supports `Focus`, `Blur`, `SelectAll`, `SetValue`, and `SetValueIfCurrent`.
The declarative initial value is consumed only when the native keyed resource is created.
`SetValue` normalizes line breaks to spaces. If the resulting value already matches, it preserves
selection, IME composition, and horizontal scrolling. A changed value moves the caret to the end,
clears composition, and resets horizontal scrolling without emitting a change event. Replacement
is unconditional.

For asynchronous formatting or validation, retain the triggering `InputEvent.Revision` and call
`controller.SetValueIfCurrent(result, inputEvent.Revision)` from an accepted event/effect continuation.
Native delivery silently skips a stale revision or active IME composition. The default
`InputSelectionPolicy.Preserve` keeps the current selection's UTF-16 offsets and direction, clamping
forward to grapheme boundaries in the replacement (or its end). `MoveToEnd` instead moves the caret
to the end. `InputCompositionPolicy.CancelComposition` explicitly permits a changed value to cancel
composition. Identical normalized values preserve editing state regardless of these policies.
Queueing is not confirmation that the replacement applied, and replacements emit no change event.
Use the next native event's revision for subsequent conditional work.

For an observable decision, bind `.OnWriteCompleted(this, static (view, result) => ...)` and call
`controller.SetValueIfCurrentWithResult(value, expectedRevision, requestId)`. The UTF-8 overload
uses the same policies. Request IDs are application-owned values from 1 through `2^62 - 1`;
use distinct IDs for outstanding requests. `InputWriteResult` contains `RequestId`, `Outcome`,
and the native `Revision` at decision time, with no text payload:

- `Applied`: the value changed and the native revision advanced.
- `Unchanged`: normalized text matched; selection, scrolling, and composition were preserved.
- `Stale`: the expected revision did not match; no editing state changed.
- `Composing`: the revision matched but composition policy rejected the write.

Revision checking precedes composition checking; `Unchanged` requires both checks to allow the
write. The result is delivered after native borrows are released. Further edits can occur before
delivery, so the result revision is not a guarantee of current state. These are ordinary live
View-bound events, not guaranteed task completions: resource removal, rebinding, or owner retirement
can drop delivery. With no binding the write still executes, without a callback. Existing
`SetValue` and `SetValueIfCurrent` remain silent. The Input gallery demonstrates reporting results.

Revisions are nonzero opaque tokens, not edit counts. They change for controller writes, IME content
and composition transitions, and resource recreation; caret movement and focus do not change them.
An event remains safe to inspect asynchronously, but its revision may already be stale. Continue
using the View's owned work/effect lifetime for cancellation and accepted delivery; revision checking
protects native editing state and does not grant a retired View permission to issue commands.

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
interaction event, including while disabled. Disabled state blocks user interaction, not
programmatic synchronization.

`TrackColor`, `FillColor`, `ThumbColor`, and `ThumbBorderColor` customize the retained parts through
`IGpuiElementStyle<SliderTag>` recipes. Omitted colors resolve from the current theme: border variant
for the track, accent for the fill and thumb border, and surface background for the thumb. Explicit
colors preserve alpha; the last declaration for each part wins. Removing an override in a later
render restores the current theme default. Presentation changes preserve value, active thumb,
focus, drag/keyboard interaction, and event revision. Part overrides do not alter disabled behavior
or the shared focus ring. The Input gallery demonstrates switching between a sample-owned recipe and theme
defaults on the same retained slider.

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
Tab chrome carries no accessibility roles yet; that belongs to the planned accessibility pass.
