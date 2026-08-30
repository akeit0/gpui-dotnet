# Components

## Component classes

GPUI.NET should treat components differently based on state ownership.

| Class | Examples | Transport |
|---|---|---|
| Snapshot component | div, badge, button chrome | render IR |
| Retained native component | input/editor/select | native entity handle + render reference |
| Data-source component | virtual list/table/tree | native entity + batched managed datasource calls |
| App/window service | dialogs, clipboard, notifications | control-plane commands |

The current repository implements the first class. The other classes are architectural next work, not simulated with per-keystroke render callbacks.

## Snapshot component schema

A component entry defines protocol semantics:

```json
{
  "id": 3,
  "name": "button",
  "csharp": "Button",
  "data": "utf8",
  "dataRequired": true,
  "children": true,
  "managedFactory": "idContainer",
  "capabilities": ["styled", "parent", "interactive"],
  "nativeAdapter": "button"
}
```

### Capabilities

Capabilities are the public semantic surface, not GPUI Rust traits.

- `styled`: width, height, color, padding, border, typography, etc.;
- `parent`: can own child render nodes;
- `layout`: can receive layout-mode operations such as HStack/VStack;
- `interactive`: can receive generated event operations;
- `checkable`: supports checked state.

C# tags implement generated marker interfaces. This changes style API generation from `components × operations` overloads into a small set of capability-constrained generic methods.

## NativeAdapter

`nativeAdapter` is generated into Rust `ComponentMetadata`. `materializer.rs` performs generic tree/style/event handling, while `components.rs` owns GPUI-specific defaults.

This is the intended adaptation boundary:

```text
C# semantic API
      │
      ▼
schema component + operations
      │
      ▼
generated NativeAdapter
      │
      ▼
components.rs handwritten GPUI code
      │
      ▼
GPUI API
```

If GPUI changes how a button is constructed, change the Rust adapter without changing C# unless the semantic behavior changes.

## Adding a snapshot component

1. Choose a stable semantic name and non-zero component ID.
2. Decide whether it is payload-only, a container, keyed container, or leaf.
3. Assign capabilities.
4. Add a `nativeAdapter` name.
5. Add semantic operations only for behavior that is meaningful and stable for C# callers.
6. Run the binding generator.
7. Add/extend the adapter match in `native/gpui-dotnet-native/src/components.rs`.
8. Add validator tests for component-specific wire values.
9. Add a sample and a dirty-render benchmark if the component is non-trivial.

Do not auto-reflect GPUI methods into C#. An explicit semantic schema is intentional insulation against GPUI's pre-1.0 API changes.

## Stateful component rule

Do not implement an editor or virtualized list by serializing all internal state into the snapshot or calling managed code every GPUI frame.

The intended future shape is:

```csharp
private NativeEntity<InputState> _input;

protected override void OnMounted(ref ViewContext context)
{
    _input = context.CreateInput(...);
}

protected override Element Render(ref RenderContext ui) =>
    ui.Input(_input).OnChanged(Events.NameChanged);
```

Rust would own `Entity<InputState>` behind an opaque generational handle. The render node would carry the handle and semantic presentation properties. Text editing, selection, IME, scrolling, and focus remain native/high-frequency.

## Data-source components

Virtual lists/tables should request visible ranges in batches. One reverse call should fill many row snapshots/payloads. Avoid one managed object or FFI call per visible cell when a contiguous batch can express the same work.
