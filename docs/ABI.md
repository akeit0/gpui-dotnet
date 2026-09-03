# Native ABI

GPUI.NET currently uses ABI version 3. Managed startup requires an exact ABI version, a compatible
API-table prefix, all required function entries, and the semantic schema hash generated from
`bindings/schema.json`.

The ABI is an internal C contract between `GPUI.NET.Core` and a native host. Application code does
not manipulate pointers or wire records directly.

## Discovery

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

The generated base schema hash is deliberately separate from the ABI version. Component IDs,
operation IDs, capabilities, or payload constraints can change without altering C record layouts;
the hash rejects a managed/native pair built from different schemas.

An optional extension has its own ID, protocol version, and schema hash. `supports_extension`
checks that tuple before application startup. Extension-specific definitions never enter the base
schema; the generic NativeExtension node carries the tuple, component kind, retained key, and an
opaque UTF-8 configuration owned by the extension schema.

ABI version 3 adds extension commands without putting extension-specific IDs or payload layouts in
Core. A command contains its extension ID, component kind, version, schema hash, owner View, key,
numeric command and flags, expected revision, and opaque byte payload. Native code validates the
envelope and provider compatibility and copies the payload before the FFI call returns.

## Application and callbacks

`run_application(application_id, callbacks)` starts one native GPUI application. The managed
callback table provides:

- dirty root rendering;
- click dispatch;
- virtual list/table range rendering;
- owner-view preparation for a requested dynamic frame;
- retained control events (Input, Slider, Dock, and observer key/mouse);
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
    uint32_t* root);
```

Status `1` means the native-owned arena needs growth. Rust grows the requested capacities and
retries managed rendering. `Render()` must therefore be deterministic and side-effect-free.

Virtual rows use:

```c
int32_t list_render_range(
    uint64_t session_id,
    uint64_t renderer_token,
    uint32_t start,
    uint32_t count,
    gpui_render_arena* arena,
    uint32_t* root);
```

The returned root must contain exactly `count` direct row children. `count` is limited to 512. Range
rendering uses the same arena-growth retry contract as root rendering.

An active `Dynamic` wrapper asks native GPUI for another display frame. Before the corresponding
root render, native invokes `dynamic_frame(session_id, owner_view)` so managed retained fragments
for that owner and its ancestors are marked dirty. Multiple active wrappers with the same owner are
collapsed to one callback per frame.

Click and renderer tokens pack a mounted managed View handle in the high 32 bits and a generated or
registered entry ID in the low 32 bits. Click records carry a separate unmanaged `uint64_t` payload
for row or model identity.

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
- components, child counts, operation capabilities, uniqueness, and resource-key conflicts are
  validated on both sides.

The managed validator catches builder/runtime errors before FFI. Native validation remains
authoritative because a custom or mismatched managed host must not create invalid GPUI state.

## Retained control events

Retained controls emit a typed `NativeControlEvent` containing event kind, flags, native revision,
and a borrowed byte range. Managed code validates and copies the payload before returning through
FFI, so asynchronous event handlers never retain native borrowed memory.

Input events carry UTF-8 data for Changed, Submitted, and FocusChanged transitions. Slider Changed
and Released events carry one little-endian `f32`, or two ordered values when the range flag is set.
Dock LayoutChanged carries no payload; Dock LayoutExported carries the UTF-8 layout JSON requested
through the controller; Dock PanelClosed carries the UTF-8 panel id.

Control-event kinds are global: Input uses 1-3, Slider uses 4-5, Dock uses 6
(LayoutChanged), 7 (LayoutExported), and 8 (PanelClosed), observer key events use 9
(KeyDown) and 10 (KeyUp), observer mouse press events use 11 (MouseDown) and 12 (MouseUp),
observer modifier events use 13 (ModifiersChanged), hover transitions use 14 (Hover), outside
press events use 15 (MouseDownOut) and 16 (MouseUpOut), mouse movement uses 17 (Move), and
scroll-wheel movement uses 18 (Wheel), and OS file drops use 19 (Dropped).
Resource kinds are Scroll 1,
List 2, Input 3, Slider 4, and Dock 5, with the command IDs listed below. These numbers
generate from `bindings/schema.json` into both managed enums and native constants; the schema
hash covers them, so either side renumbering without the schema fails verification. Command and
event payload shapes, routing, and queueing stay hand-written: the schema owns identities,
not behavior. Describing payload layouts as separate compatibility units is open phase-10 work.

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
header (`f32` x, `f32` y) followed by NUL-separated lossy UTF-8 paths, at least one and none
empty, bounded to 1 MiB and 4096 paths; GPUI translates the platform drop into its internal
drag system, so the bound element under the cursor receives the drop. All families
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

| Resource | Commands |
|---|---|
| Scroll | ScrollToOffset, ScrollToTop, ScrollToBottom |
| List/Table row engine | ScrollToItem, Splice, Reset, Refresh |
| Input | Focus, Blur, SetValue, SelectAll |
| Slider | SetValue |
| Dock | ClosePanel, SetRegionOpen, ImportLayout, ExportLayout |

Scroll and focus/value commands apply to the retained resource directly. List structural commands
are measurement hints and are reconciled with the next managed snapshot. A hint that disagrees with
the declared datasource count falls back to a full reset. Dock commands queue until the next
committed snapshot materializes the area and apply after the declaration, so imperative intent
wins ties; unknown panels and malformed documents are consumed without effect.

Scroll and focus/value commands apply to the retained resource directly. List structural commands
are measurement hints and are reconciled with the next managed snapshot. A hint that disagrees with
the declared datasource count falls back to a full reset.

All payloads are canonical: no-payload commands require zero words and empty data, indices/counts
must fit their documented words, offsets must be finite and non-negative, and Input data must be
valid UTF-8.

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
and tab activation require no managed callback. There is no Dock resource-command or retained-
control-event packet in the current slice.

## Error handling and teardown

Zero is success, positive statuses are protocol-defined control flow such as arena growth, and
negative statuses are validation/runtime failures. Managed exceptions are captured by the affected
window session and never unwind through native code. Normal late notifications or commands racing a
closed session are ignored only for documented closed-session statuses.

All exported Rust FFI functions must validate pointer/length pairs before dereference and prevent
panics from crossing the C boundary.

## Changing the ABI

When changing C layouts or entry points:

1. update Rust records and `GpuiDotnetApiV3` (or introduce the next table version);
2. regenerate `src/Gpui/Interop/NativeMethods.g.cs` through the native build/csbindgen path;
3. update managed size/version checks and tests;
4. update this document;
5. build and test every affected RID.

When only semantic components or operations change, edit `bindings/schema.json`, regenerate both
semantic outputs, and rely on the changed schema hash rather than hand-editing ABI records.
