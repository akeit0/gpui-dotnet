# Architecture

## Ownership boundary

GPUI.NET deliberately does not mirror GPUI's Rust API one-to-one.

### C# owns

- domain/application state;
- application/window definitions and stable managed window handles;
- managed `View` state and retained child-slot composition;
- generated event/list-renderer logic;
- dirty render IR construction.

### Rust owns

- `gpui::Application`, windows and frame lifecycle;
- render-arena backing memory;
- validation and retained decoded snapshots;
- semantic component materialization;
- `ScrollHandle` and `ListState`;
- retained `ManagedInput` entities and GPUI input handlers;
- deferred overlay/tooltip stacking, viewport placement, modal input interception, timing, and dismissal;
- virtual-list viewport/measurement state and row batch cache;
- high-frequency wheel/trackpad/scrollbar behavior.

The boundary rule is: **cross the ABI for state transitions or coarse data batches, never for each builder call, frame, style property or list row.**

## Application and window ownership

`GpuiApplication` is the managed owner of one native `gpui::Application`. Each `GpuiWindow` owns an independent managed render session rooted at one `View`; window IDs are globally stable for the process lifetime and are also the native callback session IDs. Opening another window never constructs a second GPUI application.

The native application host keeps a window registry and a UI-thread command channel. Open/close/activate/title/resize calls may originate on any managed thread, but GPUI window mutation occurs only when the native event loop consumes the command. Initial positioned bounds are supported. The pinned GPUI API exposes runtime resize but no cross-platform runtime reposition operation, so that capability is deliberately absent rather than emulated.

Each window has a separate `ManagedSession`, View-handle allocator, render snapshots, retained resources, list caches, and lifecycle teardown. A failure is recorded against the affected window session while other windows keep running; the application reports the first failure when the final window closes. Closing one window unmounts only its View tree. Closing the final window quits the shared GPUI application.

## Design reference and dependency policy

[GPUI Component](https://github.com/longbridge/gpui-component), especially its lower-level `gpui-base` architecture, is a project-wide design reference—not only a scrollbar reference. The relevant principles are:

- reusable behavior and the geometry required to keep it correct belong in the native foundation;
- product presentation, tokens, and final composition remain replaceable;
- retained controls expose narrow semantic handles rather than leaking concrete GPUI implementation types across the ABI;
- pointer, keyboard, focus, accessibility, overlay, virtualization, and reduced-motion behavior are part of a component contract;
- one retained handle represents one logical viewport or control instance.

These principles guide Scroll/List/Image/Input/Overlay/Tooltip today and future editor, select, menu, dialog, tree, table, and notification work. GPUI.NET does not directly depend on `gpui-component`/`gpui-base`: those crates must use the exact same GPUI revision as their consumer. This repository pins GPUI to the official Zed merge commit for [PR #41259](https://github.com/zed-industries/zed/pull/41259), allowing system IME keys and composition to follow normal Win32 translation without carrying a framework fork. Concepts are adapted behind GPUI.NET's semantic ABI instead. Attribution is recorded in the root [NOTICE](../NOTICE).

## Three planes

### Render plane

Managed `Render()` writes one arena containing nodes, operations, child edges and UTF-8. Rust validates and decodes it into flat ranges. A clean native repaint reuses that snapshot without calling .NET. Image nodes carry a filesystem path and semantic presentation options; GPUI owns decoding and its image cache.

### Retained resource/control plane

Some UI state must survive independently of a managed render snapshot. A resource is keyed by:

```text
(window session, managed View handle, local string key, resource kind)
```

The render snapshot declares that a resource is active. Imperative controllers send commands through the ABI; wheel/trackpad operations remain native. Each native window owns a separate resource store, so equal View handles and keys in different windows cannot collide. Overlay and Tooltip are keyed snapshot components rather than retained resources: native keyed state holds focus entry/restoration or hover timing while each component remains in consecutive snapshots.

Current retained resources:

- Scroll → `gpui::ScrollHandle`;
- List → `gpui::ListState` plus managed row-batch cache.
- Input → `Entity<ManagedInput>` plus `EntityInputHandler`, focus subscriptions, and native text state.

This same plane is intended to host future editor/select native state rather than forcing high-frequency text/focus state through C# renders. Input events are explicit state-transition subscriptions; clean editing with no event binding has no managed callback.

### Batched datasource plane

A virtualized List is not a normal parent component containing every item. GPUI owns the list state and invokes a Rust item closure for visible/overdraw indices. The Rust bridge maps a cache miss to an aligned managed range request:

```text
GPUI asks index 4200
      │
      ▼
Rust batch key = floor(4200 / 48) * 48
      │ cache miss
      ▼
managed list_render_range(4176, 48)  // one reverse FFI
      │
      ▼
48 row snapshots cached in Rust
```

Subsequent indices in that batch are materialized without another managed crossing. The cache retains up to four batches per List resource.

## Managed View tree

Child View identity is separate from type. The parent owns one child per retained slot. The same slot can change from `HomeView` to `SettingsView`; successful commit installs the new subtree, then unreachable old Views unmount child-first.

Dirty propagation follows the single parent pointer upward. Framework-owned View creation uses source-generated static factories; reflection is not used.

## List rows are not normal Views

A `[GpuiListItem]` renderer produces an Element subtree in a list-owned arena. It deliberately has no managed child renderer or native resource owner context. Therefore it cannot create:

- `ui.Child<T>()`;
- retained `ui.Scroll()`;
- nested retained `ui.List()`;
- retained `ui.Input()`;
- window-level `ui.Overlay()` and `ui.Tooltip()`.

This avoids coupling item cache eviction to managed View mount/unmount rules. Interactive row elements remain valid because event tokens target the owning View, and GPUI element IDs are namespaced by list identity + item index.

## Snapshot revisions and list cache correctness

`ListDataSource(count, contentRevision)` lets managed code declare row-content identity independently of the root snapshot. Rust therefore:

- preserves `ListState` and its viewport/measurement state where structurally valid;
- preserves cached row snapshots across managed renders while an explicit content revision is stable;
- drops cached row snapshots when that revision changes;
- conservatively drops batches on every managed snapshot when the legacy item-count overload omits a content revision;
- preserves row batches during clean native scroll/repaint.

`ListController.Splice/Refresh/Reset` queues measurement-preservation hints and invalidates the owning View. Rust applies the queued sequence only when the next managed snapshot revision is committed. If the hinted item-count transition does not match the declarative snapshot, managed state wins and the native list falls back to a full measurement reset.
