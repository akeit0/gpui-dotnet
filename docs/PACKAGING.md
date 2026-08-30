# Packaging

## Consumer target

Normal C# consumers should not need Cargo or a Rust toolchain.

The intended NuGet contains:

```text
lib/net10.0/Gpui.Net.dll
analyzers/dotnet/cs/Gpui.Net.Generators.dll
runtimes/win-x64/native/gpui_dotnet_native.dll
runtimes/linux-x64/native/libgpui_dotnet_native.so
runtimes/osx-x64/native/libgpui_dotnet_native.dylib
runtimes/osx-arm64/native/libgpui_dotnet_native.dylib
```

The source generator is shipped as an analyzer so `[GpuiEvent]` works in consumer compilations without a separate package reference.

## CI native matrix

Build each RID on its natural host/toolchain. After each native job, stage the produced library:

```powershell
./eng/stage-native.ps1 -Rid win-x64 -Library path/to/gpui_dotnet_native.dll
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

Keep one NuGet while binary size remains reasonable. Split platform assets into `Gpui.Net.NativeAssets.*` packages only if package size or release cadence makes it materially useful. A premature split increases restore and version-coordination complexity.

## Custom Rust components

Do not ship third-party Rust components as arbitrary runtime-loaded dylib plugins that exchange GPUI/Rust types. Rust does not provide a stable ABI for those types and GPUI versions would be tightly coupled.

A safer future extension SDK should rebuild/link a custom native host from source at application build time, while preserving the same semantic C ABI toward managed code.
