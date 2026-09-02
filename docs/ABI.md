# Native ABI

GPUI.NET currently uses ABI version 2. Managed startup requires an exact ABI version, a compatible
API-table prefix, all required function entries, and the semantic schema hash generated from
`bindings/schema.json`.

The ABI is an internal C contract between `GPUI.NET.Core` and a native host. Application code does
not manipulate pointers or wire records directly.

## Discovery

```c
const gpui_dotnet_api_v2* gpui_dotnet_get_api(uint32_t requested_version);
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

The generated base schema hash is deliberately separate from the ABI version. Component IDs,
operation IDs, capabilities, or payload constraints can change without altering C record layouts;
the hash rejects a managed/native pair built from different schemas.

An optional extension has its own ID, protocol version, and schema hash. `supports_extension`
checks that tuple before application startup. Extension-specific definitions never enter the base
schema; the generic NativeExtension node carries the tuple, component kind, retained key, and an
opaque UTF-8 configuration owned by the extension schema.

## Application and callbacks

`run_application(application_id, callbacks)` starts one native GPUI application. The managed
callback table provides:

- dirty root rendering;
- click dispatch;
- virtual list/table range rendering;
- owner-view preparation for a requested dynamic frame;
- retained control events;
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
and the global `gpui-component` and `gpui-base` themes; application style variants and Rust
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

Scroll and focus/value commands apply to the retained resource directly. List structural commands
are measurement hints and are reconciled with the next managed snapshot. A hint that disagrees with
the declared datasource count falls back to a full reset.

All payloads are canonical: no-payload commands require zero words and empty data, indices/counts
must fit their documented words, offsets must be finite and non-negative, and Input data must be
valid UTF-8.

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

1. update Rust records and `GpuiDotnetApiV2` (or introduce the next table version);
2. regenerate `src/Gpui/Interop/NativeMethods.g.cs` through the native build/csbindgen path;
3. update managed size/version checks and tests;
4. update this document;
5. build and test every affected RID.

When only semantic components or operations change, edit `bindings/schema.json`, regenerate both
semantic outputs, and rely on the changed schema hash rather than hand-editing ABI records.
