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

## Roslyn incremental event generator

Project: `dotnet/src/Gpui.Net.Generators`

Input: application source marked with `[GpuiView]` and `[GpuiEvent]`.

Output: per-view generated event binding/dispatch source.

Why compile-time generation:

- no reflection;
- no delegate dictionary;
- no constructor registration;
- NativeAOT-friendly;
- handler signature diagnostics at compile time;
- deterministic numeric event IDs.

### Diagnostics

Current generator diagnostics include:

- `GPUI001`: `[GpuiView]` class is not partial;
- `GPUI002`: unsupported nested/generic generated view;
- `GPUI003`: unsupported handler signature;
- `GPUI004`: `async void` handler;
- `GPUI005`: generated handler ID collision;
- `GPUI006`: user member conflicts with generated `Events`.

## Native ABI generation

`native/gpui-dotnet-native/build.rs` uses `csbindgen` to generate `Interop/NativeMethods.g.cs` from the Rust ABI definitions. Rust remains the ABI layout source of truth.

The checked-in generated file allows source inspection, but native builds regenerate it after ABI changes.
