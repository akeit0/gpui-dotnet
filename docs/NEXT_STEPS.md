# Next steps

The repository now has framework-owned dynamic managed Views, an ABI v5 multi-window application/session model, typed native-control events, retained native Scroll and Input state, virtualized ListState, deferred window Overlay, trigger-relative Tooltip, pointer-anchored ContextMenu, and trigger-attached PopoverMenu layers, batched managed datasource rendering with surgical batch invalidation, stable row identity, lifecycle hooks, capability-based semantic bindings, native adapters and flat snapshots, and a virtualized Table sharing the list row engine with declarative column IR.

## 1. List datasource refinement — foundation complete

`ListDataSource(count, contentRevision)` separates datasource identity from the managed root snapshot revision. Native row batches survive unrelated parent renders while the explicit content revision is stable; changing it invalidates row snapshots without discarding `ListState`. The item-count overload remains correctness-first and conservatively invalidates batches after every managed snapshot.

`ListController.Refresh`/`Splice` now invalidate only the cached batches their ranges intersect: splices keep batches entirely before the start index, refreshes keep non-intersecting batches, and validation failures still fall back to a full native reset. `ListController.RefreshRanges` queues several stable ranges as standard Refresh commands; native commits the queued sequence in one pass. Rows may declare a stable model identity with `.ItemId(id)` on the row root: stateful element identity inside the row survives splices, and `OnClick` events without an explicit payload deliver the ID as their payload without per-row closures (ID 0 is reserved). Native cache telemetry (loads, hits, evictions, invalidations, crossings, rendered rows) exists for native tests; exposing it to C# requires an ABI v6 query path, which is deferred until benchmarks justify it.

Continue only where applications demonstrate need:

- expose cache/overscan telemetry to C# as part of a future ABI v6;
- uniform-height fast path behind `ui.List` was evaluated and deferred: batch caching already amortizes materialization and uniform rows converge after first measurement, so the exact-scrollbar win does not yet justify a dual `ListState`/`UniformListState` implementation split;
- add dedicated item-scoped retained state only if real applications prove it necessary.

Do not expose GPUI's uniform/variable implementation split directly unless it provides durable semantic value.

## 2. Multi-window application refinement — foundation complete

`GpuiApplication` now owns one native event loop and multiple independent `GpuiWindow` sessions. Windows have stable IDs, per-window View/resource namespaces, deterministic mount/unmount, isolated failures, thread-safe open/close/activate/minimize/toggle-maximize/title/resize commands, system/custom/hidden title bars with native caption hit testing, and final-window shutdown. `GpuiApplication.Run(view, options)` remains a one-window convenience.

Continue only where applications demonstrate need:

- managed application/window-opened/window-closed events;
- minimum/maximum size, initial maximized/fullscreen state, and bounds persistence;
- platform verification and accessibility semantics for custom title-bar controls;
- shared application services that do not leak GPUI types into C#;
- runtime repositioning if GPUI exposes a durable cross-platform operation;
- integration tests on every supported desktop platform.

## 3. Input hardening and Editor

The single-line Input establishes the native-entity ABI for focus, IME, selection, subscriptions, UTF-8 values, and imperative commands. Harden it before expanding the same foundation into a multiline Editor:

- platform accessibility role/value/selection exposure and verification (the pinned GPUI revision has no accessibility-tree API; Input and Slider now participate in native focus traversal);
- word navigation, undo/redo, double/triple-click selection, and richer platform keymaps;
- validation/error/help presentation without moving edit state into managed renders;
- composition/IME tests across Windows, macOS, and Linux;
- optional controlled-value semantics with explicit revision/conflict behavior.

## 4. Overlay compositions

The generic window-relative Overlay is the modal geometry and event foundation. Pointer ContextMenu supplies native right-click interception and pointer anchoring. PopoverMenu supplies left-click trigger measurement and attachment. Both own viewport snapping, focus restoration, outside/Escape/selection dismissal, and managed content/actions. Dialog/Sheet compositions, first-focus selection, and deterministic window-owned stacking are now provided by the generic Overlay foundation. Remaining overlay work:

- keyboard navigation, roving selection, disabled/check/radio item semantics, accessibility, and hover switching for menu bars;
- Tooltip accessibility descriptions and keyboard/focus-triggered visibility in addition to pointer hover;
- Toast/notification host with retained ordering, timeouts, pause-on-hover, and reduced-motion policy;
- accessibility roles, labels, focus announcements, and platform verification.

Keep stacking and dismissal window-owned; keep product styling and content composition in managed code.

## 5. Table/tree — table foundation complete

The virtualized Table shares the list row engine end to end: `ui.Table` reuses the same batch renderer, cache, surgical invalidation, controller commands, and telemetry; a table registers its row engine as a list resource. Columns are declarative IR (`TableColumn[]`: key, header, px/fraction width, alignment) that drives the native header strip and reconciles every `ui.TableCell(column)` natively, so header and row geometry come from one source of truth. Row layout stays managed: cells are flex items and compose inside a horizontal container (for example `ui.HStack` stretched to the row width), since plain divs are block by default. A changed column table between snapshots invalidates all row batches. Rows keep the full list semantics — `ItemId` model identity, row events without an explicit payload, `RefreshRanges`, and `Splice` for reordering.

Continue where applications demonstrate need:

- header click/sort events and column visibility/reorder IR refinements;
- accessibility roles (table/row/columnheader/cell) as one semantic batch with list rows (blocked on the pinned GPUI accessibility-tree API);
- row activation and full roving selection semantics across rows;
- cell-level virtualization, frozen columns, and native resize chrome only if benchmarks or applications prove the need.

Tree deliberately has no native component: a visible tree is a flat list, so managed-owned flattening plus `Splice` for expand/collapse preserves measurements precisely. Reopen native hierarchy only for accessibility tree semantics or very large forests.

## 6. Package and ABI CI

Add CI gates for:

- all supported native RID builds;
- Linux native RID builds in addition to the current Windows/macOS build;
- NuGet consumer tests without a Rust toolchain;
- ABI C header/layout assertions.

The managed assemblies are now split by responsibility: `Gpui.Core` owns the platform-neutral
render API, `Gpui.Abi` owns generated ABI layouts and native build inputs, `Gpui.Runtime` owns native
application hosting, and the `Gpui` assembly remains a type-forwarding compatibility facade. Keep
these as one NuGet package while the public API is still evolving.

## 7. Review follow-ups (2026-08-31)

Addressed already: List/Table resource-key collisions are rejected at snapshot validation; empty required IDs are guarded by generated factories and managed validation; managed validator drift (scrollbar gutter/width) closed; table column key uniqueness and enum validation; controller pending-command contract documented to match actual lifecycle; `struct_size` prefix read and panic barriers on every exported FFI entrypoint; controller UTF-8 keys pre-encoded; CI/README solution-name typo fixed; table column metadata now travels as packed u64 `table_column` op records (width bits, unit, alignment) instead of a UTF-8 text blob — node data carries only the NUL-separated key/header strings, and both validators enforce the record layout; interactive virtualized-row state ids use structured `NamedInteger` element ids (fixed namespace + deterministic 64-bit hash of list/row/node identity), removing all per-frame string formatting and allocation from the row hot path; slider pointer/range/release/programmatic and keyboard/drag paths have GPUI integration coverage; operation payload constraints and event-payload legality are generated from `bindings/schema.json` into both validators; retained Slider/Input/List/Table controls participate in keyboard focus traversal.

Deferred, in priority order:

- platform accessibility roles, labels, values, and announcements once GPUI exposes a cross-platform accessibility tree;
- generated `include/gpui_dotnet.h` plus `sizeof`/`offsetof` assertions on every shipped RID in CI;
- symbolic native status diagnostics instead of raw integers;
- explicit coalescing policy for unbounded native command channels (title/resize last-wins);
- decide whether the full managed validator belongs in Release renders or Debug/tests only.
