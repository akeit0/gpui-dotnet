# View lifecycle, child slots and native resource controllers

## Managed View lifecycle

A root View is supplied to `GpuiApplication.Run`. Framework-owned children are created lazily through source-generated factories.

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

Controllers are examples:

```csharp
private ScrollController _scroll;
private ListController _list;

protected override void OnMounted(ref ViewContext context)
{
    _scroll = context.CreateScrollController("content");
    _list = context.CreateListController("rows");
}
```

Creating a controller does not immediately allocate the GPUI resource. The resource is materialized from `ui.Scroll`/`ui.List`; native commands use the same `(View handle, key)` identity.

## Render purity

`Render()` and `[GpuiListItem]` are both retryable because the native-owned arena may need growth. They must not:

- mutate managed View/domain state;
- start tasks or perform I/O;
- invoke controllers;
- register callbacks;
- call `Invalidate()`.

Events and lifecycle methods perform state transitions, then request rerender.

## Async lifetime

Each mounted View has a `Lifetime` cancellation token. Unmount cancels it. Async event handlers should pass that token to cancellable operations so route replacement does not leave work targeting a dead View.

## Virtual list row lifetime

A row is not mounted/unmounted as a managed View. It is a native cached snapshot produced by the owning View's generated renderer. Cache eviction is a virtualization concern, so normal managed child lifecycle APIs are intentionally unavailable in row render contexts.
