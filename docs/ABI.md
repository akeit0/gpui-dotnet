# ABI v5

ABI v5 separates one application identity from its independently mounted window/render session identities. Managed startup requests exactly version 5 and also checks the semantic schema hash.

## Discovery

```c
const gpui_dotnet_api_v5* gpui_dotnet_get_api(uint32_t requested_version);
```

`gpui_dotnet_api_v5` contains:

- `struct_size`;
- `abi_version`;
- `schema_hash`;
- `validate_render`;
- `run_application`;
- `notify_view`;
- `dispatch_command`;
- `dispatch_application_command`.

The current schema hash is `0x66378CABCEE9092C`.

## Managed callbacks

The native application receives one callback table:

```c
typedef struct gpui_managed_callbacks {
    uint32_t struct_size;
    gpui_managed_render_fn render;
    gpui_managed_click_fn click;
    gpui_managed_list_render_range_fn list_render_range;
    gpui_managed_control_event_fn control_event;
    gpui_managed_application_started_fn application_started;
    gpui_managed_window_closed_fn window_closed;
} gpui_managed_callbacks;
```

### Application/window lifecycle

```c
int32_t run_application(
    uint64_t application_id,
    const gpui_managed_callbacks* callbacks);

int32_t application_started(uint64_t application_id);

int32_t window_closed(
    uint64_t application_id,
    uint64_t window_id,
    int32_t native_status);
```

Native registration exists before `application_started` runs, so managed code can synchronously enqueue every initial window. `window_id` is also the session ID passed to render/event callbacks and the notification/resource-command functions. `window_closed` unmounts only that session; the native application exits after the final registered window closes.

### Dirty root render

```c
int32_t render(
    uint64_t session_id,
    gpui_render_arena* arena,
    uint32_t* root);
```

`1` means the arena needs native-side growth and the managed render may be retried. Therefore managed `Render()` must be deterministic and side-effect-free.

### Click

```c
int32_t click(
    uint64_t session_id,
    uint64_t event_token,
    uint64_t event_payload,
    const gpui_click_event* event);
```

`event_token` is generated identity:

```text
63                       32 31                         0
+--------------------------+---------------------------+
| mounted managed View ID  | generated handler ID      |
+--------------------------+---------------------------+
```

`event_payload` is an application-defined unmanaged `ulong` captured in the render IR. It is normally zero. Virtualized rows use it for row index/model identity without a managed closure.

### Virtual list range render

```c
int32_t list_render_range(
    uint64_t session_id,
    uint64_t renderer_token,
    uint32_t start,
    uint32_t count,
    gpui_render_arena* arena,
    uint32_t* root);
```

`renderer_token` has the same View-ID/high-word structure as event tokens. One call returns a synthetic root whose direct children are exactly `count` row roots. `count` is currently limited to 512. Rust uses aligned batches even though GPUI's item closure asks for individual indices.

List range rendering is also retryable when the native-owned arena must grow, so `[GpuiListItem]` is subject to the same pure-render rule.

### Native control event

```c
int32_t control_event(
    uint64_t session_id,
    uint64_t event_token,
    const gpui_native_control_event* event);
```

`gpui_native_control_event` carries a typed event kind, flags, native state revision, and a borrowed UTF-8 data range. Input currently emits Changed, Submitted, and FocusChanged. Managed code validates the record and copies the bytes before returning through FFI, making `InputEvent.Utf8Value` safe for asynchronous handlers. UTF-16 decoding is deferred until `InputEvent.Value` is accessed.

## Render arena

The ABI stays pointer-free from managed application code; only the runtime layer sees arena pointers. The wire structures remain C-layout fixed-width records.

`OpRecord` is:

```c
typedef struct gpui_op_record {
    uint32_t node;
    uint16_t code;
    uint16_t value_kind;
    uint64_t a;
    uint64_t b;
} gpui_op_record;
```

Canonical rules:

- `node.flags == 0`;
- `None` operations require `a == 0 && b == 0`;
- `F32`/`U32` use only the low 32 bits of `a`, `U64` uses all 64 bits, and each requires `b == 0`;
- `Callback` requires nonzero `a`; `b` must be zero except for `OnClick` and `OverlayOnDismiss`, where it is the unmanaged event payload;
- UTF-8 ranges must be valid and in-bounds;
- component/operation capability constraints are validated on both managed and native sides.

`ListRenderer` is encoded as a callback-kind operation but currently uses `b == 0`.

## Application/window commands

Application commands use a separate application-scoped channel:

```c
typedef struct gpui_native_application_command {
    uint64_t window_id;
    uint16_t command;
    uint16_t flags;
    uint32_t reserved;
    const uint8_t* title;
    int32_t title_length;
    uint32_t reserved2;
    float left;
    float top;
    float width;
    float height;
} gpui_native_application_command;
```

| Command | Payload |
|---|---|
| `Open` | UTF-8 title, positive size, optional positioned flag + finite `left`/`top`, optional activate flag, title-bar style |
| `Close` | none |
| `Activate` | none |
| `Minimize` | none |
| `ToggleMaximize` | none |
| `SetTitle` | non-empty UTF-8 title |
| `Resize` | positive finite width/height |

For `Open`, flag bit 0 means the position fields are present, bit 1 requests activation, and bits 2–3 encode `System = 0`, `Custom = 1`, or `Hidden = 2`. All higher bits and the unused value 3 are rejected. `Custom` uses a transparent platform title-bar surface with client decorations; `Hidden` removes the title-bar surface and keeps the UTF-8 title as the native window/task-switcher title.

The call copies borrowed UTF-8 before returning and enqueues the owned command. All GPUI window mutation occurs on the native UI thread. Initial position is supported by `Open`; runtime resize is supported, but runtime reposition is absent because the pinned GPUI API has no durable cross-platform operation for it. The command record layout remains ABI v5; the schema hash rejects native/managed pairs that disagree about the extended flag semantics.

## Native window-control regions

Semantic operation `WindowControlArea` (`201`, `U32`) marks a Div or Button as a native title-bar hit-test region. Values are `Drag = 0`, `Minimize = 1`, `Maximize = 2`, and `Close = 3`; all others are rejected by both managed and native validation. Child regions override an enclosing drag region through normal GPUI hit testing.

These regions are part of the render snapshot, not managed click callbacks. GPUI and the platform process drag and caption interaction directly, so pointer motion and window-management gestures do not cross the ABI. Right-click on an unwrapped Drag region remains platform-owned and presents the operating system window menu where supported.

`Minimize` and `ToggleMaximize` use the application command channel rather than semantic hit-test regions. The native host normalizes maximize/restore behavior across platforms; in particular, it explicitly restores a maximized Win32 window because the pinned GPUI Windows `zoom_window` implementation only maximizes.

## Context menu component

Semantic component `ContextMenu` (`15`) is keyed and requires exactly two children: a normal-layout trigger followed by deferred menu content. `ContextMenuPriority` (`620`, `U32`) and `ContextMenuMarginPx` (`621`, finite non-negative `F32`) configure its native layer; defaults are priority 300 and an 8-pixel viewport margin.

Rust owns right-button interception, pointer anchoring, viewport snapping, focus entry/restoration, a transparent full-window input backdrop, and dismissal. Right mouse-down on the trigger stops propagation and suppresses the platform default before opening. Outside left/right press and Escape close immediately. A left mouse-up inside content closes after the target's normal click sequence, so managed `OnClick` actions run before the menu disappears. Menu presentation and actions remain ordinary managed semantic elements.

ContextMenu state is scoped by `(window session, managed View handle, local UTF-8 key)`. It is rejected in virtualized List row snapshots, which do not have a mounted View lifetime.

Semantic component `PopoverMenu` (`16`) is keyed and requires exactly two children: a normal-layout trigger followed by deferred menu content. `PopoverMenuPriority` (`630`, `U32`) and `PopoverMenuMarginPx` (`631`, finite non-negative `F32`) configure its native layer; defaults are priority 300 and an 8-pixel viewport margin.

The native adapter measures the trigger, opens on left pointer-down, attaches content below the trigger's leading edge, snaps it inside the viewport, and owns selection/outside/Escape dismissal plus focus restoration. While a PopoverMenu is open, hovering another PopoverMenu in the same View transfers the active menu and preserves the original focus-restoration target. PopoverMenu uses the owning View namespace and is rejected in virtualized List rows.

`HoverBackgroundRgba` (`113`, canonical `U32`) applies only to interactive components and changes their native hover fill without callbacks. It is independent from ContextMenu and is used by custom caption controls and ordinary application buttons.

## Retained native resource commands

Scroll, List, and Input state are not rebuilt from managed objects. Commands use:

```c
typedef struct gpui_native_resource_command {
    uint32_t owner_view;
    uint16_t resource_kind;
    uint16_t command;
    const uint8_t* key;
    int32_t key_length;
    const uint8_t* data;
    int32_t data_length;
    uint32_t reserved;
    uint64_t a;
    uint64_t b;
} gpui_native_resource_command;
```

Resource identity is `(owner_view, UTF-8 key)` and the kind is validated independently.

Current commands:

| Kind | Command | Payload |
|---|---|---|
| Scroll | `ScrollToOffset` | `a=x f32 bits`, `b=y f32 bits` |
| Scroll | `ScrollToTop` | none |
| Scroll | `ScrollToBottom` | none |
| List | `ScrollToItem` | `a=index` |
| List | `Splice` | `a=start`, high/low halves of `b` = removed/inserted |
| List | `Reset` | `a=itemCount` |
| List | `Refresh` | `a=start`, `b=count` |
| Input | `Focus` | none |
| Input | `Blur` | none |
| Input | `SetValue` | UTF-8 `data` |
| Input | `SelectAll` | none |

Commands are sent through the native UI message channel and handled on the GPUI thread. Scroll commands and `List.ScrollToItem` apply immediately. `List.Splice/Reset/Refresh` are measurement-preservation hints: when a List already exists they are queued and committed only with the next managed snapshot revision, so they cannot race materialization of the previous declarative `itemCount`. A hint/snapshot mismatch falls back to a full `ListState.reset`.

Command payloads are canonicalized too: scalar indices/counts use only their documented 32-bit words, no-payload commands require zero payload words and empty data, scroll offsets must be finite non-negative `f32` values, and Input value data must be valid UTF-8. The UTF-8 `SetValue` managed overload pins the caller span only for this synchronous call; Rust copies it into retained native storage before returning.

## Error handling

Negative statuses are implementation/runtime failures. Managed callback exceptions are recorded by the affected window session and are not allowed to unwind through native code. One failed window does not tear down the other windows, but the application returns the first recorded failure after final-window shutdown. Late notifications/resource commands racing normal teardown are ignored for known closed-session statuses.
