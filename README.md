# GPUI.NET

GPUI.NET is an experimental .NET frontend for Rust [GPUI](https://gpui.rs/). C# owns durable application state and publishes compact render IR; Rust owns GPUI, native render state, event delivery, and frame-sensitive work.

This repository intentionally uses a narrow semantic ABI instead of mirroring GPUI's Rust API. GPUI can evolve without forcing equivalent churn into every C# application.

## Current architecture

```text
C# View state
   │
   │ dirty render only
   ▼
RenderArena (nodes / ops / children / UTF-8)
   │
   ▼
Rust validation + flat retained snapshot
   │
   ▼
semantic NativeAdapter
   │
   ▼
GPUI elements
```

Clean GPUI repaints reuse the retained Rust snapshot and do not call managed `Render()`.

## C# API

Event fields and constructor registration are intentionally gone. Mark a partial view and its handlers; the Roslyn incremental generator emits stable local handler IDs, allocation-free `Events.*` bindings, and direct dispatch code.

```csharp
[GpuiView]
internal sealed partial class CounterView : View
{
    private int _count;

    [GpuiEvent]
    private async ValueTask Increment(ClickEvent _)
    {
        await Task.Delay(100);
        _count++;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.VStack(
                ui.Text($"Count: {_count}"),
                ui.Button("increment", "Increment")
                    .OnClick(Events.Increment)
                    .Padding(Px(10)))
            .Gap(Px(16));
}
```

Generated callbacks are materialized only while a view is mounted. Do not create them in constructors or field initializers. Use `Events.Handler` inside `Render()`.

Lifecycle-specific work belongs in `OnMounted` / `OnUnmounted`, not in constructors or `Render()`:

```csharp
protected override void OnMounted(ref ViewContext context)
{
    // Future retained native entities, subscriptions, focus resources, etc.
}
```

`Render()` is a pure description function. Arena growth can retry it, so it must not mutate view state, start tasks, perform I/O, register handlers, or call `Invalidate()`.

## What changed in ABI v2

- click callbacks carry a 64-bit event token: high 32 bits = mounted view handle, low 32 bits = generated handler ID;
- no managed callback dictionary or snapshot callback-owner rebuild;
- async `Task` / `ValueTask` handlers are observed by the runtime; `async void` is rejected by the generator;
- managed child views use reference identity, detach child-first, and unmount on native shutdown before GPUI quits;
- the semantic schema is capability-based (`styled`, `parent`, `interactive`, etc.);
- Rust snapshots store flat op/child ranges and reuse validation/grouping scratch buffers;
- Rust component behavior is selected by generated `NativeAdapter` metadata.

## Build

Prerequisites:

- .NET 10 SDK (pinned in `global.json`)
- current Rust toolchain with Cargo
- platform prerequisites required by GPUI

```powershell
dotnet build gpui-dotnet.slnx
dotnet run --project dotnet/samples/Gpui.Net.Sample
```

To force native arena growth/retry:

```powershell
dotnet run --project dotnet/samples/Gpui.Net.Sample -- --stress-growth
```

Allocation probe:

```powershell
dotnet run -c Release --project dotnet/samples/Gpui.Net.Bench
```

## Binding generation

`bindings/schema.json` is the semantic protocol source of truth. It generates both:

- `dotnet/src/Gpui.Net/Rendering/Semantic.g.cs`
- `native/gpui-dotnet-native/src/semantic.g.rs`

```powershell
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

This repository generator is separate from `Gpui.Net.Generators`, the Roslyn incremental generator that compiles `[GpuiEvent]` methods into event dispatch code in application projects.

## Adding a Rust-side component

For a snapshot component:

1. add its semantic component entry to `bindings/schema.json`;
2. assign capabilities and a `nativeAdapter` name;
3. regenerate semantic bindings;
4. implement the adapter branch in `native/gpui-dotnet-native/src/components.rs`;
5. add only semantic operations that C# should depend on;
6. test validation/materialization and benchmark the dirty-render path.

Do not expose GPUI trait/layout details directly through the ABI. See [docs/COMPONENTS.md](docs/COMPONENTS.md).

## Packaging

The intended distribution is one managed NuGet containing the source generator plus RID-specific native assets:

```text
Gpui.Net.nupkg
  lib/net10.0/Gpui.Net.dll
  analyzers/dotnet/cs/Gpui.Net.Generators.dll
  runtimes/win-x64/native/gpui_dotnet_native.dll
  runtimes/linux-x64/native/libgpui_dotnet_native.so
  runtimes/osx-x64/native/libgpui_dotnet_native.dylib
  runtimes/osx-arm64/native/libgpui_dotnet_native.dylib
```

CI builds each native RID independently, stages artifacts with `eng/stage-native.ps1`, then creates the package with `eng/pack.ps1`.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [ABI](docs/ABI.md)
- [Components](docs/COMPONENTS.md)
- [View lifecycle and generated events](docs/VIEW_LIFECYCLE.md)
- [Binding generation](docs/BINDING_GENERATION.md)
- [Performance contract](docs/PERFORMANCE.md)
- [Packaging](docs/PACKAGING.md)
- [Next steps](docs/NEXT_STEPS.md)
