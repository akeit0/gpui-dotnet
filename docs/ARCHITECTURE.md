# Architecture

## Ownership boundary

GPUI.NET deliberately avoids a one-to-one Rust API binding.

### C# owns

- application/domain state;
- managed `View` state and declarative composition;
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

## Managed View composition

A managed child View owns local C# state and a retained managed render fragment. It is appropriate when the subtree needs independent state, lifecycle, invalidation, events, or future native entities. Pure visual decomposition should remain an Element/helper rather than automatically becoming a View.

### Framework-owned children

Framework ownership is the default:

```csharp
ui.Child<SidebarView>()
ui.Child<CounterView>("counter")
```

`[GpuiView]` generates an AOT-safe static factory. The parent runtime retains the created View by child slot; application code does not need a field.

A parent has exactly one ownership edge to each child View. A child instance has at most one parent. This makes invalidation propagation a direct parent-pointer walk rather than a general graph traversal.

### Slot identity is separate from View type

```text
Parent View
 ├─ slot key:"sidebar"  -> SidebarView
 └─ slot key:"content"  -> DashboardView
```

The next render may produce:

```text
Parent View
 ├─ slot key:"sidebar"  -> SidebarView     (same instance)
 └─ slot key:"content"  -> SettingsView    (replacement)
```

The key identifies the location; the requested type is the value occupying that location. Same slot + same framework View type retains the existing instance. Same slot + different type creates a replacement and makes the old subtree unreachable after commit.

Unkeyed children use positional View slots. Keyed and unkeyed slots are distinct. Use keys whenever relative child order can change or identity matters independently of call position.

### Dynamic route/tab content

Runtime selection among different View types is ordinary composition:

```csharp
var page = _route switch
{
    Route.Home => ui.Child<HomeView>("content"),
    Route.Settings => ui.Child<SettingsView>("content"),
    Route.Editor => ui.Child<EditorView>("content"),
    _ => throw new InvalidOperationException(),
};
```

The View graph is therefore not statically fixed even though each factory is source-generated.

### Explicit ownership

`ui.Child(existingView, key)` is the escape hatch for imperative access. The application keeps the object reference; the framework still controls whether it is mounted based on tree membership. Reusing one View instance under multiple parents is rejected.

## Retained composition staging

Managed child fragments can successfully render before the root native arena later asks for growth. Re-running `OnMounted` or recreating a new child on each growth retry would be incorrect and expensive.

The runtime therefore separates:

```text
committed children     tree currently owned by the successful snapshot
candidate children     instances resolved during a render attempt/retry
working children       slots used by the currently executing Render()
staged children        composition matching a successfully rendered retained fragment
```

A newly resolved child may mount before the root commit because its `OnMounted` state can be required by its first `Render()`. Candidates are retained across native arena growth retries, so mount happens once. A successful root validation publishes staged child maps atomically at the managed tree level, then unreachable candidates/old subtrees are unmounted child-first.

Hard render failures terminate the session and normal session cleanup unmounts every attached candidate.

## Props

Constructor parameters are not the normal parent-to-child data path. `View<TProps>` stores the latest parent-supplied inputs:

```text
parent retained state
      │
      │ TProps
      ▼
child View<TProps>
      ├ local retained C# state
      └ retained render fragment
```

Props are applied before first mount. For retained children, a props change increments only that child's fragment version. Child invalidation still propagates through its parent chain so ancestors that copy the child fragment rebuild as necessary.

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

- `RenderContext` is stack-only and writes transient render IR or resolves declarative child slots.
- `ViewContext` exists only during mount/unmount and is the future home for retained native resources, subscriptions, focus handles, and window services.

Constructors should initialize ordinary managed state only. Child View construction is normally generated and deferred until its slot is first used.

## Semantic boundary

`bindings/schema.json` describes a GPUI.NET semantic vocabulary, not GPUI Rust traits. Components declare capabilities such as `styled`, `parent`, `interactive`, and `checkable`. Operations require a capability.

The repository generator turns that into:

- C# marker-tag constraints, so style APIs are generated once rather than once per component;
- Rust metadata used by validation;
- `NativeAdapter` metadata used by Rust materialization.

This lets the Rust implementation change when GPUI changes without forcing a C# API break unless the semantic behavior itself changes.

## Future control plane

Stateful controls such as text editors, virtual lists, docks, or complex tables should not serialize their high-frequency internal state into every render snapshot. They should use generational native handles and a batched command/control ABI while the render IR carries only a handle/reference.

That layer is intentionally not faked in the current vertical slice. See `NEXT_STEPS.md`.
