# Deferred layers and window chrome

Window toasts are hosted by the native window rather than declared inside a View snapshot.
`GpuiWindow.ShowToast` uses a stable ID to replace a previous toast, and `DismissToast` or
`ClearToasts` begins dismissal. The native host keeps at most three active toasts visible, preserves
newer ordering, pauses timeouts while the stack is hovered, and handles entry/exit transitions.
Posting a toast does not steal focus. Their text and timeout come from C#; background, border, and text colors
resolve from the ambient theme.

Windows own placement, stacking, dismissal, and focus restoration; application Views own content.

## Deferred layers

Deferred layers paint relative to the window rather than the local layout tree:

- `Overlay`: generic modal or non-modal content with placement, priority, backdrop, and dismissal
- `Dialog`: centered modal Overlay composition
- `Sheet`: edge-aligned Overlay composition
- `Tooltip`: delayed trigger-relative content with side flipping and viewport clamping
- `ItemTooltip`: window-owned content requested by delayed hover on a marked List/Table item element
- `ContextMenu`: pointer-anchored right-click content
- `ItemContextMenu`: window-owned content responding to a List/Table item request
- `PopoverMenu`: trigger-attached left-click content with menu switching

Rust owns geometry, input interception, deterministic stacking, focus entry/restoration, and
dismissal. Managed code owns visuals and actions. Deferred layers can contain normal child views and
retained controls, but cannot appear inside virtualized items.

For virtual-item actions, declare `.ItemId(nonzeroId)` on each item root and bind
`.OnContextMenuRequested(this, static (view, request) => ...)` on the List or Table. Store the
`ListContextMenuEvent`, invalidate the View, and declare
`ui.ItemContextMenu("item-menu", request, content)` in that same View's ordinary render. The Table
gallery demonstrates this pattern. Use `request.ItemId` for actions; `Index` describes the
displayed position at the time of the request. Right-click does not change application selection.
Use `ListDataSource` with a stable ContentRevision when opening the menu. Count-only declarations
invalidate item batches on every managed render, which also expires the anchor.

The native window holds at most one item-menu request. It keeps the pointer position, a weak
collection reference, and the displayed item's artifact identity. Deferred prepaint checks that
the original item is still painted at the same bounds and clip. Scrolling, movement, clipping,
cache eviction, content/theme changes, projection replacement, and removal expire the request.
Escape, outside click, menu selection, a wheel gesture, or omitting the menu declaration also
dismiss it. The dismissal wheel gesture is consumed; subsequent gestures scroll normally.
Rendering an expired request never reopens it; another right-click supplies a fresh request.
Items without a stable ItemId do not request a menu. No item View, deferred item child, per-item
managed closure, or pointer-position callback is needed. Keyboard menu requests are not exposed.

For virtual-item hover details, mark the target element with `.ItemTooltipTarget()` and bind
`.OnTooltipRequested(this, static (view, request) => ...)` on the List or Table. The item root must
declare a nonzero `ItemId`. Store the `ListTooltipEvent`, invalidate the owning View, and declare
`ui.ItemTooltip("item-tooltip", request, content)` outside the item renderer. TaskBoard demonstrates
this on task cells, with explicit right-side placement to keep neighboring titles clear, a 700 ms
show delay, and a 150 ms hide delay. As with item menus, keep `ListDataSource.ContentRevision` stable while opening.
The marker adds no retained resource or deferred child to an item; outside a virtual item it has no effect.

`OnTooltipRequested` accepts the same `TooltipOptions` as ordinary tooltips: show and hide delays,
placement, alignment, gap, and viewport margin. Declare them once on the collection; `ItemTooltip`
supplies only content. For example:

```csharp
.OnTooltipRequested(this, static (view, request) => view.RequestTaskTooltip(request),
    new TooltipOptions(
        showDelay: TimeSpan.FromMilliseconds(700),
        hideDelay: TimeSpan.FromMilliseconds(400)))
```

Defaults are a 500 ms show delay, 300 ms hide delay, Auto placement, Center alignment, and 8 px gap
and margin. Leaving before the show delay makes no managed call. The hide delay lets the pointer
cross the gap into the tooltip, which stays open while its content is hovered; zero hides immediately.
Placement uses the marked element's actual bounds on both axes, independently of item order or
layout, with viewport flipping and clamping. A request captures the collection options; changing
them expires the pending or visible request, and the next hover uses the new values.
This does not add horizontal virtualization to the currently vertical List adapter.

The window holds one pending or visible item tooltip. Anchor movement, clipping, batch replacement,
item removal, or an omitted tooltip declaration expires it. Mouse press, wheel input, or a key
reaching the root dismisses it without consuming the input. A stale declaration cannot reopen it.
Tooltip content belongs to the callback's View; high-frequency hover and geometry remain native.

Tooltip and PopoverMenu share native trigger measurement and delegate viewport-aware positioning to
the `gpui-base` Positioner. ContextMenu uses the same Positioner for pointer-corner placement and
viewport clamping. PopoverMenu and ContextMenu delegate their open/focus/restoration lifecycle to
foundation PopoverState. Modal Overlay containers register with the foundation FocusTrapElement;
Dialog and Sheet inherit that behavior because they are managed Overlay compositions.

GPUI.NET retains tooltip timing, menu-group switching, overlay placement and backdrop rendering,
priority arbitration, topmost dismissal guards, and managed dismissal callback routing. The
foundation Sheet host couples Escape and backdrop closing and does not expose the independent
semantic options or ordering needed by the generic Overlay contract.
Overlay registrations use non-reused window-local sequence numbers. Deferred focus and dismissal
callbacks from an earlier frame cannot act on a newly registered layer with the same key.
An unfulfilled initial focus request remains pending until a current topmost registration can
deliver it.

## Window chrome

`WindowControlArea` marks Div or Button nodes as native Drag, Minimize, Maximize, or Close regions.
These are hit-test semantics, not managed click callbacks.

`GpuiTitleBar.RenderWindow` is a managed composition that consumes `GpuiMenu[]`. It preserves the
native macOS title/menu path by default and supplies minimal managed menus and window controls where
app-side chrome is appropriate. Applications may force the managed macOS path or build title bars
manually from the same primitives.
