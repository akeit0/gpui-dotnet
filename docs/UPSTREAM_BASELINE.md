# Native upstream baseline

GPUI.NET consumes `gpui-base` from the `external/gpui-component` submodule and locks the complete
native dependency graph in `crates/gpui-dotnet/Cargo.lock`. The submodule gitlink is the executable
fork pin; the reviewable copy of the revision tuple is `crates/native-baseline.toml`.

## GPUI Component

- Fork: <https://github.com/akeit0/gpui-component>
- Upstream: <https://github.com/longbridge/gpui-component>
- Validated fork revision: `40566467b3810362508145e019a2f3c873486a8a`
- Upstream base revision: `d6c10c21a58617b29494f8efeba4895ca384e465`
- Fork delta: one commit
- Integration branch: `codex/gpui-dotnet-integration`

The submodule uses `origin` for the fork. Add `upstream` for Longbridge when refreshing the
baseline, measure `origin/main...upstream/main`, and validate the candidate revision before
updating the parent repository's gitlink.

## Zed / GPUI

- Upstream: <https://github.com/zed-industries/zed>
- Validated revision: `f66ed399cdde86092af8af3dc7b418abf45f37f8`

The direct `gpui` dependency deliberately uses the same Git source declaration as `gpui-base`.
`Cargo.lock` selects the validated revision. `cargo tree --locked --manifest-path
crates/gpui-dotnet/Cargo.toml --invert gpui` must resolve without an ambiguous package error; this
guards against incompatible GPUI type universes.

## Downstream patches

| Patch | Reason | Upstream status |
|---|---|---|
| Side-aware popup positioning | Anchored tooltips and menus need generic placement, alignment, offset, viewport margin, and deferred priority controls. | PR deferred until the GPUI.NET anchored-layer migration validates the API across the remaining components. |

Fork-only changes must stay generic, include focused tests when appropriate, and be recorded here
with their upstream issue or pull-request status. GPUI.NET ABI, FFI, callback, and managed-runtime
logic remains in this repository.

## Updating the baseline

1. Fetch the fork and upstream remotes and measure their divergence.
2. Run the relevant `gpui-base` tests in the fork.
3. Commit the fork change, check out that exact commit in the submodule, and resolve the matching
   GPUI revision.
4. Update `Cargo.lock`, `crates/native-baseline.toml`, and this document together.
5. Commit the updated submodule gitlink in GPUI.NET only after the fork commit is available from
   `origin`.
6. Run the locked dependency-graph check and the GPUI.NET native and managed verification suites.
