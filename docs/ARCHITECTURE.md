# Architecture

## Ownership boundary

GPUI.NET deliberately avoids a one-to-one Rust API binding.

### C# owns

- application/domain state;
- `View` composition and dirty invalidation;
- event handler logic;
- render IR construction.

### Rust owns

- `gpui::Application`, windows and GPUI entities;
- writable render arena memory lent to C#;
- validation and retained decoded snapshots;
- native component materialization;
- high-frequency interaction state and GPUI repaint behavior.

The rule is: **cross the ABI for state transitions, not for every builder call or frame operation.**

## Render plane

A dirty render is one reverse call from Rust into C#:

```text
Rust owns arena capacity
       │
       ▼
managed Render(ref RenderContext)
       │ writes directly
       ▼
NodeRecord[] / OpRecord[] / ChildRecord[] / UTF-8
       │
       ▼
Rust validate + decode
       │
       ▼
flat ValidatedSnapshot
```

A clean GPUI repaint uses `ValidatedSnapshot` and never enters .NET.

### Arena growth

Rust owns production render buffers. If managed writing discovers insufficient capacity it records required capacity and returns the grow-required status. Rust grows geometrically and calls `Render()` again.

Therefore `Render()` is retryable and must be pure.

## Managed child views

A child view has its own retained managed fragment. Its state is not flattened into the parent object.

```text
Root View
 ├─ Sidebar View   -> managed retained fragment
 └─ Dashboard View
     ├─ Counter View
     └─ Preferences View
```

Dirty propagation walks parent links so a changed child causes the necessary ancestor snapshot to be rebuilt. Unchanged child fragments are appended rather than re-executing their `Render()` methods.

Views use reference identity. A child may have only one committed parent. After a successful managed composition, views that are no longer reachable are unmounted, their event handles are invalidated, and retained fragment memory is disposed.

## Event plane

Source-generated handlers remove runtime delegate registration.

```text
Events.Increment
      │ BindClick(local handler id)
      ▼
64-bit event token
  high 32: mounted View handle
  low  32: generated handler id
      │
      ▼
Render IR OP_ON_CLICK
      │
      ▼
Rust GPUI listener
      │
      ▼
managed click(session, event_token, payload)
      │
      ▼
View.DispatchClickAsync generated switch
```

There is no per-snapshot callback-owner dictionary.

`Task` and `ValueTask` completion is observed. The GPUI synchronization context posts continuations back through the session queue. `async void` handlers are compile-time errors.

## Lifecycle/control plane

`RenderContext` and `ViewContext` intentionally have different jobs.

- `RenderContext` is stack-only and writes transient render IR.
- `ViewContext` exists only during mount/unmount and is the future home for retained native resources, subscriptions, focus handles, and window services.

Constructors should initialize ordinary managed state only.

## Semantic boundary

`bindings/schema.json` describes a GPUI.NET semantic vocabulary, not GPUI Rust traits. Components declare capabilities such as `styled`, `parent`, `interactive`, and `checkable`. Operations require a capability.

The generator turns that into:

- C# marker-tag constraints, so style APIs are generated once rather than once per component;
- Rust metadata used by validation;
- `NativeAdapter` metadata used by Rust materialization.

This lets the Rust implementation change when GPUI changes without forcing a C# API break unless the semantic behavior itself changes.

## Future control plane

Stateful controls such as text editors, virtual lists, docks, or complex tables should not serialize their high-frequency internal state into every render snapshot. They should use generational native handles and a batched command/control ABI while the render IR carries only a handle/reference.

That layer is intentionally not faked in the current vertical slice. See `NEXT_STEPS.md`.
