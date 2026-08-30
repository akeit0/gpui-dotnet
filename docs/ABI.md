# ABI v2

ABI v2 is intentionally incompatible with v1.

## Discovery

Managed code calls:

```c
const gpui_dotnet_api_v2* gpui_dotnet_get_api(uint32_t requested_version);
```

The function table carries:

- `struct_size` for forward-safe table extension;
- `abi_version`;
- semantic `schema_hash`;
- render validation;
- application startup;
- native view notification.

Managed startup rejects ABI or schema mismatches before a window is created.

`ManagedCallbacks` contains `render`, `click`, and `shutdown`. `shutdown(session_id)` is called when the last GPUI window closes and before the native application quits, allowing managed lifecycle cleanup while the native session is still valid.

## Render records

`NodeRecord` identifies a semantic component and an optional UTF-8 payload range.

`OpRecord` contains:

```text
node       u32
code       u16
value_kind u16
a          u64
b          u64
```

`a` carries the current scalar/callback payload. `b` is reserved for operations that need a second inline word; keeping it now avoids changing record layout for common two-word semantic values later.

`ChildRecord` is a `(parent, child)` edge. The validator requires a single-use tree: one parent per non-root node, no cycles, and every node reachable from the root.

Repeated semantic operations are legal and follow fluent-builder ordering. Style/state/event materialization uses the last applicable operation for properties that collapse to one value, so `.Checked(false).Checked(true)` and repeated `.OnClick(...)` behave as last-write-wins.

## Event token

For generated click events:

```text
63                         32 31                          0
+----------------------------+-----------------------------+
| mounted managed view handle| generated local handler id  |
+----------------------------+-----------------------------+
```

Both halves reserve zero. Handles are scoped to one managed session and are never reused during that session.

Rust does not interpret the halves. It stores the token in the retained snapshot and returns it to managed code when GPUI fires the listener.

## Validation

Both managed and native validation exist for different reasons.

Managed validation catches builder bugs close to C# call sites. Native validation is authoritative at the trust boundary and checks:

- lengths/capacities and pointers;
- UTF-8 ranges/validity;
- known components and operations;
- operation value kinds;
- capability compatibility;
- finite floating-point values;
- canonical scalar payloads (unused high bits are zero);
- zeroed reserved `NodeRecord.flags` / `OpRecord.b` fields;
- non-zero event tokens;
- boolean wire values;
- child ownership, cycles, and reachability.

## Status codes

The ABI currently uses integer status codes because they are cheap across FFI. Negative values are failures; `1` is the managed render arena grow/retry signal.

Do not expose these status numbers as stable application-facing API. Managed code translates control-plane errors into exceptions and native render errors into diagnostic UI.

## ABI evolution rules

- semantic changes update `schemaVersion` and therefore `schema_hash`;
- record/function layout changes update `ABI_VERSION`;
- never expose Rust pointers, Rust trait objects, `Entity<T>`, or Rust enum layout directly to C#;
- future retained resources use opaque generational handles;
- high-volume commands should be batched rather than one P/Invoke per property.
