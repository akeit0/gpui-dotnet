# Packaging

## Package family

Normal C# consumers should not need Cargo or a Rust toolchain.

The package family is:

```text
Gpui           -> Gpui.Core + Gpui.Runtime
Gpui.Core     -> Utf8StringInterpolation + managed ABI layouts
Gpui.Runtime  -> RID-specific native assets only
```

The platform-neutral render API, managed application runtime, and generated ABI layouts live in
`Gpui.Core.dll`; `Gpui.Runtime` has no managed assembly and ships only native assets. Installing
`Gpui` still gives normal consumers one package reference through NuGet dependencies, while
extension packages can select the managed/native pieces they need.

The source generator is shipped as an analyzer so `[GpuiView]` and `[GpuiListItem]` work in consumer compilations without a separate package reference. Event callbacks are runtime view-bound fluent APIs and require no event attribute.

## CI native matrix

Build each RID on its natural host/toolchain. After each native job, stage the produced library:

```powershell
./eng/stage-native.ps1 -Rid win-x64 -Library path/to/gpui_dotnet.dll
```

Equivalent calls are used for Linux and macOS artifacts. The final packaging job downloads all staged `artifacts/native/<rid>/...` files and runs:

```powershell
./eng/pack.ps1 -Configuration Release
```

`eng/pack.ps1` refuses to package if one of the four expected native assets is missing. It emits
`Gpui`, `Gpui.Core`, and the native-only `Gpui.Runtime` packages.

## Development build vs package build

Repository development builds compile the host native library automatically and copy it beside the sample executable.

Package assembly sets `BuildGpuiNative=false`; it consumes the already-built RID matrix rather than trying to cross-compile every GPUI target from one machine.

## Extensions and custom Rust hosts

Future extension packages can reference `Gpui` and ship their managed APIs, generators, semantic
components, and RID-specific native assets independently. See
[EXTENSIONS.md](EXTENSIONS.md) for the custom-host contract.

An application may select an extension-owned native host with
`new NativeRuntimeOptions { LibraryPath = ... }`. The host must export the versioned
`gpui_dotnet_get_api` entry point and pass the managed ABI/schema validation. Rust/GPUI types do
not cross the boundary directly; this leaves custom Rust implementations possible without
committing the package model to a single native binary.
