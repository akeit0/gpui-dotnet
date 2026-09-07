# Roadmap

This file lists open work only. Completed behavior belongs in the README or the focused design
documents.

## Implementation priorities

1. Control customization: use concrete sample needs to scope combined interaction-state
   precedence, such as selected/hovered and invalid/focused. Keep application variants in
   managed style recipes and native interaction intact.
2. Virtual-row menus and tooltips: design window-owned overlays anchored to stable item identities,
   with explicit behavior on scrolling, eviction, movement, and removal.
3. Input editing: word navigation, undo/redo, richer pointer selection, platform IME tests, and
   controlled-binding helpers built on conditional replacement.
4. Performance: optimize a measured application bottleneck. Evaluate GPUI tessellation/scene-buffer
   APIs only when workloads justify the work; its current public API consumes these buffers.

Build on existing gpui-base component behavior and accessibility. Keep customization in GPUI.NET's
managed declarations, native adapters, and samples. Change gpui-base only for a concrete blocker;
further foundation migration or rewriting working behavior is not a priority.

Preserve the semantic batching and ownership boundaries. Native-retained fragment transport,
general item-scoped editors, universal state styling, and scoped themes require separate design
and measurements; they are not prerequisites for the focused improvements above.

## Default native host size

The Dock skin no longer links the complete `gpui-component` facade: Dock wears a small in-repo
renderer over `gpui-base`, and the default host resolves `gpui` plus `gpui-base` only. A
Windows x64 Release build (`cargo build -p gpui-dotnet-default-host --release`) is 14,988,800
bytes, against the 14,127,104-byte pre-Dock
reference and 19,929,600 bytes with the styled integration. This project is in preview, so
there is no per-RID size-report or link-map CI job: the boundary holds through the structural
`cargo tree --invert gpui-component` guard plus manual re-measurement on dependency changes.
What remains is hardening, not further isolation:

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

- explicit accessible names for icon-only controls and field relationships;
- List/Table viewport, row, header, cell, and selection semantics;
- Dock tabs, custom title-bar controls, and deferred-layer semantics;
- platform verification of focus, roles, values, and announcements, with backend limits documented.

Reuse existing foundation and GPUI capabilities. Add missing managed semantics in coarse
declarations alongside scoped commands and focus targets; do not restart the foundation work.

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

- window-owned row menus and tooltips with stable anchors and explicit dismissal on anchor loss;
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
- paint-state precedence for combined states such as selected/hovered and invalid/focused;
- scoped native key bindings with explicit consumption, separate from observer events;
- general focus targets, restoration, and composite entry behavior;
- accessible names for icon-only controls and semantic relationships for form fields.

Keep pointer motion, focus mechanics, and state-style evaluation native. Submit presentation as
retained descriptions rather than invoking managed callbacks during interaction.

## Runtime

Runtime performance and platform verification are tracked in
[RUNTIME_PLAN.md](RUNTIME_PLAN.md).

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
