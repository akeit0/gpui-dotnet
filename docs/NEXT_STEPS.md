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

## Input and editor

Harden the single-line Input before introducing a multiline editor:

- word navigation and platform keymaps;
- undo/redo and richer pointer selection;
- validation/help/error composition;
- controlled-value conflict semantics;
- IME and clipboard integration tests on every desktop platform.

A multiline editor should reuse the native entity, focus, IME, selection, UTF-8 event, and command
contracts rather than moving edit state into managed renders.

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
