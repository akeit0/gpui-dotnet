# Roadmap

This file lists open work only. Completed behavior belongs in the README or the focused design
documents.

## Default native host size

Prevent the default native host from retaining unrelated component families:

- separate or narrowly feature-gate the styled Dock skin so using `gpui-base::dock` does not link
  the complete `gpui-component` façade;
- keep editor providers, grammars, and other optional runtime families exclusive to their custom
  hosts;
- add a reproducible per-RID release-size report and a default-host dependency/link-map check;
- evaluate Thin LTO and one release codegen unit against build time and frame-sensitive runtime
  performance before enabling them in packaging.

The measured Windows x64 default host is 19,929,600 bytes, compared with 14,127,104 bytes at the
equivalent pre-Dock boundary. The focused analysis and acceptance criteria are in
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

Build on the retained center and side-region layout slice with:

- serialized layout import/export and an application reconciliation policy;
- tile layouts where application requirements justify them;
- coarse close and layout-change events plus programmatic controller operations;
- cross-platform interaction, accessibility, and persistence tests.

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
- run NativeAOT smoke tests for every supported RID;
- decide whether full managed render validation remains enabled in Release builds.
