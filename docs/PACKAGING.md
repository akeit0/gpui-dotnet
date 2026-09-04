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

The default native host is also a dependency boundary. It must not link optional provider families
merely because their managed schema packages are separate. Its Cargo dependencies and features
should select only the implementations required by the base semantic surface. A custom host owns
the additional component crates, parsers, grammars, and assets selected by its extension set.
Release packaging should record native artifact sizes and inspect the resolved dependency or link
graph so accidental provider leakage is visible. The current Dock-related size work and reference
measurements are tracked in
[GPUI_BASE_MIGRATION.md](GPUI_BASE_MIGRATION.md#default-native-host-size-and-dependency-boundary).

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

Every package embeds the standalone repository-root `NUGET_README.md` as its NuGet readme. The
repository `README.md` remains the project overview and is not copied into packages.

## NuGet release

The `Release` workflow runs when a version tag is pushed. Create a tag matching the version in
`Directory.Build.props` and push it, for example:

```sh
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

It builds and validates the package family, publishes the validated packages to NuGet.org, and
then creates the GitHub Release with generated notes and package assets. Package versions
containing `-` are marked as prereleases. The workflow can also be dispatched manually from any
ref with `validate_only` enabled for validation without publishing.

For future pull requests, apply the labels configured in `.github/release.yml`: `bug` or `fix` for
fixes, `enhancement` or `feature` for features, `documentation` or `docs` for documentation,
`breaking-change` for breaking changes, and `dependencies`, `chore`, or `ci` for maintenance.
Commit or pull-request prefixes such as `fix:` are not interpreted by GitHub's native generator
unless a separate label automation is added.

The `Release` GitHub Actions workflow builds and tests all four native RIDs on their platform
runners, assembles the package family, and verifies clean local restores and runtime loading on
Linux, macOS, and Windows, including a file-based `#:package` consumer on Windows. The package version comes from
`Directory.Build.props`; a release tag must match that version, with an optional leading `v`.

Configure a NuGet.org trusted publishing policy with:

- repository owner: `akeit0`
- repository: `gpui-dotnet`
- workflow file: `release.yml`
- environment: `nuget`

Create a GitHub environment named `nuget`, allow the `main` branch and the release tag pattern used
by the repository (for example, `v*`), and add the NuGet.org profile name—not an email address—as
its `NUGET_USER` secret. No API-key secret is needed; the publish job exchanges GitHub's OIDC token
for a short-lived key.

Pushing a version tag automatically publishes the validated packages to NuGet.org and attaches the
`.nupkg` files to the GitHub Release. An existing package version is skipped so a partial release
can be retried after the workflow is rerun.

## Consumer verification

A release pipeline should verify:

- semantic bindings are current;
- managed and native tests pass on each build host;
- every native library exports `gpui_dotnet_get_api` and reports the expected ABI/schema;
- a clean consumer project restores and launches using only NuGet packages;
- NativeAOT publishing succeeds for supported RIDs;
- no package attempts to compile Rust on the consumer machine.

## Windows application manifest

`GPUI.NET` ships a default Windows application manifest through `buildTransitive`:

```text
buildTransitive/GPUI.NET.targets
buildTransitive/GPUI.NET.Windows.manifest
```

The target applies only to Windows executable projects (`Exe`/`WinExe`). When the consumer has
not already set `ApplicationManifest` and has not set `NoWin32Manifest=true`, the package sets
`ApplicationManifest` to its bundled `GPUI.NET.Windows.manifest`. The default enables Common
Controls v6 (`Microsoft.Windows.Common-Controls` version `6.0.0.0`) and Per-Monitor V2 DPI
awareness.

Custom-manifest ownership is preserved: an explicitly configured consumer `ApplicationManifest`
is never overwritten, and `NoWin32Manifest=true` opts out of the package-provided manifest. A
custom manifest must include `Microsoft.Windows.Common-Controls` version `6.0.0.0`; without it,
Windows may fail to load the native host because the common-control API set is not activated
for the process. The sample keeps its explicit `app.manifest` because it consumes `Gpui.csproj`
through `ProjectReference`, which does not apply NuGet `buildTransitive` assets.

Windows clean-consumer validation restores a `net10.0` / `win-x64` project with no
`ApplicationManifest` from the packed `GPUI.NET` package, verifies the MSBuild
`ApplicationManifest` property points at the package-provided manifest, then builds and runs the
consumer so `NativeRuntime.Load()` proves the Common Controls v6 dependency resolves. A second
file-based `#:package GPUI.NET@<version>` consumer (`dotnet run --file app.cs`) covers the
file-based reproduction path.

## Extensions

An extension schema references `GPUI.NET.Core` but carries no native assets. Its runtime is a
custom host built with one or more Rust providers and shipped under a unique file name with its own
RID packages. Select it through `NativeRuntimeOptions.LibraryPath` and declare required extension
ID/version/hash tuples through `NativeRuntimeOptions.Extensions`. See
[EXTENSIONS.md](EXTENSIONS.md).
