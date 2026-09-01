# View lifecycle

Each window owns an independent managed View tree. The initial root is supplied to
`GpuiApplication.OpenWindow`; framework-owned descendants are created lazily through generated
factories.

```text
child slot requested
        │
        ▼
generated CreateGpuiView()
        │ props applied for View<TProps>
        ▼
attach runtime identity
        │
        ▼
OnMounted(ref ViewContext)
        │
        ▼
Render(ref RenderContext)  ← dirty rerenders
        │
        ▼
commit child slots and event bindings
        │
        ▼
slot removed or replaced
        │
        ▼
cancel Lifetime, then OnUnmounted child-first
```

## Child slots

`ui.Child<TView>()` uses the next positional slot. `ui.Child<TView>(key)` uses a keyed slot. A slot
retains the same View instance while its requested concrete type remains the same.

A keyed slot can replace the child type, which is the normal route/tab pattern:

```csharp
var page = _route switch
{
    Route.Home => ui.Child<HomeView>("page"),
    Route.Settings => ui.Child<SettingsView>("page"),
    _ => throw new InvalidOperationException(),
};
```

The new tree renders and commits before the old subtree unmounts. The same View instance cannot be
owned by multiple parents or rendered in multiple slots.

Use `View<TProps>` when the parent supplies render inputs. Applying changed props marks the child
fragment dirty. Passing an existing View instance to `ui.Child` is supported when the parent
intentionally needs an imperative reference; framework ownership is the default.

## Constructor and mount

Constructors initialize ordinary managed state only. Runtime-dependent work belongs in
`OnMounted(ref ViewContext)`.

Explicit-key controllers can be created there:

```csharp
private ScrollController _scroll;

protected override void OnMounted(ref ViewContext context)
{
    _scroll = context.CreateScrollController("content");
}
```

Input, List, and Slider controllers can also be default-initialized fields and passed by `ref` in
`Render()`. The first render assigns a stable per-view key retained by that controller.

Creating a controller does not eagerly create a native resource. The resource appears when a
matching semantic declaration is committed.

## Render purity

The native host may retry a render after growing its arena. `Render()` must not:

- mutate application or View state;
- perform I/O;
- start tasks;
- call controllers;
- call `Invalidate()`;
- depend on one-time side effects.

Declaring event bindings and allowing a ref-bound framework controller to initialize its stable key
are supported render-time operations. Binding tables are staged and committed with the successful
render; a failed attempt preserves the previous bindings.

`[GpuiListItem]` renderers follow the same purity rule.

## Invalidation

`Invalidate()` marks the current View fragment dirty and propagates the required version to its
ancestors. Repeated native notifications are coalesced while one render is already pending.

Root rendering always uses the current `GpuiApplication.Theme`. Retained child fragments receive
the same theme. A theme change invalidates every fragment in each window because ambient theme
input is not represented by props.

## Events and async work

Event handlers run against mounted View targets and may change state, call controllers, and request
rerender. `Task` and `ValueTask` handlers are observed by the session.

Every mounted View has a `Lifetime` cancellation token. It is cancelled before `OnUnmounted`.
Pass it to asynchronous work that must not outlive route replacement or window closure:

```csharp
private async ValueTask LoadAsync()
{
    var data = await service.LoadAsync(Lifetime);
    _data = data;
    Invalidate();
}
```

Do not retain a controller or View reference after its owning View has unmounted.

## Virtual rows

A virtual List/Table row is a cached element snapshot produced by its owning View, not a mounted
View. It has no `OnMounted`, `OnUnmounted`, child slots, or independent controller lifetime.

Row cache eviction is a virtualization concern. Stable `.ItemId` values preserve native element
identity across datasource splices; they do not create managed row objects.
