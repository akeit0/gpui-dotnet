# View lifecycle and generated events

## Lifecycle

```text
new View()
   │ managed state only
   ▼
Attach to committed managed tree
   ▼
OnMounted(ref ViewContext)
   ▼
Render(ref RenderContext)  (may be retried)
   ▼
events / invalidation / rerenders
   ▼
view leaves committed tree or last native window closes
   ▼
OnUnmounted(ref ViewContext)
   │ child views are unmounted before parents
   ▼
native application quits
```

A view removed from the tree is detached immediately after the new managed tree is committed. Its retained fragment is disposed and its mounted event-token handle becomes invalid. Detached subtrees are unmounted child-first.

On normal application shutdown, Rust invokes the managed `shutdown` callback while the GPUI application is still alive, then quits the native application. This makes `OnUnmounted` a valid future location for releasing session-scoped native entities; the managed `Run` finally path remains an idempotent fallback for abnormal native termination.

## Constructor

Use constructors and field initializers for ordinary C# state and child `View` objects only.

Do not initialize generated callbacks there. `Events.*` requires a mounted view handle by design.

## OnMounted / OnUnmounted

These are control-plane lifecycle hooks. Put resources here whose validity depends on a running GPUI session.

```csharp
protected override void OnMounted(ref ViewContext context)
{
    // native entities/subscriptions in future control-plane APIs
}

protected override void OnUnmounted(ref ViewContext context)
{
    // symmetric application cleanup if needed
}
```

`ViewContext` is deliberately different from `RenderContext`.

## Render

`RenderContext` is a `ref struct`; elements are generation-bound value types.

`Render()` must be deterministic for the same state and side-effect free because native arena growth may call it more than once for one logical dirty render.

Do not:

- mutate fields;
- increment diagnostic counters;
- perform I/O;
- start tasks;
- call `Invalidate()`;
- register callbacks/resources.

## Generated events

```csharp
[GpuiView]
internal sealed partial class PreferencesView : View
{
    private bool _enabled;

    [GpuiEvent]
    private void Toggle()
    {
        _enabled = !_enabled;
        Invalidate();
    }

    protected override Element Render(ref RenderContext ui) =>
        ui.Checkbox("enabled", "Enabled")
            .Checked(_enabled)
            .OnClick(Events.Toggle);
}
```

The incremental generator emits:

- a tiny `Events` facade bound to the current view;
- deterministic non-zero local handler IDs;
- a `DispatchClickAsync` switch that directly calls methods.

No delegate is allocated or registered at runtime.

Handlers may have zero parameters or one `ClickEvent` parameter. Supported returns are `void`, `Task`, and `ValueTask`. Use `ValueTask` for asynchronous handlers when completion is frequently synchronous.

`async void` is rejected because its completion and exception cannot be observed by the runtime.

## Async continuation behavior

Event dispatch runs with the GPUI managed synchronization context installed. Await continuations are posted into the session queue and wake the native view. Asynchronous exceptions are captured and surfaced through the next managed render boundary instead of becoming unobserved process exceptions.

Cancellation is treated as a normal event outcome.

## Threading

`Invalidate()` and `Dispatcher.Post()` can be called from worker threads while mounted. Application state itself remains the application's synchronization responsibility; prefer mutating UI-owned state on the GPUI synchronization context.
