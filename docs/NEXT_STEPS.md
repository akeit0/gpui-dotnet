# Next steps

The repository now has framework-owned dynamic managed Views, an ABI v5 multi-window application/session model, typed native-control events, retained native Scroll and Input state, virtualized ListState, deferred window Overlay and trigger-relative Tooltip layers, batched managed datasource rendering, lifecycle hooks, capability-based semantic bindings, native adapters and flat snapshots.

## 1. List datasource refinement — high priority

`ListDataSource(count, contentRevision)` now separates datasource identity from the managed root snapshot revision. Native row batches survive unrelated parent renders while the explicit content revision is stable; changing it invalidates row snapshots without discarding `ListState`. The existing item-count overload remains correctness-first and conservatively invalidates batches after every managed snapshot.

Continue the datasource foundation before Table/Tree:

- invalidate only cache batches intersecting `ListController.Refresh`/`Splice` ranges;
- add stable model-ID payload helpers that do not require per-row closures;
- add batched multi-range invalidation;
- expose cache/overscan/crossing telemetry for benchmarks and diagnostics;
- evaluate a uniform-height fast path behind the same `ui.List` API;
- add dedicated item-scoped retained state only if real applications prove it necessary.

Do not expose GPUI's uniform/variable implementation split directly unless it provides durable semantic value.

## 2. Multi-window application refinement — foundation complete

`GpuiApplication` now owns one native event loop and multiple independent `GpuiWindow` sessions. Windows have stable IDs, per-window View/resource namespaces, deterministic mount/unmount, isolated failures, thread-safe open/close/activate/title/resize commands, system/custom/hidden title bars with native caption hit testing, and final-window shutdown. `GpuiApplication.Run(view, options)` remains a one-window convenience.

Continue only where applications demonstrate need:

- managed application/window-opened/window-closed events;
- minimum/maximum size, initial maximized/fullscreen state, and bounds persistence;
- platform verification and accessibility semantics for custom title-bar controls;
- shared application services that do not leak GPUI types into C#;
- runtime repositioning if GPUI exposes a durable cross-platform operation;
- integration tests on every supported desktop platform.

## 3. Input hardening and Editor

The single-line Input establishes the native-entity ABI for focus, IME, selection, subscriptions, UTF-8 values, and imperative commands. Harden it before expanding the same foundation into a multiline Editor:

- accessibility role/value/selection exposure and platform verification;
- word navigation, undo/redo, double/triple-click selection, and richer platform keymaps;
- validation/error/help presentation without moving edit state into managed renders;
- composition/IME tests across Windows, macOS, and Linux;
- optional controlled-value semantics with explicit revision/conflict behavior.

## 4. Overlay compositions

The generic window-relative Overlay is the geometry and event foundation. Build semantic compositions on it rather than repeatedly recreating modal behavior:

- Dialog and Sheet semantics on the generic modal focus trap, including initial-focus selection and deterministic stacked-overlay dismissal policy;
- Popover/Menu with trigger measurement, anchor flipping/snapping, keyboard navigation, and outside-click dismissal;
- Tooltip accessibility descriptions and keyboard/focus-triggered visibility in addition to pointer hover;
- Toast/notification host with retained ordering, timeouts, pause-on-hover, and reduced-motion policy;
- accessibility roles, labels, focus announcements, and platform verification.

Keep stacking and dismissal window-owned; keep product styling and content composition in managed code.

## 5. Table/tree

Build Table/Tree on the same coarse datasource principles rather than as thousands of managed child Views. Column/tree metadata can be semantic IR; viewport state and measurement remain native.

## 6. Package and ABI CI

Add CI gates for:

- all supported native RID builds;
- Linux/macOS native RID builds in addition to the current Windows build;
- NuGet consumer tests without a Rust toolchain;
- ABI C header/layout assertions.
