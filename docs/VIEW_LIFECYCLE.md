# View lifecycle, child slots, props and generated events

## Lifecycle

A root View is supplied explicitly to `GpuiApplication.Run`. Framework-owned children are created lazily when `ui.Child<T>()` first resolves their slot.

```text
child slot requested
   │
   ▼
generated AOT-safe factory creates View
   │ props applied first for View<TProps>
   ▼
Attach to managed session
   ▼
OnMounted(ref ViewContext)
   ▼
Render(ref RenderContext)  (root render may be retried)
   ▼
committed retained child slot
   ▼
events / invalidation / rerenders
   ▼
slot removed or replaced and new tree commits
   ▼
OnUnmounted(ref ViewContext)
   │ descendant Views are unmounted before parents
   ▼
fragment/event handle released
```

A candidate can be mounted before the root tree commits because `OnMounted` may initialize state needed by the first `Render()`. Candidates are retained across native arena-growth retries, so that mount is not repeated. A hard render failure shuts down the session and unmounts all attached candidates.

On normal application shutdown, Rust invokes the managed `shutdown` callback while the GPUI application is still alive, then quits the native application. This keeps `OnUnmounted` valid for releasing future session-scoped native entities.

## Constructor

Constructors initialize ordinary durable C# state only.

Do **not** create normal child Views in parent constructors. Prefer:

```csharp
ui.Child<InspectorView>("inspector")
```

instead of:

```csharp
private readonly InspectorView _inspector = new();
```

The explicit field form remains supported when the parent intentionally needs imperative access to that exact child object:

```csharp
private readonly EditorView _editor = new();

protected override Element Render(ref RenderContext ui) =>
    ui.Child(_editor, "editor");
```

Do not initialize generated callbacks in constructors or field initializers. `Events.*` requires a mounted view handle by design.

## Child slot identity

### Positional

```csharp
ui.Child<HeaderView>()
ui.Child<BodyView>()
```

Unkeyed child calls are assigned positional View slots in the order of unkeyed child resolution for that parent render. Use them for structurally stable children.

### Keyed

```csharp
ui.Child<RowView>(row.Id)
ui.Child<SettingsView>("content")
```

Keys are scoped to one parent and support `string`, signed/unsigned integral values, and `Guid`. Duplicate use of one child slot in a single `Render()` is rejected.

Keyed slots should be used for:

- route/tab content;
- dynamic lists or reorderable children;
- conditional children when insertion/removal would otherwise shift positional identity;
- long-lived semantic locations such as `"sidebar"`, `"content"`, `"inspector"`.

### Dynamic type replacement

A key does not encode the View type.

```csharp
return _selected switch
{
    Section.Overview => ui.Child<OverviewView>("content"),
    Section.Activity => ui.Child<ActivityView>("content"),
    _ => throw new InvalidOperationException(),
};
```

If `"content"` changes from `OverviewView` to `ActivityView`, the runtime creates and renders `ActivityView`. After successful root commit, `OverviewView` and any now-unreachable descendants are unmounted child-first.

Switching back later creates a new `OverviewView` unless application code deliberately retained an explicit instance. Framework-owned Views live exactly as long as their retained tree membership.

## Props

Use `View<TProps>` for parent inputs:

```csharp
internal readonly record struct DocumentProps(long Id, string Title);

[GpuiView]
internal sealed partial class DocumentView : View<DocumentProps>
{
    protected override Element Render(ref RenderContext ui) =>
        ui.Text(Props.Title);
}
```

Parent:

```csharp
var props = new DocumentProps(document.Id, document.Title);
return ui.Child<DocumentView, DocumentProps>(document.Id, in props);
```

Rules:

- props are applied before first `OnMounted`;
- a retained child compares new props with `EqualityComparer<TProps>.Default`;
- changed props dirty the child's retained fragment;
- child-local fields remain state and are not reset by props updates;
- constructor model arguments are intentionally not part of framework child creation.

Prefer immutable record structs/records or other values with meaningful equality. If a props type uses reference equality for mutable objects, the framework cannot detect internal mutations; either supply a new/equality-distinct props value or explicitly invalidate the child through an appropriate state owner.

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

`Render()` must be deterministic for the same state and side-effect free because native arena growth may call the root more than once for one logical dirty render.

Do not:

- mutate fields;
- increment diagnostic counters;
- perform I/O;
- start tasks;
- call `Invalidate()`;
- register callbacks/resources.

Declarative `ui.Child<T>()` calls are framework operations and are permitted: candidate resolution is retained across retries so child construction/mount does not repeat merely because the native root arena grew.

## Generated View support

Every framework-owned View is marked `[GpuiView]` and declared `partial`:

```csharp
[GpuiView]
internal sealed partial class ToolbarView : View
{
    protected override Element Render(ref RenderContext ui) => ...;
}
```

The source generator emits an implementation of `IGeneratedViewFactory<TView>`. No reflection is used. Generated Views must be non-abstract top-level classes with a parameterless constructor. The constructor may be non-public because the generated implementation is another partial declaration of the same class.

A View that is only ever explicitly instantiated/root-owned does not need `[GpuiView]` unless it also uses `[GpuiEvent]`.

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

- the static framework child factory;
- a tiny `Events` facade when handlers exist;
- deterministic non-zero local handler IDs;
- a `DispatchClickAsync` switch that directly calls methods.

No delegate is allocated or registered at runtime.

Handlers may have zero parameters or one `ClickEvent` parameter. Supported returns are `void`, `Task`, and `ValueTask`. Use `ValueTask` for asynchronous handlers when completion is frequently synchronous.

`async void` is rejected because its completion and exception cannot be observed by the runtime.

## Async continuation behavior

Event dispatch runs with the GPUI managed synchronization context installed. Await continuations are posted into the session queue and wake the native view. Asynchronous exceptions are captured and surfaced through the next managed render boundary instead of becoming unobserved process exceptions.

Each mount exposes a protected `Lifetime` cancellation token. It is cancelled before `OnUnmounted`, which is important for dynamic routes: an async handler from a View that leaves the tree should not later resume and call `Invalidate()` on a detached instance.

```csharp
[GpuiEvent]
private async ValueTask Refresh()
{
    var data = await service.LoadAsync(Lifetime);
    _data = data;
    Invalidate();
}
```

Cancellation is treated as a normal event outcome by the event runtime. Explicitly retained Views receive a fresh token each time they are mounted again.

## Threading

`Invalidate()` and `Dispatcher.Post()` can be called from worker threads while mounted. Application state itself remains the application's synchronization responsibility; prefer mutating UI-owned state on the GPUI synchronization context.
