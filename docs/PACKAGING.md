# Packaging

Normal NuGet consumers should not need Cargo or a Rust toolchain. Development builds compile the
current-platform host from source; release packaging consumes native assets built on each target
platform.

## Package graph

```text
GPUI.NET
  ├── GPUI.NET.Core
  ├── GPUI.NET.Native
  └── analyzer: Gpui.Generators.dll

GPUI.NET.Native
  ├── GPUI.NET.Native.win-x64
  ├── GPUI.NET.Native.linux-x64
  ├── GPUI.NET.Native.osx-x64
  └── GPUI.NET.Native.osx-arm64
```

`GPUI.NET.Core` contains the platform-neutral managed API, application runtime, semantic builders,
generated ABI layouts, and native loader.

`GPUI.NET.Native` is an aggregate package with no managed implementation. Each RID package contains
exactly one native host under its `runtimes/<rid>/native` path:

| RID | Native file |
|---|---|
| `win-x64` | `gpui_dotnet.dll` |
| `linux-x64` | `libgpui_dotnet.so` |
| `osx-x64` | `libgpui_dotnet.dylib` |
| `osx-arm64` | `libgpui_dotnet.dylib` |

`GPUI.NET` is the application-facing meta package. It also carries the Roslyn analyzer so
`[GpuiView]` and `[GpuiListItem]` work without an additional package reference.

## Development builds

`dotnet build` uses the `BuildGpuiNative` targets in `GPUI.NET.Core` to run Cargo for the current
machine and copy the resulting host beside managed outputs. Use the project property
`BuildGpuiNative=false` only when a compatible native binary is already being supplied.

Native-only builds are available through:

```sh
./eng/build-native.sh crates/gpui-dotnet/Cargo.toml debug
./eng/build-native.sh crates/gpui-dotnet/Cargo.toml release
```

The PowerShell equivalent is `eng/build-native.ps1`. Windows release builds locate `fxc.exe` from
the Windows SDK unless `GPUI_FXC_PATH` is set explicitly.

## Release asset staging

Build each native RID on its natural host and stage it with:

```powershell
./eng/stage-native.ps1 -Rid win-x64 -Library path/to/gpui_dotnet.dll
```

Use the corresponding RID and library name for Linux and macOS. Staged files live under
`artifacts/native/<rid>/`.

After all four assets are available:

```powershell
./eng/pack.ps1 -Configuration Release
```

The pack script refuses to continue when any expected RID asset is missing. It sets
`BuildGpuiNative=false`, packs the Core project, all RID projects, the native aggregate, and the
application meta package, verifies the native entries, and writes packages to
`artifacts/packages`.

## NuGet release

The `Release NuGet packages` GitHub Actions workflow is manual. It builds and tests all four native
RIDs on their platform runners, assembles the package family, and verifies a clean local restore.
The package version comes from `Directory.Build.props`.

Configure a NuGet.org trusted publishing policy with:

- repository owner: `akeit0`
- repository: `gpui-dotnet`
- workflow file: `release-nuget.yml`
- environment: `nuget`

Create a GitHub environment named `nuget`, restrict its deployment branches to `main`, and add the
NuGet.org profile name—not an email address—as its `NUGET_USER` secret. No API-key secret is
needed; the publish job exchanges GitHub's OIDC token for a short-lived key.

Run the workflow with `publish` disabled for a dry run and downloadable package artifact. Enable
`publish` on `main` to push the validated packages to NuGet.org; an existing package version is
skipped so a partial release can be retried.

## Consumer verification

A release pipeline should verify:

- semantic bindings are current;
- managed and native tests pass on each build host;
- every native library exports `gpui_dotnet_get_api` and reports the expected ABI/schema;
- a clean consumer project restores and launches using only NuGet packages;
- NativeAOT publishing succeeds for supported RIDs;
- no package attempts to compile Rust on the consumer machine.

Windows consumers must also embed a Common Controls v6 application manifest in the executable;
the sample's `app.manifest` is the reference. Without it, Windows may fail to load the native host
because the common-control API set is not activated for the process.

## Extensions

An extension can reference `GPUI.NET.Core` and supply a compatible host with its own file name and
RID assets. Select it through `NativeRuntimeOptions.LibraryPath`. The custom host must satisfy the
same API table, ABI version, and schema hash. See [EXTENSIONS.md](EXTENSIONS.md).
