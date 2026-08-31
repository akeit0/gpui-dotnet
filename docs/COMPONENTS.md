# Components and retained resources

## Component classes

| Class | Examples | Transport/state |
|---|---|---|
| Snapshot component | div, badge, button, image, overlay, tooltip | render IR |
| Retained container | Scroll | render IR reference + native state keyed by View/key |
| Virtualized datasource | List | retained native state + batched managed row renderer |
| Native entity component | Input; future editor/select | opaque native handle + commands/events |
| App/window service | future dialogs/clipboard/notifications | control commands |

Do not force all classes into one ABI shape.

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

`ui.Input(key, options)` materializes a keyed `Entity<ManagedInput>`. The entity owns text, focus, UTF-8/UTF-16 IME range conversion, selection, caret/selection geometry, horizontal reveal, clipboard actions, and password presentation. Declarative `initialValue` is used only when the resource is created; normal managed rerenders do not overwrite user edits.

String and UTF-8-first forms are both public:

```csharp
ui.Input("name", new InputOptions(placeholder: "Name"));
ui.Input("name"u8, new Utf8InputOptions(placeholder: "Name"u8));
```

`OnChanged`, `OnSubmitted`, and `OnFocusChanged` are opt-in generated event bindings. `InputEvent.Utf8Value` is the owned no-transcode payload; `Value` is a lazy string convenience. `InputController` supports focus, blur, selection, and programmatic value replacement. Input is retained and therefore intentionally rejected inside a virtualized List row, whose cache lifetime is not a managed View/resource lifetime.

## Overlay

`ui.Overlay(key, child, options)` gives one ordinary semantic child a deferred window-relative layer. Rust owns viewport placement, deferred paint priority, modal backdrop input interception, initial focus, Tab/Shift+Tab containment, previous-focus restoration, and native backdrop/Escape detection. Dismissal crosses into managed code only through an explicit `OnDismiss` callback.

The default options are a centered modal, 16-pixel viewport margin, priority 10, a translucent black backdrop, and backdrop/Escape dismissal. `OverlayPlacement` also covers every window edge and corner, providing the low-level geometry for dialogs and sheets without exposing GPUI element types through the ABI. A modal uses native non-tab-stop boundary handles to wrap GPUI focus traversal without enumerating managed children. Popover/menu interaction, toast lifecycle, and higher-level Dialog/Sheet/Popover components remain separate composition work.

Overlay keys are scoped by the owning managed View. This prevents two reusable child Views with the same local overlay key from sharing GPUI focus state.

## Tooltip

`ui.Tooltip(key, trigger, content, options)` is a semantic two-child component. Rust measures the trigger during prepaint, owns delayed hover transitions, and paints the content at deferred priority 200. The default behavior is 500 ms before showing and a 300 ms grace period before hiding, which lets the pointer cross the gap into rich tooltip content. Pointer press dismisses immediately.

Placement is relative to trigger bounds rather than the cursor. Auto prefers Bottom, then Top, Right, and Left. Explicit side preferences flip when the primary axis does not fit, while cross-axis alignment and final viewport margins are clamped. Managed code owns all tooltip presentation.

Tooltip keys use the same `(managed View handle, local key)` identity rule as retained resources. Tooltip is disallowed inside cached List rows because those rows do not have a mounted managed View lifetime.

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

Rows are pure Element renderers, not retained managed View roots. `RenderContext` for a list range intentionally has no managed child resolver and no native resource owner. `ui.Child`, retained `ui.Scroll`, nested retained `ui.List`, retained `ui.Input`, window-level `ui.Overlay`, and `ui.Tooltip` therefore fail rather than creating ambiguous lifetimes.

If item-scoped managed state is needed in the future, implement a dedicated virtualized-item state mechanism instead of silently reusing normal View slots.

### Events

Repeated row shapes often reuse a local element id such as `"row"`. Rust namespaces stateful GPUI IDs by:

```text
list owner View + list key + item index + local element id
```

Rows can bind payloads without per-item closures:

```csharp
ui.Button("row", label)
    .OnClick(Events.Open, (ulong)index);
```

The handler receives `ClickEvent.Payload`.

### Structural changes

When model order/count changes, provide a `ListController.Splice` hint rather than throwing away all measurement state. Use `Refresh` when content/height changes without a structural change, and `Reset` when the whole datasource is replaced.

Structural hints are transactional with the next managed snapshot revision: they are queued on the Rust side, the controller invalidates the owning View, and the sequence is applied only when the new declarative `itemCount` arrives. If the sequence and snapshot disagree, the declarative snapshot is authoritative and native measurements are fully reset.
