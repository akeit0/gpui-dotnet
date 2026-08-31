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

`Scroll`, `List`, `Input`, `Overlay`, and `Tooltip` are manual factories because they encode retained resource metadata or component-specific invariants in addition to normal semantic component identity. Styling operations are still capability-generated.

For callback operations the generated managed fluent API has two forms:

```csharp
.OnClick(Events.Save)
.OnClick(Events.SelectRow, payload)
```

`payload` is written to `OpRecord.B`; it is not a delegate or GCHandle.

## Roslyn incremental generator

Project: `dotnet/src/Gpui.Net.Generators`

### `[GpuiView]`

Generates an AOT-safe static factory implementing `IGeneratedViewFactory<T>`. Framework-owned `ui.Child<T>()` therefore requires no reflection.

### `[GpuiEvent]`

Accepted signatures:

```csharp
void Handler()
void Handler(ClickEvent e)
void Handler(InputEvent e)
Task Handler()
Task Handler(ClickEvent e)
Task Handler(InputEvent e)
ValueTask Handler()
ValueTask Handler(ClickEvent e)
ValueTask Handler(InputEvent e)
```

The generator emits:

- nonzero deterministic handler IDs;
- `Events.Handler` callback tokens;
- direct switch-based `DispatchClickAsync` and `DispatchInputAsync` overrides as needed;
- diagnostics for invalid/duplicate/reserved handlers and ID collisions.

`async void` is rejected.

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
