# Binding generation

There are two generators with different responsibilities.

## Repository semantic generator

Project: `tools/Gpui.Bindings.Generator`

Input: `bindings/schema.json`

Outputs:

- managed component IDs, operation IDs, marker tags, factories and capability-constrained fluent APIs;
- Rust IDs, component/operation metadata, capability masks and `NativeAdapter` enum;
- a shared schema hash validated at startup.

```powershell
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

`Scroll`, `List`, `Input`, `Overlay`, `Tooltip`, `ContextMenu`, and `PopoverMenu` are manual factories because they encode retained resource metadata or component-specific invariants in addition to normal semantic component identity. Styling operations are still capability-generated.

Callback operations use manual view-bound fluent APIs rather than generated delegate tokens:

```csharp
.OnClick(this, (view, _) => view.Save())
.OnClick(this, (view, e) => view.SelectRow(e), payload)
```

`payload` is written to `OpRecord.B`; it is not a delegate or GCHandle.

Operation payload legality and scalar constraints are declared beside each operation in
`bindings/schema.json`. The generator emits the payload-kind table and constraint evaluator for
both the managed and Rust validators, so adding operation validation does not require duplicating
a switch in either runtime.

## Roslyn incremental generator

Project: `src/Gpui.Generators`

### `[GpuiView]`

Generates an AOT-safe static factory implementing `IGeneratedViewFactory<T>`. Framework-owned `ui.Child<T>()` therefore requires no reflection.

Event callbacks are bound directly at the element call site through `OnClick`, `OnChanged`,
`OnSubmitted`, `OnFocusChanged`, and `OnDismiss`. Click callbacks always receive `ClickEvent`; use
`_` when the event is not needed. `ClickEvent` remains the way to receive payload and pointer context. The render
owner stores callbacks in an internal recyclable entry list. Successful render passes sweep entries
not used by the current pass, while failed passes preserve the previous bindings. A callback may
target any mounted View, and invocation checks the target's concrete type.

`Gpui.Generators` intentionally does not inspect ordinary event methods. It only generates view
factories and list-item renderers, so no event attributes or generated event dispatch overrides
are required.

### `[GpuiListItem]`

Accepted signature:

```csharp
Element Row(int index, ref RenderContext ui)
```

`Element<TTag>` return types are also accepted. Methods must be synchronous, instance, ordinary and non-generic.

The generator emits:

- a deterministic nonzero renderer ID;
- `Rows.Row` as a `ListItemRenderer` token bound to the mounted View;
- a direct switch-based `RenderListItem` override;
- diagnostics for invalid signatures, duplicate names, reserved `Rows`, and renderer-ID collisions.

A row renderer is retried if its native-owned arena grows. Treat it with the same purity requirements as `Render()`.
