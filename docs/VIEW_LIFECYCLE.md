# View lifecycle, child slots and native resource controllers

## Managed View lifecycle

Each `GpuiApplication.OpenWindow(rootView, options)` creates an independent root lifecycle. `GpuiApplication.Run()` mounts initial windows and runs until the last window closes; windows opened while it is running use the same native application. Framework-owned children are created lazily through source-generated factories inside each window tree.

```text
child slot requested
   │
   ▼
CreateGpuiView()
   │ props applied first for View<TProps>
   ▼
Attach
   ▼
OnMounted(ref ViewContext)
   ▼
Render(ref RenderContext)  (retryable)
   ▼
commit retained child slot
   ▼
events / invalidation / rerenders
   ▼
slot removed/replaced and new tree commits
   ▼
OnUnmounted child-first
```

A keyed slot can change View type. The slot is identity; the type is the current retained value. Existing same-type children keep their managed state.

## Constructor vs OnMounted

Constructors should initialize ordinary managed state only. Anything that relies on a mounted runtime belongs in `OnMounted`.

Explicit-key controllers are examples of mounted resources:

```csharp
private ScrollController _scroll;
private ListController _list;
private InputController _input;

protected override void OnMounted(ref ViewContext context)
{
    _scroll = context.CreateScrollController("content");
}
```

Creating an explicit-key controller does not immediately allocate the GPUI resource. Input and list controllers can instead remain default-initialized and be bound by reference in `Render()` with `ui.Input(ref _input, ...)` or `ui.List(ref _list, ...)`; the first render assigns a stable per-view UTF-8 key that the controller retains for later renders and commands. Native commands use the same `(View handle, key)` identity. Input's declarative initial value is creation-only, so rerenders cannot accidentally reset an in-progress native edit.

## Render purity

`Render()` and `[GpuiListItem]` are both retryable because the native-owned arena may need growth. They must not:

- mutate managed View/domain state (framework ref-bound controllers may initialize their retained key);
- start tasks or perform I/O;
- invoke controllers;
- register callbacks;
- call `Invalidate()`.

Events and lifecycle methods perform state transitions, then request rerender.

## Async lifetime

Each mounted View has a `Lifetime` cancellation token. Unmount cancels it. Async event handlers should pass that token to cancellable operations so route replacement does not leave work targeting a dead View.

## Virtual list row lifetime

A row is not mounted/unmounted as a managed View. It is a native cached snapshot produced by the owning View's generated renderer. Cache eviction is a virtualization concern, so normal managed child lifecycle APIs are intentionally unavailable in row render contexts.
