# Components and retained resources

## Component classes

| Class | Examples | Transport/state |
|---|---|---|
| Snapshot component | div, badge, button, image, overlay, tooltip, context menu | render IR |
| Retained container | Scroll | render IR reference + native state keyed by View/key |
| Virtualized datasource | List | retained native state + batched managed row renderer |
| Native entity component | Input; future editor/select | opaque native handle + commands/events |
| App/window service | title-bar presentation; future dialogs/clipboard/notifications | control commands + semantic hit-test regions |

Do not force all classes into one ABI shape.

## Window chrome

`GpuiWindowOptions.TitleBarStyle` selects `System`, `Custom`, or `Hidden` when a window opens. System chrome requires no managed elements. Custom chrome extends managed content into a transparent native title-bar surface; Hidden removes that surface entirely.

Managed Div and Button elements can use `WindowControlArea(Drag|Minimize|Maximize|Close)`. Rust maps the semantic operation to GPUI's native window-control hit testing, so window movement and caption actions do not become managed pointer/click traffic. A parent Drag region can contain child caption controls because the most specific child hit region wins. `GpuiWindow.Minimize()` and `ToggleMaximize()` expose the same operations to managed menu actions; the native host handles the Win32 restore path that the pinned GPUI maximize primitive omits. macOS custom windows retain native traffic lights, so managed title-bar layouts should reserve their space.

Interactive elements can use `HoverBackground(Color)` for an explicit native hover fill. This is useful for platform-like caption controls: neutral minimize/maximize feedback and a destructive red close hover remain entirely in GPUI without managed pointer callbacks.

## Snapshot component adapters

A normal semantic component is declared in `bindings/schema.json`, generated into C#/Rust metadata and mapped by a handwritten Rust adapter. C# depends on semantic operations, not GPUI trait names or concrete Rust types.

## Scroll

`ui.Scroll(key, axis, options, children)` is a normal render-tree parent plus a retained native `ScrollHandle`. Smooth wheel scrolling and the draggable overlay scrollbar default to enabled.

```csharp
ui.Scroll("main", ScrollAxis.Vertical, content)
    .Grow()
    .Height(Percent(100));
```

The key is local to the owning managed View. Use `ScrollController` only for imperative offset commands. Scroll input itself remains native.

## Virtualized list and table

Virtualized lists and tables can bind their retained row-engine key to a controller by reference:

```csharp
private ListController _list;

protected override Element Render(ref RenderContext ui) =>
    ui.List(ref _list, new ListDataSource(Count, revision), Rows.RenderRow);
```

The first render assigns a stable per-view key; later renders reuse the UTF-8 key retained by the controller.

## Image

`ui.Image(path)` records a UTF-8 filesystem path in the render arena. Rust materializes it with GPUI's native image element, so decoding and caching stay on the native side:

```csharp
ui.Image(Path.Combine(AppContext.BaseDirectory, "Assets", "photo.webp"))
    .Fit(ImageFit.Cover)
    .Grayscale()
    .Width(Px(240))
    .Height(Px(160));
```

Supported fits are `Fill`, `Contain` (the default), `Cover`, `ScaleDown`, and `None`. Image overflow is clipped to the styled bounds, and load/decode failures display a neutral fallback. Image is a snapshot component and is safe inside virtualized List rows; GPUI owns decoded-image caching independently of the managed row snapshot cache.

Only filesystem paths are public today. Relative paths resolve from the process working directory. Remote URI loading is deferred until GPUI.NET installs a native HTTP client and defines explicit network behavior rather than exposing an API that silently cannot load.

## Input

`ui.Input(key, options)` materializes a keyed `Entity<ManagedInput>`. For controller-owned identity, use `ui.Input(ref controller, options)`; the first render assigns that controller a stable per-view key and later renders reuse it. The entity owns text, focus, UTF-8/UTF-16 IME range conversion, selection, caret/selection geometry, horizontal reveal, clipboard actions, and password presentation. Declarative `initialValue` is used only when the resource is created; normal managed rerenders do not overwrite user edits.

String and UTF-8-first forms are both public:

```csharp
ui.Input("name", new InputOptions(placeholder: "Name"));
ui.Input("name"u8, new Utf8InputOptions(placeholder: "Name"u8));
```

`OnChanged`, `OnSubmitted`, and `OnFocusChanged` are opt-in view-bound event bindings. `InputEvent.Utf8Value` is the owned no-transcode payload; `Value` is a lazy string convenience. `InputController` supports focus, blur, selection, and programmatic value replacement. Input is retained and therefore intentionally rejected inside a virtualized List row, whose cache lifetime is not a managed View/resource lifetime.

## Slider

`ui.Slider(key, options)` materializes a retained native slider. `ui.Slider(ref controller, options)` assigns a stable per-View resource key on first render, matching the ref-bound controller pattern used by Input and List. `SliderOptions` supports bounds, step, horizontal/vertical orientation, disabled state, linear/logarithmic scale, and either `SliderValue.Single(value)` or `SliderValue.Range(start, end)`.

`OnChanged` emits `SliderEventKind.Changed` continuously during pointer movement. `OnReleased` emits `SliderEventKind.Released` once when the active pointer interaction ends, including release outside the slider. `SliderEvent.Value` preserves single versus range shape; `SliderController.SetValue` changes native state without synthesizing a managed interaction event. Slider is retained and is not available inside cached List rows.

## Overlay

`ui.Overlay(key, child, options)` gives one ordinary semantic child a deferred window-relative layer. Rust owns viewport placement, deferred paint priority, modal backdrop input interception, initial focus, Tab/Shift+Tab containment, previous-focus restoration, and native backdrop/Escape detection. Dismissal crosses into managed code only through an explicit `OnDismiss` callback.

The default options are a centered modal, 16-pixel viewport margin, priority 10, a translucent black backdrop, and backdrop/Escape dismissal. `OverlayPlacement` also covers every window edge and corner, providing the low-level geometry for dialogs and sheets without exposing GPUI element types through the ABI. A modal uses native non-tab-stop boundary handles to wrap GPUI focus traversal without enumerating managed children; opening selects the first available child tab stop and falls back to the boundary when the content has none.

`ui.Dialog(key, child, options)` and `ui.Sheet(key, child, side, options)` are semantic compositions over `Overlay`. Dialog forces centered modal placement. Sheet forces one of the four window-edge placements through `SheetSide`. They do not prescribe product visuals or content structure. Modal layers are registered in a window-owned stack rebuilt from each committed snapshot; priority wins, declaration order breaks ties, and only the topmost capturing layer handles dismissal. Toast lifecycle and platform accessibility remain separate work.

Overlay keys are scoped by the owning managed View. This prevents two reusable child Views with the same local overlay key from sharing GPUI focus state.

## Tooltip

`ui.Tooltip(key, trigger, content, options)` is a semantic two-child component. Rust measures the trigger during prepaint, owns delayed hover transitions, and paints the content at deferred priority 200. The default behavior is 500 ms before showing and a 300 ms grace period before hiding, which lets the pointer cross the gap into rich tooltip content. Pointer press dismisses immediately.

Placement is relative to trigger bounds rather than the cursor. Auto prefers Bottom, then Top, Right, and Left. Explicit side preferences flip when the primary axis does not fit, while cross-axis alignment and final viewport margins are clamped. Managed code owns all tooltip presentation.

Tooltip keys use the same `(managed View handle, local key)` identity rule as retained resources. Tooltip is disallowed inside cached List rows because those rows do not have a mounted managed View lifetime.

## Context menu

`ui.ContextMenu(key, trigger, content, options)` is a semantic two-child component. The trigger stays in normal layout. Rust intercepts right mouse-down, suppresses the platform default, records the pointer position, focuses a deferred layer at priority 300, and clamps content to an 8-pixel viewport margin. The defaults can be changed through `ContextMenuOptions`.

A transparent native backdrop blocks input behind the open menu and dismisses on outside left/right press. Escape, a right press in the menu, or left mouse-up after a menu selection also dismisses, then focus returns to the previously focused element. Content and action bindings remain managed, so the component imposes no product styling or fixed item model. ContextMenu is for application content; custom title-bar Drag regions should remain unwrapped so platform non-client behavior is not replaced by an application menu.

ContextMenu keys use the owning View namespace and ContextMenu is disallowed inside cached List rows for the same lifetime reason as Tooltip and Overlay.

## PopoverMenu

`ui.PopoverMenu(key, trigger, content, options)` is the left-click, trigger-attached counterpart to ContextMenu. Rust measures the first child during prepaint and positions the second child below its leading edge in a deferred layer. GPUI's anchored element snaps the menu to the configured viewport margin.

Pointer-down on the trigger opens the menu and transfers focus into it. While one menu is open, hovering another PopoverMenu in the same View transfers the active menu without another click and preserves the original focus-restoration target. Selection, Escape, or pointer-down outside the content closes it and restores prior focus. An outside pointer-down continues to its underlying target; pressing the same trigger is intercepted so it closes instead of immediately reopening. Menu visuals and item actions remain ordinary managed elements. `PopoverMenuOptions` configures priority and margin.

PopoverMenu keys are scoped by the owning View and the component is disallowed inside cached List rows. Native keyboard item navigation and disabled/check/radio item roles remain higher-level menu behavior.

## List

`ui.List` is intentionally **not** a parent receiving all item elements:

```csharp
ui.List("files", new ListDataSource(files.Count, filesRevision), Rows.File, options)
```

The render IR carries:

- resource owner View handle;
- local resource key;
- item count;
- generated renderer token;
- managed batch size;
- overdraw pixels;
- top/bottom alignment;
- estimated item height for stable virtual scrollbar geometry;
- optional explicit datasource content revision;
- smooth-scrolling and scrollbar visibility flags.

Rust owns `ListState` and calls managed `list_render_range` only for missing aligned batches.

An explicit `ListDataSource` revision keeps cached row snapshots across unrelated managed renders. Change the revision whenever renderer-visible state may have changed. The `itemCount` overload omits this operation and deliberately retains the previous conservative invalidation behavior.

### Row renderer restrictions

Rows are pure Element renderers, not retained managed View roots. `RenderContext` for a list range intentionally has no managed child resolver and no native resource owner. `ui.Child`, retained `ui.Scroll`, nested retained `ui.List`, retained `ui.Input`, window-level `ui.Overlay`, `ui.Tooltip`, `ui.ContextMenu`, and `ui.PopoverMenu` therefore fail rather than creating ambiguous lifetimes.

If item-scoped managed state is needed in the future, implement a dedicated virtualized-item state mechanism instead of silently reusing normal View slots.

### Events

Repeated row shapes often reuse a local element id such as `"row"`. Rust namespaces stateful GPUI IDs by:

```text
list owner View + list key + item index + local element id
```

Rows can bind payloads without per-item closures:

```csharp
ui.Button("row", label)
    .OnClick(this, (view, e) => view.Open(e), (ulong)index);
```

The handler receives `ClickEvent.Payload`.

### Structural changes

When model order/count changes, provide a `ListController.Splice` hint rather than throwing away all measurement state. Use `Refresh` when content/height changes without a structural change, and `Reset` when the whole datasource is replaced.

Structural hints are transactional with the next managed snapshot revision: they are queued on the Rust side, the controller invalidates the owning View, and the sequence is applied only when the new declarative `itemCount` arrives. If the sequence and snapshot disagree, the declarative snapshot is authoritative and native measurements are fully reset.
