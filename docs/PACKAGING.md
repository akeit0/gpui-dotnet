# Packaging

## Consumer target

Normal C# consumers should not need Cargo or a Rust toolchain.

The intended NuGet contains:

```text
lib/net10.0/Gpui.dll
lib/net10.0/Gpui.Core.dll
lib/net10.0/Gpui.Runtime.dll
analyzers/dotnet/cs/Gpui.Generators.dll
runtimes/win-x64/native/gpui_dotnet.dll
runtimes/linux-x64/native/libgpui_dotnet.so
runtimes/osx-x64/native/libgpui_dotnet.dylib
runtimes/osx-arm64/native/libgpui_dotnet.dylib
```

`Gpui.dll` remains the compatibility facade for the original public assembly identity. The
platform-neutral render API and ABI live in `Gpui.Core.dll`; native loading, application/window
hosting, callbacks, and managed sessions live in `Gpui.Runtime.dll`. They are shipped together in
the single `Gpui` package, so consumers still need only one package reference.

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

`eng/pack.ps1` refuses to package if one of the four expected native assets is missing.

## Development build vs package build

Repository development builds compile the host native library automatically and copy it beside the sample executable.

Package assembly sets `BuildGpuiNative=false`; it consumes the already-built RID matrix rather than trying to cross-compile every GPUI target from one machine.

## Future package split

Keep one NuGet while binary size remains reasonable. Split platform assets into `Gpui.NativeAssets.*` packages only if package size or release cadence makes it materially useful. A premature split increases restore and version-coordination complexity.

## Custom Rust components

Do not ship third-party Rust components as arbitrary runtime-loaded dylib plugins that exchange GPUI/Rust types. Rust does not provide a stable ABI for those types and GPUI versions would be tightly coupled.

A safer future extension SDK should rebuild/link a custom native host from source at application build time, while preserving the same semantic C ABI toward managed code.
