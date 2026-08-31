# ABI v4

ABI v4 is intentionally incompatible with previous revisions. Managed startup requests exactly version 4 and also checks the semantic schema hash.

## Discovery

```c
const gpui_dotnet_api_v4* gpui_dotnet_get_api(uint32_t requested_version);
```

`gpui_dotnet_api_v4` contains:

- `struct_size`;
- `abi_version`;
- `schema_hash`;
- `validate_render`;
- `run_application`;
- `notify_view`;
- `dispatch_command`.

The current schema hash is `0x8B91513B238A37C7`.

## Managed callbacks

The native application receives one callback table:

```c
typedef struct gpui_managed_callbacks {
    uint32_t struct_size;
    gpui_managed_render_fn render;
    gpui_managed_click_fn click;
    gpui_managed_list_render_range_fn list_render_range;
    gpui_managed_control_event_fn control_event;
    gpui_managed_shutdown_fn shutdown;
} gpui_managed_callbacks;
```

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

### Shutdown

`shutdown(session_id)` runs after the last GPUI window closes but before the native application quits. Managed Views are unmounted while native lifecycle infrastructure is still valid.

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
- `F32`/`U32` use only the low 32 bits of `a` and require `b == 0`;
- `Callback` requires nonzero `a`; `b` must be zero except for `OnClick` and `OverlayOnDismiss`, where it is the unmanaged event payload;
- UTF-8 ranges must be valid and in-bounds;
- component/operation capability constraints are validated on both managed and native sides.

`ListRenderer` is encoded as a callback-kind operation but currently uses `b == 0`.

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

Negative statuses are implementation/runtime failures. Managed callback exceptions are recorded by the managed session and are not allowed to unwind through native code. Late notifications/commands racing normal teardown are ignored by managed code for the known closed-session statuses.
