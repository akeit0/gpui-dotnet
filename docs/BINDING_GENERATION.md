# Binding generation

There are two separate generators with different responsibilities.

## Repository semantic generator

Project: `tools/Gpui.Bindings.Generator`

Input: `bindings/schema.json`

Outputs:

- managed component IDs, operation IDs, marker tags, factories and capability-constrained APIs;
- Rust IDs, metadata, capability masks and `NativeAdapter` enum;
- a shared schema hash checked at native startup.

Commands:

```powershell
dotnet run --project tools/Gpui.Bindings.Generator -- generate
dotnet run --project tools/Gpui.Bindings.Generator -- verify
```

The managed project runs generation before normal build preparation so schema edits do not silently leave stale checked-in protocol files.

### Schema design rule

The schema describes GPUI.NET semantics. It is not a dump of GPUI traits/methods. Keep the surface small, stable, and meaningful from C#.

## Roslyn incremental View generator

Project: `dotnet/src/Gpui.Net.Generators`

Input: application source marked with `[GpuiView]` and optionally `[GpuiEvent]`.

Output per generated View:

- `IGeneratedViewFactory<TView>` static implementation used by `ui.Child<TView>()`;
- `Events.*` accessors when event handlers exist;
- generated direct event dispatch switch.

The factory is generated in a partial declaration of the View itself, so a private/internal parameterless constructor can be used without reflection. This is compatible with NativeAOT/trimming and avoids requiring public constructors purely for framework activation.

### Why compile-time generation

- no reflection/`Activator.CreateInstance` for normal child Views;
- no delegate callback dictionary;
- no callback constructor registration;
- NativeAOT-friendly;
- handler signature diagnostics at compile time;
- deterministic numeric event IDs;
- one explicit marker (`[GpuiView]`) identifies types eligible for framework-owned child slots.

### Diagnostics

Current generator diagnostics include:

- `GPUI001`: `[GpuiView]` class is not partial;
- `GPUI002`: unsupported nested/generic generated view;
- `GPUI003`: unsupported handler signature;
- `GPUI004`: `async void` handler;
- `GPUI005`: generated handler ID collision;
- `GPUI006`: user member conflicts with generated `Events`;
- `GPUI007`: overloaded/duplicate generated event accessor name;
- `GPUI008`: `[GpuiView]` type does not derive from `View`;
- `GPUI009`: event method uses a generated-reserved name;
- `GPUI010`: generated View is abstract or lacks a parameterless constructor;
- `GPUI011`: user member conflicts with generated `CreateGpuiView`.

## Native ABI generation

`native/gpui-dotnet-native/build.rs` uses `csbindgen` to generate `Interop/NativeMethods.g.cs` from the Rust ABI definitions. Rust remains the ABI layout source of truth.

The checked-in generated file allows source inspection, but native builds regenerate it after ABI changes.
