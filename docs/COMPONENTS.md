# Components and retained resources

## Component classes

| Class | Examples | Transport/state |
|---|---|---|
| Snapshot component | div, badge, button, image | render IR |
| Retained container | Scroll | render IR reference + native state keyed by View/key |
| Virtualized datasource | List | retained native state + batched managed row renderer |
| Native entity component | future input/editor/select | opaque native handle + commands/events |
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

## List

`ui.List` is intentionally **not** a parent receiving all item elements:

```csharp
ui.List("files", files.Count, Rows.File, options)
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
- smooth-scrolling and scrollbar visibility flags.

Rust owns `ListState` and calls managed `list_render_range` only for missing aligned batches.

### Row renderer restrictions

Rows are pure Element renderers, not retained managed View roots. `RenderContext` for a list range intentionally has no managed child resolver and no native resource owner. `ui.Child`, retained `ui.Scroll`, and nested retained `ui.List` therefore fail rather than creating ambiguous lifetimes.

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
