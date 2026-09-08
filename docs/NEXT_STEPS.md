# Roadmap

This file lists open work only. Completed behavior belongs in the README or the focused design
documents.

## Implementation priorities

1. Presentation contract: close inheritance and state-propagation gaps between snapshot elements
   and retained controls. Cover pointer/keyboard transitions, theme changes, and declaration
   replacement without losing native state. Prioritize coherent behavior over additional part-color
   methods or sample variants.
2. Input and focus foundations: word navigation, undo/redo, richer pointer selection, platform IME
   tests, controlled-binding helpers, and composite focus entry/restoration where applications need it.
3. Performance: optimize a measured application bottleneck. Evaluate GPUI tessellation/scene-buffer
   APIs only when workloads justify the work; its current public API consumes these buffers.

Build on existing gpui-base component behavior and accessibility. Keep customization in GPUI.NET's
managed declarations, native adapters, and samples. Change gpui-base only for a concrete blocker;
further foundation migration or rewriting working behavior is not a priority.

Preserve the semantic batching and ownership boundaries. Native-retained fragment transport,
general item-scoped editors, universal state styling, and scoped themes require separate design
and measurements; they are not prerequisites for the focused improvements above.

Ambient Primary/Secondary content roles require a general native capability beyond the pinned
GPUI's single inherited foreground. The [upstream proposal](proposals/GPUI_CONTENT_COLORS.md)
records the evidence, alternatives, and acceptance criteria. Pursue upstream discussion if other
GPUI applications need it; do not maintain a GPUI fork or expose unsupported role semantics.

## Structural refactors

- Separate event-token and binding lifetime management from typed managed event dispatch. Reduce
  repetitive binders while preserving NativeAOT support and avoiding boxed event payloads.
- Move managed implementation sources into `src/Gpui.Core`, matching the project that compiles
  them, and organize them by subsystem. Keep `src/Gpui` focused on the application package facade.

Public API breaks are acceptable when they simplify these boundaries. Preserve the semantic
batching model and native interaction ownership; these refactors require no upstream GPUI edits.

## Native host packaging

- keep editor providers, grammars, and other optional runtime families exclusive to their custom
  hosts;
- evaluate Thin LTO and one release codegen unit against build time and frame-sensitive runtime
  performance before enabling them in packaging.

The focused analysis and acceptance criteria are in
[GPUI_BASE_MIGRATION.md](GPUI_BASE_MIGRATION.md#default-native-host-size-and-dependency-boundary).

## Accessibility

Button, Checkbox, and Radio already use foundation activation, focus, accessibility, and disabled
behavior. The retained Input declares the TextInput role; Slider supplies its role, numeric value,
bounds, step, and orientation. Preserve these integrations as presentation becomes customizable.

Remaining work is focused coverage, authoring, and platform verification:

- accessible label/help/error relationships between fields and supporting elements;
- List/Table viewport, row, header, cell, and selection semantics;
- Dock tabs, custom title-bar controls, and deferred-layer semantics;
- platform verification of focus, roles, values, and announcements, with backend limits documented.

Reuse existing foundation and GPUI capabilities. Add missing managed semantics in coarse
declarations that compose with scoped commands and focus targets; do not restart the foundation work.

## Input

Harden the single-line Input independently of the optional editor extension:

- word navigation and platform keymaps;
- undo/redo and richer pointer selection;
- accessible validation/help/error relationships for field compositions;
- a controlled-binding helper using revision-aware replacement that avoids destructive edit echoes;
- IME and clipboard integration tests on every desktop platform.

## Optional editor extension

Build on the separate `Gpui.Editor` schema and `gpui-dotnet-editor-host` runtime probe only where it
validates reusable extension behavior:

- optional undo/redo and multi-edit commands if an application needs them;
- managed reconciliation helpers for applying revisioned UTF-8 edits and handling stale commands;
- runtime language/highlighter changes and explicit unsupported-language behavior;
- RID runtime packages and clean-consumer tests that never require Cargo;
- IME, clipboard, undo, large-document, accessibility, and cross-platform behavior tests.

## Dock

Tile layouts stay undeclared: no application requirement justifies them, and the managed schema
describes only splits and tab groups. Tiles subtrees in imported documents are skipped.

What remains is behavior evidence rather than surface:

- cross-platform interaction, accessibility, and persistence tests;
- controller tab activation once the foundation offers a node-stable handle (activation today is
  declarative through `DockTabs` activeIndex).

Keep drag/drop targeting, tab activation, focus, and splitter motion native. Do not stream layout
deltas across the managed boundary while a pointer is moving.

## Menus and deferred layers

- keyboard navigation and roving selection for menu items;
- disabled, checked, radio, and submenu semantics;
- keyboard/focus-triggered tooltips;
- toast/notification hosting with ordering, timeout, pause, and reduced-motion behavior;
- broader focus and dismissal integration tests.

Keep stacking and dismissal window-owned in Rust while product visuals remain managed.

## Windows

- minimum/maximum size and initial maximized/fullscreen options;
- bounds persistence;
- managed application/window lifecycle events;
- runtime repositioning if GPUI exposes a cross-platform operation;
- platform verification for native and forced-managed title-bar modes.

## List and table

- improve demand-driven rendering separately from View Signal work, preserving targeted row-cache
  invalidation and measurement refresh for changed items, including items outside cached batches;
- optional public cache/overscan diagnostics when benchmarks justify an ABI query;
- column visibility/reordering;
- range/multi-selection, selection anchors, and modifier policies when application needs justify them;
- active-item preservation across arbitrary reorder if applications require a coarse identity map;
- frozen columns or resize chrome only when application requirements and measurements justify the
  added native state.

A Tree should remain a managed flattened List unless hierarchy is required by accessibility or
proven large-dataset behavior.

Cached rows remain element snapshots. Supporting an active editor later requires bounded native
interaction ownership separate from row-batch eviction; do not introduce mounted Views per row.

## Control presentation and commands

- indicator presentation driven by sample needs;
- richer state-rule composition only where the current base/hover/active contract cannot express
  a concrete application need;
- consistent Input focus indication at the styled-wrapper boundary without duplicating focus ownership;
- custom focus restoration and composite entry behavior where concrete components need it;
- accessible names for icon-only controls and semantic relationships for form fields.

Keep pointer motion, focus mechanics, and state-style evaluation native. Submit presentation as
retained descriptions rather than invoking managed callbacks during interaction.

## Runtime

The current runtime contracts are described in [Runtime design](RUNTIME_DESIGN.md).
The remaining measurement and platform work is listed here.

Measure large static trees with a changing leaf, dense drawings with stable geometry, variable-height
collection churn, IME with async completion, and multi-window navigation. Include native allocations,
bytes copied, retained capacity, and layout/paint or input latency beyond `ManagedView::render`.
Use the preparation, copying, and CPU drawing-frame probes in [PERFORMANCE.md](PERFORMANCE.md)
to select application workloads. Validate any drawing or Dynamic owner cache against snapshot
replacement, resource lifetime, theme changes, and resize. Extend current-thread Rust allocation
counts with retained-memory and platform measurements before claiming an end-to-end improvement.

## ABI, diagnostics, and CI
- generate and verify a public C header with `sizeof`/`offsetof` assertions per RID;
- enrich ambiguous render failures with structured node/operation context if a future ABI change
  justifies it; current diagnostics preserve operation-specific symbols and numeric statuses;
- define coalescing policies for high-frequency window commands;
- run NativeAOT smoke tests for every supported RID.
