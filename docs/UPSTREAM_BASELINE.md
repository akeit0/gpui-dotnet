# Native upstream baseline

GPUI.NET consumes `gpui-base` from the `external/gpui-kit` submodule and locks the
complete native dependency graph in `crates/gpui-dotnet/Cargo.lock`. The submodule gitlink is the
executable fork pin; the reviewable copy of the revision tuple is `crates/native-baseline.toml`.
Broad `gpui-component` facilities link only into the optional component host.

## GPUI Kit

- Fork: <https://github.com/akeit0/gpui-kit>
- Upstream: <https://github.com/longbridge/gpui-kit>
- Integration branch: `codex/gpui-dotnet-integration`
- Fork delta: three downstream patches plus the upstream-main sync merge

Exact revisions are not duplicated here. The `external/gpui-kit` gitlink is the
executable pin; `crates/native-baseline.toml` carries the reviewable revision tuple and
`crates/gpui-dotnet/Cargo.lock` locks the resolved dependency graph.

The submodule uses `origin` for the fork. Add `upstream` for Longbridge when refreshing the
baseline, measure `origin/main...upstream/main`, and validate the candidate revision before
updating the parent repository's gitlink.

## GPUI (`gpui-pre`)

- Source: crates-io (`gpui-pre`, matching the `gpui-base` workspace dependency)

The direct `gpui` dependency deliberately uses the same crates-io `gpui-pre` source
declaration as `gpui-base`.
`crates/gpui-dotnet/Cargo.lock` is the source of truth for the validated version.
`cargo tree --locked --manifest-path
crates/gpui-dotnet/Cargo.toml --invert gpui-pre` must resolve without an ambiguous package
error; this guards against incompatible GPUI type universes.

Consume GPUI itself unmodified; this repository does not maintain a GPUI fork or patch
Cargo's cached sources. When a missing GPUI capability has a reusable native use case, document
the limitation and an upstream proposal, then adopt it only through a validated upstream
revision. The [content-color proposal](proposals/GPUI_CONTENT_COLORS.md) records the current
single-foreground limitation and the requirements for contextual roles. GPUI Kit's separate,
narrowly scoped fork policy below remains unchanged.

## Downstream patches

| Patch | Reason | Upstream status |
|---|---|---|
| Side-aware popup positioning | Anchored tooltips and menus need generic placement, alignment, offset, viewport margin, and deferred priority controls. | PR deferred until the GPUI.NET anchored-layer migration validates the API across the remaining components. |
| Configurable editor line-number width | Hosts embedding the foundation Editor need a stable optional gutter width instead of layout shifts when the document crosses a decimal digit boundary. | PR deferred until the optional Editor component completes interaction validation. |
| Rust primitive deprecation cleanup | Enabling Tree-sitter under the current Rust toolchain exposed warnings from importing the deprecated `std::usize` module. | Include with a later editor-related upstream PR. |

Fork-only changes must stay generic, include focused tests when appropriate, and be recorded here
with their upstream issue or pull-request status. GPUI.NET ABI, FFI, callback, and managed-runtime
logic remains in this repository.

## Updating the baseline

1. Fetch the fork and upstream remotes and measure their divergence.
2. Run the relevant `gpui-base` tests in the fork.
3. Commit the fork change, check out that exact commit in the submodule, and resolve the matching
   GPUI revision.
4. Update `crates/gpui-dotnet/Cargo.lock` and `crates/native-baseline.toml` together.
   Update this document only when fork patches, upstream status, or the update process
   itself changes; do not copy revision hashes here.
5. Commit the updated submodule gitlink in GPUI.NET only after the fork commit is available from
   `origin`.
6. Run the locked dependency-graph check and the GPUI.NET native and managed verification suites.
