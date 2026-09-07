# Native ABI

GPUI.NET currently uses ABI version 7. Managed startup requires an exact ABI version, a compatible
API-table prefix, all required function entries, and the semantic schema hash generated from
`bindings/schema.json`.

Conditional input write acknowledgements use semantic command 25 (`InputSetValueIfCurrentWithResult`),
Input callback operation 509, and control event 22. Command word `a` is the nonzero expected native
revision; `b` packs the existing selection/composition policy in bits 0–1 and a nonzero request ID
in bits 2–63. UTF-8 data is validated as for command 24, whose reserved-bit rules remain unchanged.
Event 22 carries 16 little-endian bytes: U64 request ID, U32 `input_write_outcome` (0–3), and zero
U32 reserved. Its flags are zero and its revision is the nonzero decision-time native revision.
Managed validation rejects malformed lengths, reserved fields, IDs, outcomes, flags, and revisions
before dispatch. Native delivery is deferred until input and root borrows end and follows the live
binding lifetime. This changes the semantic hash only; C layouts, callback tables, and ABI 7 remain
unchanged.

The ABI is an internal C contract between `GPUI.NET.Core` and a native host. Application code does
not manipulate pointers or wire records directly.

Accessibility operations 900 (`AccessibleName`) and 901 (`AccessibleDescription`) carry UTF-8 data
using the existing offset/length operation layout. The `accessible` capability restricts them to
Button, Checkbox, Radio, Input, and Slider. Standard data-operation validation and snapshot string
interning apply; last declarations win. They change the semantic schema hash, not ABI version 7.

## Discovery

List/Table operation 329 (`ListOnContextMenuRequested`) binds a right-click request for rows with
a nonzero ItemId. Control event 23 carries 24 little-endian bytes: U32 row index, zero U32 reserved,
nonzero U64 item ID, and nonzero U64 native anchor ID. Flag bit 1 indicates a content revision in
the existing revision field; other flag bits must be zero. With bit 1 unset, revision must be zero.
Managed validation checks the complete payload before dispatch.

ContextMenu operation 622 (`ContextMenuRowAnchor`) selects that native request instead of a local
trigger. `RowContextMenu` emits the existing two-child shape with an empty trigger. The anchor is
window-local in scope, owned by the callback's View, single-use, and invalid after dismissal or row
loss. Invalid/expired anchors produce no menu. This changes the semantic hash only; ABI 7 layouts
and entry points are unchanged.

```c
const gpui_dotnet_api_v3* gpui_dotnet_get_api(uint32_t requested_version);
```

The API table contains:

- `struct_size`, `abi_version`, and `schema_hash`;
- `validate_render`;
- `run_application`;
- `notify_view`;
- `dispatch_command` for retained resources;
- `dispatch_application_command` for windows and themes;
- `dispatch_application_menu`;
- `supports_extension` for independently versioned build-time extension schemas.
- `dispatch_extension_command` for schema-owned commands to retained extension resources.
- `invalidate_artifacts` for batched reactive demand-cache invalidation.

The generated base schema hash is deliberately separate from the ABI version. Component IDs,
operation IDs, capabilities, or payload constraints can change without altering C record layouts;
the hash rejects a managed/native pair built from different schemas.

Table nodes may contain zero header children or exactly one content child per `TableColumn`
operation (322), in column order. Other counts fail native validation with `-57`; managed validation
rejects the same structure before publication. Header children are ordinary retained-snapshot
content, not virtual row batches. Input part colors use U32 RGBA operations 506 (placeholder),
507 (caret), and 508 (selection); the last declaration for each part wins, including its alpha.
Omitted colors resolve from the current native theme. These semantic additions change the schema
hash without changing ABI 7, C layouts, entry points, or callback packets.

Slider part colors use U32 RGBA operations 341 (track), 342 (fill), 343 (thumb), and 344 (thumb
border), restricted to Slider nodes. The last declaration for each part wins, including alpha;
omission restores the current theme default when the retained configuration is reconciled.
These operations change the schema hash only and preserve ABI 7 and the existing event packets.

Table header colors use U32 RGBA operations 325 (background), 326 (inherited text), and 327 (bottom
border), restricted to Table nodes. They affect both native labels and managed header content.
Omission uses current theme roles; later declarations override earlier colors without discarding
alpha. Header paint is not part of retained column metadata and does not invalidate row batches.
These operations change the schema hash only, with no C-layout or ABI-version change.

An optional extension has its own ID, protocol version, and schema hash. `supports_extension`
checks that tuple before application startup. Extension-specific definitions never enter the base
schema; the generic NativeExtension node carries the tuple, component kind, retained key, and an
opaque UTF-8 configuration owned by the extension schema.

List/Table projection revision is optional U64 operation 328. Last declaration wins; omission and
zero are distinct. Changing the optional value resets the retained collection and discards queued
positional commands during snapshot reconciliation. Content revision and event packets retain
their existing meaning. This operation changes the semantic schema hash, with no ABI 7 layout or
entry-point change.

The extension-command envelope keeps extension-specific IDs and payload layouts out of Core. A command contains its extension ID, component kind, version, schema hash, owner View, key,
numeric command and flags, expected revision, and opaque byte payload. Native code validates the
envelope and provider compatibility and copies the payload before the FFI call returns.

ABI 7 adds required artifact acceptance and batched artifact invalidation. It retains explicit
root acceptance, range source/artifact identities, release obligations, and managed-owned
single-pass buffers. Both function tables are incompatible with earlier hosts.
The API table keeps the historical `GpuiDotnetApiV3` name;
its `abi_version` value and the requested version, not that type name, negotiate this protocol.
Old and new managed/native hosts must not be mixed; rebuild custom hosts with the matching contract.

## Application and callbacks

`run_application(application_id, callbacks)` starts one native GPUI application. The managed
callback table provides:

- dirty root rendering;
- root snapshot acceptance acknowledgement;
- click dispatch;
- virtual list/table range rendering;
- cached range artifact release;
- owner-view preparation for a requested dynamic frame;
- retained control events (Input, Slider, Dock, List/Table row events, and observer key/mouse);
- application-started notification;
- window-closed notification;
- application-menu action dispatch.

The callback table starts with `struct_size`, allowing native code to validate the available
prefix. Every callback is a Cdecl unmanaged function pointer and returns an `int32_t` status.

The native application is registered before the application-started callback, so managed code can
enqueue initial windows synchronously. A window ID is also its render-session ID. Closing one
window detaches only that managed session; the application event loop ends after the last registered
window closes.

## Render callbacks

Dirty root rendering uses:

```c
int32_t render(
    uint64_t session_id,
    gpui_render_arena* arena,
    uint32_t* root,
    uint64_t* revision);
```

`arena` is an output descriptor, not writable Rust-owned storage. Managed code resets a reusable
managed-owned arena, reserves capacity before every write, invokes user rendering once, and
publishes the descriptor only after managed rendering and validation succeed. Growth preserves
written contents and does not change the current render generation or rerun user code.

Status `0` means success; every nonzero status is an error, including the old value `1`.
Rust immediately validates and decodes the borrowed buffers into an owned `ValidatedSnapshot`.
It must finish decoding before any further managed callback or session teardown, and must not
retain or free a buffer pointer. Root and range rendering use separate reusable managed owners.
Cached native row batches own decoded snapshots, never borrowed output arenas.

A successful root publication returns a nonzero, monotonically increasing session revision.
This is distinct from the arena generation used to validate builder handles. After decoding and
reconciling resource declarations, Rust calls exactly once:

```c
int32_t render_completed(uint64_t session_id, uint64_t revision, int32_t status);
```

Rust must release its borrow of the arena before this callback. Status zero accepts the snapshot;
a nonzero validation/decode status faults the session without mounting candidates. Failed render
callbacks have no publication and receive no acknowledgement. An acknowledgement failure also
rejects the native snapshot. Missing, zero, mismatched, or duplicate revisions are protocol errors.
The callback table places this required pointer after `dynamic_frame` (offset 72 on 64-bit
targets; offset 36 on 32-bit targets). `release_artifact` is at offset 80/40 and `accept_artifact`
at offset 88/44, for a total callback-table size of 96/48 bytes on 64/32-bit targets.

Managed acceptance commits the complete reachable tree and props, retires replaced subtrees, then
activates all new View routes, then starts effects parent-first. New root/range render and event dispatch are excluded until it
finishes. Mount hooks can enqueue accepted-resource commands and invalidate a later frame. Native
materialization and row requests begin only after successful acknowledgement.

The output descriptor's `flags` and all four legacy `required_*_capacity` fields are reserved and
must be zero. They remain in the layout to avoid needless generated-record churn. A failed
callback publishes no consumable descriptor. `Render()` remains deterministic and side-effect-free;
removing capacity retry does not relax the declarative contract.

The source/artifact acceptance, release, and invalidation protocol is independent of request
shape. `list_render_range` is the current List/Table request adapter, not a generic demand request
format. Its range bounds and direct-child-count rule apply only to that adapter. Shared managed
publication and native decoding/lease ownership do not impose those rules on other demand shapes.
There is currently no public custom demand-request entry point. Separating this implementation
does not change the ABI 7 layouts, callback signatures, or semantic schema hash.

Virtual rows use:

```c
int32_t list_render_range(
    uint64_t session_id,
    uint64_t renderer_token,
    uint64_t source_id,
    uint32_t start,
    uint32_t count,
    gpui_render_arena* arena,
    uint32_t* root,
    uint64_t* artifact_id);
```

The returned root must contain exactly `count` direct row children. `count` is limited to 512. Range
rendering uses the same single-call, borrowed-output contract as root rendering. Each requested
row is rendered once per range request; later cache misses can request that range again.

Each native row engine has a nonzero, non-reused source ID independent of its renderer method.
Two controls using the same renderer still have distinct source IDs. Successful managed range
publication returns a nonzero, non-reused session artifact ID. Its event bindings remain live
until the native batch releases them:

```c
int32_t release_artifact(
    uint64_t session_id, uint64_t source_id, uint64_t artifact_id, int32_t status);
```

Native code releases after it finishes borrowing the output. Status zero retires the artifact
normally; a nonzero decode/shape status also faults the session. Failed managed publication
returns no artifact. Normal release occurs on eviction, invalidation, source removal, or window
shutdown. A native batch owns exactly one release obligation. Duplicate releases and releases
after managed shutdown are harmless. A live artifact cannot be released by another source.
Release runs framework cleanup only and is allowed during pending root acceptance and after a
session fault. Removing a source releases its batches even if an old native frame retains the
row engine. No cache hit or individual cached row requires a release callback.

After successful decode and row-count validation, native calls the required
`accept_artifact(session_id, source_id, artifact_id)` callback, returning `int32_t`. It runs after
the arena borrow ends and before the batch enters the cache. Managed code commits the artifact's
Signal dependencies here. Duplicate or mismatched acceptance faults the session. Acceptance
failure discards and releases the batch.

Signal changes send native invalidation at the outer managed callback boundary:

```c
struct native_artifact_key { uint64_t source; uint64_t artifact; };
int32_t invalidate_artifacts(uint64_t session_id, const native_artifact_key* keys, int32_t count);
```

Each key is 16 bytes, with `artifact` at offset 8. The required API entry is at offset 80/48 on
64/32-bit targets. Count must be positive, the pointer non-null, and both IDs nonzero. Native
copies the batch before returning and delivers it through the existing window message channel.
Delivery evicts matching artifacts and remeasures their rows, preserving other cached batches and
requesting native repaint without marking the managed root dirty. Stale source/artifact pairs are
harmless. The regular release callback retires their managed dependencies and event bindings.

An active `Dynamic` wrapper asks native GPUI for another display frame. Before the corresponding
root render, native invokes `dynamic_frame(session_id, owner_view)` so managed retained fragments
for that owner and its ancestors are marked dirty. Multiple active wrappers with the same owner are
collapsed to one callback per frame.

Renderer tokens pack a prepared/mounted View handle in the high 32 bits and a generated method ID
below. Event tokens pack the View handle above a dynamic marker (bit 31) and a non-reused 31-bit
event ID. Live IDs map to recyclable storage slots; retired IDs never resolve to new callbacks.
Retired event IDs and owner handles are ignored. Malformed or never-issued identities are errors.
Click records carry a separate unmanaged `uint64_t` payload for row or model identity.

## Render arena

The render arena consists of four flat buffers:

- `NodeRecord[]` for component IDs, flags, and UTF-8 data ranges;
- `OpRecord[]` for typed semantic operations;
- `ChildRecord[]` for parent/child edges;
- one UTF-8 byte buffer.

`OpRecord` has fixed C layout:

```c
typedef struct gpui_op_record {
    uint32_t node;
    uint16_t code;
    uint16_t value_kind;
    uint64_t a;
    uint64_t b;
} gpui_op_record;
```

Canonical payload rules:

- node flags and reserved fields must be zero;
- `None` operations require `a == 0 && b == 0`;
- `F32` and `U32` use only the low 32 bits of `a` and require `b == 0`;
- `F32x2` packs two finite IEEE-754 values into the low and high 32-bit halves of `a` and requires
  `b == 0`;
- `U64` uses all of `a` and requires `b == 0`;
- callback operations require nonzero token `a`; only schema-approved callbacks may use payload
  `b`;
- all floats must satisfy the operation's finite/range constraints;
- UTF-8 ranges must be valid and in bounds;
- components, child counts, and operation capabilities receive managed diagnostics and
  authoritative native validation. Full graph connectivity and retained resource-key conflicts
  are enforced at native acceptance without a second managed pass.

The managed validator catches builder/runtime errors before FFI. Native validation remains
authoritative because a custom or mismatched managed host must not create invalid GPUI state.

## Retained control events

Retained controls emit a typed `NativeControlEvent` containing event kind, flags, native revision,
and a borrowed byte range. Managed code validates and copies the payload before returning through
FFI, so application code can retain event data without retaining native borrowed memory.

Input events carry UTF-8 data for Changed, Submitted, and FocusChanged transitions. Slider Changed
and Released events carry one little-endian `f32`, or two ordered values when the range flag is set.
Dock LayoutChanged carries no payload; Dock LayoutExported carries the UTF-8 layout JSON requested
through the controller; Dock PanelClosed carries the UTF-8 panel id.

Built-in text payloads are validated as UTF-8 before application delivery; malformed sequences
return `-112` rather than being replaced with U+FFFD. An explicitly encoded U+FFFD remains valid.
Input retains validated, owned bytes and decodes its UTF-16 `Value` lazily. Extension payloads
remain opaque bytes whose interpretation belongs to the extension decoder.

Control-event kinds are global: Input uses 1-3, Slider uses 4-5, Dock uses 6
(LayoutChanged), 7 (LayoutExported), and 8 (PanelClosed), observer key events use 9
(KeyDown) and 10 (KeyUp), observer mouse press events use 11 (MouseDown) and 12 (MouseUp),
observer modifier events use 13 (ModifiersChanged), hover transitions use 14 (Hover), outside
press events use 15 (MouseDownOut) and 16 (MouseUpOut), mouse movement uses 17 (Move), and
scroll-wheel movement uses 18 (Wheel), OS file drops use 19 (Dropped), and List/Table row events
use 20 (Activated) and 21 (SelectionRequested).
Resource kinds are Scroll 1,
List 2, Input 3, Slider 4, and Dock 5, with the command IDs listed below. These numbers
generate from `bindings/schema.json` into both managed enums and native constants; the schema
hash covers them, so either side renumbering without the schema fails verification. Command and
event payload shapes, routing, and queueing stay hand-written: the schema owns identities,
not behavior. Describing payload layouts as separate compatibility units is open phase-10 work.

List/Table activation binds operation 323 (`ListOnActivated`) to a View callback token. Its payload
is exactly 16 little-endian bytes: `u32` row index (at most `Int32.MaxValue`), `u32` reserved zero,
and `u64` row-root ItemId (zero means absent). Flag bit 0 selects Keyboard (1) or Pointer (0);
bit 1 indicates that revision contains the accepted datasource content revision, including zero.
Without bit 1, revision must be zero. All other flags are reserved zero. Managed code rejects
malformed packets with `-112` and copies the scalar identity before callback delivery. This semantic
addition changes the schema hash and retains ABI version 7 and the existing control-event layout.

List/Table selection requests bind operation 324 (`ListOnSelectionRequested`) to an independent
View callback token and use the same payload, flag, revision, and validation contract as activation.
Native emits a single-row request on unmodified Space or an unconsumed primary single press; the
managed application owns accepted selection and its row presentation. There is no selected-state
command or per-row selection callback in the datasource protocol. Both event bindings reuse one
cached identity lookup, and keyboard requests load at most one ordinary aligned row batch when
needed. Adding this operation and event retains ABI version 7 and changes the schema hash.

Key observer payloads carry the UTF-8 GPUI key name (non-empty, NUL-free, at most 128 bytes)
as borrowed data. Flags carry modifiers in bits 0-4 (control, alt, shift, platform, function,
matching the click encoding) plus the held-repeat bit 5 for KeyDown only; revision is reserved
zero. Mouse observer payloads carry 16 little-endian bytes: `f32` x, `f32` y, `u32` button
(0 Left, 1 Right, 2 Middle, 3 Back, 4 Forward), and `u32` click count (at most 255). Flags
carry the same 5 modifier bits; revision is reserved zero. Modifier observer payloads carry
no data: flags hold the current 5 modifier bits and revision is reserved zero; this is the only
event for modifier-only presses, which never produce key events in GPUI. Hover payloads carry
no data: flags hold modifiers in bits 0-4 and the hovering state in bit 5; they fire on
enter/exit transitions only. Outside press payloads match the 16-byte mouse press shape. Mouse
movement payloads carry 12 little-endian bytes: `f32` x, `f32` y, and `u32` pressed button
(0-4, or `0xFFFFFFFF` when none). Scroll-wheel payloads carry 20 little-endian bytes: `f32` x,
`f32` y, `f32` delta x/y, and `u32` units (0 pixels, 1 lines). Movement and wheel events are
only published while bound, so unregistered elements cost nothing; registered handlers must
stay cheap because these fire at pointer frequency. File-drop payloads carry an 8-byte LE
header (`f32` x, `f32` y) followed by NUL-separated UTF-8 paths, at least one and none
empty, bounded to 1 MiB and 4096 paths; GPUI translates the platform drop into its internal
drag system, so the bound element under the cursor receives the drop. Native converts platform
paths lossily where necessary, but the resulting wire bytes must be valid UTF-8. All families
validate strictly and
are dropped with `-112` on any out-of-range flag, revision, length, non-finite coordinate,
unknown button, or malformed UTF-8.

Event kinds with bit `0x8000` set belong to the generic native-extension namespace. The lower 15
bits contain the non-zero event ID generated from the extension schema; flags, revision, and byte
payload retain their schema-defined meanings. Core validates and copies the envelope, then routes
it through the render-bound event token to the typed extension decoder. This reserves no
extension-specific IDs or payload layouts in the base ABI.

## Application commands

`NativeApplicationCommand` is application-scoped and contains a window ID, command, flags, a
borrowed byte range, and geometry fields. The native entry point validates and copies borrowed data
before enqueueing an owned command to the GPUI thread.

Current commands are:

| Command | Contract |
|---|---|
| Open | UTF-8 title, positive size, optional position/activation, title-bar style |
| Close | existing window ID |
| Activate | existing window ID |
| Minimize | existing window ID |
| ToggleMaximize | existing window ID |
| SetTitle | non-empty UTF-8 title |
| Resize | positive finite width and height |
| SetTheme | versioned appearance and resolved semantic palette, application-scoped |
| ManagedCodeUpdated | empty application-scoped Hot Reload invalidation |

Open flags encode optional position, activation, and `System`, `Custom`, or `Hidden` title-bar
style. Runtime reposition is not exposed because the pinned GPUI revision has no durable
cross-platform operation for it.

The theme command uses the command record's byte pointer as a private fixed-size payload. Payload
version 2 is 20 sequential little-endian `u32` values: version, appearance (`0` Light or `1` Dark),
and 18 resolved RGBA semantic roles. The native entry point requires the exact payload size and
rejects unsupported versions or appearance values. Resolved roles feed GPUI.NET native rendering
and the global `gpui-base` theme; application style variants and Rust
foundation types do not cross the ABI.
The managed-code update command clears native List/Table row snapshots and dirties each managed
window without resetting retained control or Dock identity and interaction state.

## Application menus

`dispatch_application_menu` receives a borrowed flat preorder tree. Record kinds are Menu, Action,
and Separator. Children reference their containing menu by record index; actions carry nonzero
managed action IDs. Rust copies the tree before returning and installs the GPUI menu model.

macOS presents the model in the global AppKit menu bar. Other platforms can reuse the same managed
definitions through app-side `PopoverMenu` composition. Native activation returns the action ID
through the managed menu callback.

## Retained resource commands

`NativeResourceCommand` identifies a resource by owner View handle, resource kind, and UTF-8 key.
The call validates and copies borrowed key/data bytes before queueing work on the native UI thread.

Commands require a mounted owner and a matching declaration in the accepted native presence index.
An absent resource returns `-34`, surfaced as a managed exception. Ingress stamps each queued
command with the declaration's presence generation, then delivery checks it again on the GPUI
thread. Removal retires the generation; reappearance under the same key gets a new one. Commands
already queued for the old generation are discarded. Repeated snapshots preserving a declaration
preserve its generation. Pending materialization commands are pruned when the declaration leaves.
The same rule applies to UTF-8 Input setters and extension commands. Presence is published before
`render_completed`, allowing accepted effect setup to command declared resources before materialization.

| Resource | Commands |
|---|---|
| Scroll | ScrollToOffset, ScrollToTop, ScrollToBottom |
| List/Table row engine | ScrollToItem, Splice, Reset, Refresh |
| Input | Focus, Blur, SetValue, SetValueIfCurrent, SelectAll |
| Slider | SetValue |
| Dock | ClosePanel, SetRegionOpen, ImportLayout, ExportLayout |

Scroll and focus/value commands apply to the retained resource directly. List structural commands
are measurement hints and are reconciled with the next managed snapshot. A hint that disagrees with
the declared datasource count falls back to a full reset. Dock commands queue until the next
committed snapshot materializes the area and apply after the declaration, so imperative intent
wins ties; unknown panels and malformed documents are consumed without effect.

All payloads are canonical: no-payload commands require zero words and empty data, indices/counts
must fit their documented words, offsets must be finite and non-negative, and Input data must be
valid UTF-8.

Input command `24` (`InputSetValueIfCurrent`) uses `a` for a nonzero expected revision and UTF-8
`data` for the replacement. Word `b` packs selection policy in bit 0 (`0` preserves current UTF-16
selection offsets and direction, `1` moves to end) and composition policy in bit 1 (`0` skips active
composition, `1` permits cancellation). Other bits must be zero. Preserved selection offsets clamp
forward to grapheme boundaries in the new value, or to its end. Revision zero or reserved policy
bits fail ingress validation with `-54`. This semantic command changes the schema hash, not ABI 7
or any C layout.

Delivery compares the expected revision with the retained Input on the GPUI thread; stale values
and disallowed composition are silently ignored. Ingress success only acknowledges queueing, not
replacement. Both replacement commands normalize line breaks and preserve all editing state when
the normalized value is identical, including with explicit cancellation/move-to-end policies.
Changed values clear composition and advance the revision without emitting events. Preserving
selection retains horizontal scroll until native caret reveal adjusts it; moving to end resets it.

Input event revisions are nonzero opaque tokens allocated at creation and whenever content or
composition changes, including controller writes and uncommitted IME edits. Tokens are never
reused within the native host lifetime, including after resource recreation. Selection/focus alone
do not advance them. They are not edit counts, and gaps are expected. IME changes still emit
`OnChanged` only on commit; its revision is the current token, with no extra increment for delivery.

## Native extension commands

`NativeExtensionCommand` is the generic command envelope for build-time extensions. Extension
schemas own command IDs, flags, revision policies, and payload formats; the base ABI owns only safe
routing. Extension and component identifiers are ASCII, the retained key is non-empty UTF-8 without
control characters, and the payload is an arbitrary owned byte sequence limited to 256 MiB.

```c
typedef struct gpui_native_extension_command {
    uint32_t owner_view;
    uint16_t command;
    uint16_t flags;
    uint32_t schema_version;
    uint32_t reserved;
    uint64_t schema_hash;
    uint64_t expected_revision;
    const uint8_t* extension_id;
    int32_t extension_id_length;
    const uint8_t* component_kind;
    int32_t component_kind_length;
    const uint8_t* key;
    int32_t key_length;
    const uint8_t* payload;
    int32_t payload_length;
} gpui_native_extension_command;
```

Commands may arrive before the matching extension node is materialized. They are queued by the
complete extension resource identity and delivered on the GPUI thread during materialization. A
committed snapshot that omits that identity discards both its retained native state and pending
commands. Providers validate schema-specific commands before they enter the View queue.

The editor schema defines one-shot UTF-8 bootstrap, revision-independent focus, and
revision-checked selection, whole-document replacement, and contiguous-edit commands. Editor event
`1` reports a native or command-originated document transaction with its base revision and one or
more UTF-8 replacement records; the envelope revision is the resulting document revision. Event
`2` reports a rejected state-dependent command, including its expected revision while the envelope
revision carries the current native document revision. Invalid UTF-8 byte ranges and stale
revisions are rejected without changing native state.

## Semantic window and interaction operations

`WindowControlArea` marks Div or Button nodes as native Drag, Minimize, Maximize, or Close hit-test
regions. These operations stay in the render snapshot and do not create managed pointer callbacks.

Hover and active background/text/border operations carry resolved RGBA values and apply only to
interactive components. They express transient native paint state, not durable application state.

Button, Checkbox, and Radio advertise the generated `disableable` capability. Their `Disabled`
operation is a canonical Boolean `U32`; Checkbox and Radio continue to carry controlled state in
`Checked`. Foundation activation and change requests reuse the existing click callback token and
payload, so no Rust event object crosses the ABI. Accessible names are derived natively from
descendant semantic Text nodes.

`OnKeyDown` (203), `OnKeyUp` (204), `OnMouseDown` (205), `OnMouseUp` (206),
`OnModifiersChanged` (207), `OnHover` (208), `OnMouseDownOut` (209), `OnMouseUpOut` (210),
`OnMouseMove` (211), `OnScrollWheel` (212), and `OnFileDrop` (213) are observer
callback operations requiring the generated `key_mouse` capability (Div, Button, Checkbox,
Radio). They reuse the existing `control_event` reverse channel with kinds 9-19, so this is a
semantic-only schema change: ABI version and C layouts are unchanged, only the schema hash
moves. Native listeners observe via `on_key_down`, `on_key_up`, `on_mouse_down`,
`on_mouse_up`, `on_modifiers_changed`, `on_hover`, `on_mouse_down_out`, `on_mouse_up_out`,
`on_mouse_move`, and `on_scroll_wheel`, and never call `stop_propagation`, never move focus,
and never prevent default
handling. Focused Input, Slider, List/Table navigation, Overlay Escape, and menu/context-menu
triggers therefore keep their behavior; a bound element only sees events that bubble to it.
Attach hot keys to the root container and match with `KeyEvent.Matches`; modifier-only presses
(e.g. holding Ctrl alone) arrive only as `ModifiersEvent` through `OnModifiersChanged`, since
GPUI never produces key events for bare modifiers. Hover fires on enter/exit transitions only.
Mouse movement and scroll-wheel events are only published while bound, so unregistered elements
cost nothing; registered handlers must stay cheap because these fire at pointer frequency, and
wheel observation never replaces retained Scroll resources. `OnHover` needs stable element
state, so plain Divs are wrapped with their deterministic node id for that listener only.
`OnFileDrop` fires on the bound element under the cursor with the dropped paths; like the
other observers it never consumes the drop.
These bindings are invalid inside
virtualized row snapshots, which have no mounted View lifetime.

ContextMenu and PopoverMenu are keyed two-child semantic components. Their native adapters own
trigger interception, deferred placement, viewport snapping, focus restoration, and dismissal.
They are invalid inside virtualized row snapshots because rows have no mounted View lifetime.

DockArea is a keyed retained semantic component with one center tree and up to one DockRegion for
each left, bottom, and right placement. Center and region trees use DockSplit/DockTabs/DockPanel
nodes; each panel has a unique string ID across the area, a title, and exactly one ordinary content
subtree. Initial center/region declarations, placement, open/collapsible state, and panel options
use generated semantic operations; they change the base schema hash without changing any C record
layout. Native pointer dragging, split and region resizing, region collapse, focus,
and tab activation require no per-frame managed callback. Dock controllers and retained control
events use the Dock commands and event payloads described above.

## Error handling and teardown

Zero is success; nonzero statuses have entry-point-specific meanings and negative statuses report
validation/runtime failures. Render output has no capacity-retry status. Managed exceptions are captured by the affected
window session and never unwind through native code. Normal late notifications or commands racing a
closed session are ignored only for documented closed-session statuses.

Managed diagnostics decode statuses in the context of the native operation. For example, `-30`
means `ResourceCommand.SessionMissing` during command delivery, but `Snapshot.InvalidImageObjectFit`
during render validation. A resource-command failure includes the session, owner, resource kind,
command, and UTF-8 key. Input values and extension payloads are excluded from diagnostic text.
Snapshot acknowledgement includes its revision; demand rejection includes the source and artifact.
Formatting occurs only on failure and adds no successful-call crossing or allocation.

The internal `NativeStatus` catalog preserves numeric values and reports `UnknownStatus` for
unmapped values. Some render codes are ambiguous even within that operation: `-56` can mean
duplicate retained-resource identity or invalid border style; `-63` can mean empty operation data
or wrong row count; `-64` can mean invalid font data or a missing row artifact. Diagnostics name
these alternatives rather than claiming a precise cause unavailable from the current ABI.
This diagnostic catalog does not change wire layouts, entry points, or ABI version.

All exported Rust FFI functions must validate pointer/length pairs before dereference and prevent
panics from crossing the C boundary.

Typed buffers must be aligned, their byte counts must fit Rust's slice limits, and their address
ranges must not wrap. Structural checks precede typed borrows. They cannot prove that an arbitrary
address belongs to a live allocation: the caller must provide initialized, sufficiently large
buffers and keep them alive and free of concurrent mutation for the entire native borrow. Owners
must also survive raw function-table calls; GC lifetime protection does not synchronize explicit
concurrent disposal. Fuzz malformed records inside valid allocations; arbitrary-address fuzzing
requires process isolation.

## Changing the ABI

When changing C layouts or entry points:

1. update Rust records and `GpuiDotnetApiV3` (or introduce the next table version);
2. regenerate `src/Gpui/Interop/NativeMethods.g.cs` through the native build/csbindgen path;
3. update managed size/version checks and tests;
4. update this document;
5. build and test every affected RID.

When only semantic components or operations change, edit `bindings/schema.json`, regenerate both
semantic outputs, and rely on the changed schema hash rather than hand-editing ABI records.
