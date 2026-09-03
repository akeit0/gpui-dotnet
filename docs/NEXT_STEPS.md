# Roadmap

This file lists open work only. Completed behavior belongs in the README or the focused design
documents.

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

The pinned GPUI revision does not expose a complete cross-platform accessibility-tree API. When a
durable API is available, add semantic roles, names, values, selection, and announcements for:

- Input and Slider;
- List/Table viewport, rows, headers, and cells;
- custom title-bar controls;
- dialogs, sheets, tooltips, and menus.

Treat accessibility as a semantic batch rather than platform-specific managed branches.

## Input

Harden the single-line Input before introducing a multiline editor:

- word navigation and platform keymaps;
- undo/redo and richer pointer selection;
- validation/help/error composition;
- controlled-value conflict semantics;
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

- optional public cache/overscan diagnostics when benchmarks justify an ABI query;
- table header sort events and column visibility/reordering;
- row activation and selection semantics;
- frozen columns or resize chrome only when application requirements and measurements justify the
  added native state.

A Tree should remain a managed flattened List unless hierarchy is required by accessibility or
proven large-dataset behavior.

## ABI, diagnostics, and CI

- generate and verify a public C header with `sizeof`/`offsetof` assertions per RID;
- add symbolic native status diagnostics;
- define coalescing policies for high-frequency window commands;
- run NativeAOT smoke tests for every supported RID.
