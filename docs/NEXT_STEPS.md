# Roadmap

This file lists open work only. Completed behavior belongs in the README or the focused design
documents.

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

Build on the separate `Gpui.Editor` schema and `gpui-dotnet-editor-host` runtime probe with:

- a controller for focus, selection, document replacement, edits, undo, and redo;
- revisioned UTF-8 edit/delta events without materializing the whole Rope per keypress;
- an initial-document bootstrap path that does not resend large text on unrelated dirty renders;
- runtime language/highlighter changes and explicit unsupported-language behavior;
- schema generation/verification instead of duplicated managed and Rust constants;
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
