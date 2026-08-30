# Next steps

The current repository now has the structural pieces needed before expanding the component surface: ABI event tokens, lifecycle hooks, generated event dispatch, capability-based schema, native adapters, view detachment, and flat native snapshots.

## 1. Retained native entity/control ABI

Implement opaque generational handles for native GPUI state. Required primitives should be batch-oriented, for example:

```text
entity_create(type_id, init payload) -> handle
entity_commands(command arena)
entity_release(handle)
```

Do not expose Rust pointers or `Entity<T>` layout.

Use a text input/editor as the first proof because it forces correct decisions around focus, IME, selection, native subscriptions, and managed change events.

## 2. Virtualized datasource ABI

Implement list/table visible-range requests as batches. Rust owns virtualization and scroll state; C# supplies model rows for requested ranges. Event tokens should combine generated handler IDs with unmanaged row payloads rather than creating one managed closure per row.

## 3. Multi-window application model

Lift the current one-window startup ABI into an application/session model with explicit window handles. Keep `GpuiApplication.Run(new MainView())` only if a convenience wrapper is desired for the new model; the underlying ownership should be app → windows → managed roots.

## 4. Diagnostics/analyzers

Add semantic analyzers beyond generated handler signatures, especially:

- suspicious state mutation inside `Render()`;
- `Invalidate()` from `Render()`;
- storing `Element` outside render scope where detectable;
- native entity use before mount or after unmount;
- duplicate semantic keys where a component requires uniqueness.

## 5. Native fragment references

Profile large child-view trees. If copying retained managed fragments into each root arena becomes material, add a native retained-fragment/reference opcode rather than optimizing blindly.

## 6. Platform/release verification

Run the same integration and performance suite on win-x64, linux-x64, osx-x64, and osx-arm64. Publish a NuGet only after the RID assets are produced from the same source/schema hash and native ABI version.
