# MoonBit retained tree (segments)

Status: phase 0 implemented and used by the counter sample. Segment splice
(`Frame::append_segment`), revision-keyed section cache (`Frame::section`), prune-on-accept,
and `SectionHandle::invalidate` are implemented in `moonbit/retained.mbt` with whitebox coverage
in `moonbit/runtime_wbtest.mbt`. No ABI or native-host change. `SectionHandle` drops the cache
entry and notifies; per-subtree dirty flags beyond revision comparison remain future work.

Correctness discipline: `ListController` refresh/splice/reset re-request ranges, so a list inside
a retained section needs a section revision bump (or handle invalidation) alongside the command,
or reused range closures keep stale snapshots. Structural commands additionally change the
declared item count, which only a section re-render republishes.

## Problem

There is no retained View tree. Each window owns one render closure, and any
`ctx.invalidate()` / `Window::invalidate()` re-runs that window's whole root render: a small
change in one window costs a full render of that window (`docs/MOONBIT.md` states this openly
under "Known limitation: full root re-render"). Invalidation is scoped per window session plus
list batch commands only.

## Non-goals

- No View inheritance, source-generated factories, Signals, effects, or managed Task scheduler.
  The API stays closure-first, consistent with the frontend boundary in `docs/MOONBIT.md`.
- No wire-protocol change and no native partial materialization in this phase. The native host
  keeps receiving one complete tree per publication; Rust validation and list batch reuse apply
  unchanged. Wire-level partial packets are a possible later phase, gated on measurements showing
  the packet/native share dominates (see `docs/NEXT_STEPS.md` runtime guidance).
- No automatic dependency tracking. That belongs to a future reactive layer above this runtime.

## Design

### Keyed sections

```moonbit
ui.section(key, revision, fn(sub) -> Element)
```

Stable `key` follows the same `valid_key` rules as list datasources; caller-owned `revision`
is bumped when that subtree's inputs change — the same contract as `list`'s `content_revision`
(`moonbit/frame.mbt`). A dirty section (new key, bumped revision, or dirty ancestor) runs its
render closure; a clean section skips it entirely and reuses its cached segment.

### Segments are frames

A section renders into a scratch sub-frame via the existing `new_frame` path, so all current
validation, operation checks, and ownership rules apply unchanged. A segment is therefore its
own node/op/edge/data slices plus its event/range bindings. Assembly into the parent frame is
concatenation with base-offset fixups:

- nodes: `offset += data_base`, `children`/`parent` indices `+= node_base`;
- ops: `node += node_base`; kind-6 data ops also shift `a` (payload offset) by `data_base`;
  scalar/event ops (kinds 0/1/2/4/5/3) need no data remap, and event tokens need no remap
  because session token identities are monotonic and never recycled;
- `edges += child.edges`; the caller attaches the spliced root with the ordinary `Element::child`,
  which accounts for the linking edge;
- `events`/`ranges` maps merge into the parent frame and flow into the pending publication
  through the existing `render_root` path (`moonbit/callbacks.mbt`), so dirty and retained
  bindings commit together and `complete_root` keeps its replace semantics.

Budgets (1,048,576 rows per family, 64 MiB payload, depth 128, single connected tree) are
enforced at assembly exactly as in `Frame::finish`.

### Lifecycle

- Retained segments live per session. A key absent from the new composition contributes no
  bindings and its cache is pruned — the same pattern as `renderer_keys` pruning on
  `complete_root` (`moonbit/callbacks.mbt`).
- Removal therefore retires event tokens naturally: the accepted maps are replaced, never
  patched, so dropped sections cannot leak callbacks.
- Window close / run return frees all caches with the existing route-revocation ordering.
- `section` is forbidden inside item renderers (element-only snapshots, same rule as nested
  lists). Sections may contain lists; a clean section's `RangeBinding` is carried into the
  pending publication like event bindings.
- Section render closures obey root-render purity and the `rendering` reentry guard
  (`Window::live` in `moonbit/runtime.mbt`).

### Invalidation

- `revision` bump + `ctx.invalidate()`: root assembly re-runs, but only dirty sections execute
  user closures. Unchanged native behavior otherwise.
- `SectionHandle::invalidate()` (planned): imperative dirty-flag + notify, mirroring
  `View.Invalidate()` for the section granularity.

## Verification

- Round-trip: the same tree built one-shot and via spliced segments must produce byte-identical
  packets, including nested splices and carried event bindings.
- Lifetime: prune-on-remove, token retirement, and budget enforcement covered by whitebox tests
  alongside `moonbit/runtime_wbtest.mbt`.
- The counter sample splits its main window into `header`/`controls`/`row-list` sections with a
  root-level dynamic status line and input; static sections render once while handlers stay live
  through retained bindings.
- All existing gates keep passing with zero warnings: `static-test`, `check`, `test`, `build`,
  `verify`, `generator-test`, `abi-test`, `c-test` via `python tools/moonbit.py`.
