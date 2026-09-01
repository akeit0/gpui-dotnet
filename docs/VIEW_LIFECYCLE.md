# View lifecycle

View lifetime follows UI ownership, not CLR reachability:

- an open `GpuiWindow` owns its root View;
- a parent View owns each child through one committed slot;
- an ordinary C# reference to a View or controller does not keep that View mounted.

The framework is the lifetime owner. Views deliberately do not implement `IDisposable`, and there
is no API for manually retaining a child outside a slot. Removing a child declaration or closing a
window permanently unmounts the affected View instances.

```text
Created
   │ first session attachment
   ▼
OnMounted(ref ViewContext)
   │
   ▼
Mounted  ◄──── dirty rerenders and successful slot/props commits
   │ slot removed/replaced, window closed, or mount failed
   ▼
cancel Lifetime
   │
   ▼
OnUnmounted()  (child-first, cleanup only)
   │
   ▼
Unmounted  (terminal)
```

A View instance has one stable `Lifetime` token. Its cancellation source is allocated lazily only
if the token is requested. Unmount cancels it, calls `OnUnmounted()` exactly once if mounting began,
releases framework-retained state, and makes the instance unusable. The same instance cannot be
mounted again or used as another window root.

If `Lifetime` is never read, the View allocates no `CancellationTokenSource`; terminal access uses
a shared cancelled token. Once requested, the token and its source are permanently identity-bound
to that View and are never pooled or reused.

Runtime/session state is separate from that permanent identity. Mount creates a non-pooled,
any-thread `ViewCommandRoute` and a GPUI-thread-only `MountedViewAttachment`. The route carries an
immutable owner handle and serializes commands against deactivation. The attachment contains the
render/event/resource-key state; unmount removes and completely resets it before returning it to a
bounded pool. A stale command can retain only its route and therefore cannot observe recycled
attachment state.

Unmount deactivates runtime access before cancelling `Lifetime` and invoking `OnUnmounted()`, so a
terminal View retained by application code does not retain its session or event tables. See
[Lifecycle and threading](THREADING.md) for the GPUI entity model and exact thread boundaries.

A queued root whose window closes before mounting goes directly from `Created` to `Unmounted`.
Because mounting never began, neither lifecycle callback runs, but its `Lifetime` is still
cancelled and the instance is still terminal.

## Child slots and ownership

`ui.Child<TView>()` uses the next positional slot. `ui.Child<TView>(key)` uses a keyed slot. A slot
retains the same View instance while its requested concrete type remains the same.

Positional slots are intended for unconditional children in a fixed declaration order. Use keys
for conditional children, repeated instances of the same type, routes, and declarations that can
reorder. A key belongs to its parent; props are not part of child identity.

A keyed slot can replace the child type, which is the normal route/tab pattern:

```csharp
var page = _route switch
{
    Route.Home => ui.Child<HomeView>("page"),
    Route.Settings => ui.Child<SettingsView>("page"),
    _ => throw new InvalidOperationException(),
};
```

After the replacement commits, the old subtree unmounts and cannot be reused. Durable navigation
or domain state should therefore live in a parent View or application service. State local to the
removed child is intentionally released with that child.

The same View instance cannot be supplied to multiple parents or rendered in multiple slots.
Child slots create their own framework-owned instances through generated factories; keeping a
reference to a child, its callback, or one of its controllers does not extend slot ownership.

Use `View<TProps>` when the parent supplies render inputs. The props overload is mandatory at
compile time:

```csharp
var card = ui.Child<CounterCardView, CounterCardProps>(
    "account",
    new("Account", revision)
);
```

Each declaration stages a new props value, like a constructor call for the retained slot. The same
key and concrete type retain the View instance and local state. Changed props rerender that child;
a successful whole-tree commit promotes staged props to committed props. A failed render discards
them, so event handlers continue to observe the last committed value.

The implementation retains two `TProps` payloads, not three: the committed value observed outside
rendering and the latest declaration used during rendering and for fragment comparison. The
fragment's required/rendered versions determine whether that latest declaration produced valid
cached output, including after a failed render or arena-growth retry.

`TProps` must implement `IEquatable<TProps>`, enforced by the `View<TProps>` generic constraint, so
`EqualityComparer<TProps>.Default` has a strongly typed comparison path. Records and record structs
supply it automatically; ordinary types must implement it explicitly.

`View` and `View<TProps>` are sibling API types over `ViewBase`. This makes the no-props and props
child overloads mutually exclusive through normal generic constraints; generated factories only
provide reflection-free activation.

## Mount and unmount

Constructors initialize ordinary managed state only. Runtime-dependent work belongs in
`OnMounted(ref ViewContext)`:

```csharp
private ScrollController _scroll;
private IDisposable? _subscription;

protected override void OnMounted(ref ViewContext context)
{
    _scroll = context.CreateScrollController("content");
    _subscription = service.Subscribe(OnServiceChanged);
}

protected override void OnUnmounted()
{
    _subscription?.Dispose();
    _subscription = null;
}
```

`OnUnmounted()` is for releasing application-owned subscriptions, timers, registrations, and other
resources acquired for the mounted View. `Lifetime` has already been cancelled, `IsMounted` is
false, and runtime commands are disabled. Do not call `Invalidate()`, post through `Dispatcher`, or
use a controller there. Committed props remain readable during the callback. A candidate that never
committed exposes its latest supplied props instead. Retained props are released after the callback
returns.

If `OnMounted` throws, the framework still performs terminal cleanup and calls `OnUnmounted()` once.
This lets one cleanup path handle partially initialized fields. Descendants unmount before their
parent.

Input, List, and Slider controllers can also be default-initialized fields and passed by `ref` in
`Render()`. The first render assigns a stable per-View key retained by that controller. Creating a
controller does not eagerly create a native resource; the resource appears when a matching
semantic declaration is committed.

## Transactional rendering

The native host may retry a render after growing its arena. `Render()` must not:

- mutate application or View state;
- perform I/O;
- start tasks;
- call controllers;
- call `Invalidate()`;
- depend on one-time side effects.

Declaring event bindings and allowing a ref-bound framework controller to initialize its stable key
are supported render-time operations. Event bindings are render-pass state rather than a separately
committed managed tree. Arena-growth retries reuse the declared bindings; if a render fails, the
native host presents its managed-render error surface and the failed snapshot does not become
interactive. `[GpuiListItem]` renderers follow the same purity rule.

A newly requested child is attached before its first render so its callbacks and controllers have
a stable owner identity. It is a session-owned candidate until the complete tree commits and can
survive an arena-growth retry. The previously committed tree remains active during that attempt.
After success, the candidate becomes the slot's committed child and the replaced subtree unmounts;
an abandoned candidate is unmounted during reconciliation or session shutdown.

Application code should not depend on when a candidate becomes committed. `OnMounted` may acquire
resources, but all visible state changes still belong in event or lifecycle work outside
`Render()` and must tolerate cleanup before the candidate is ever displayed.

## Invalidation and async work

`Invalidate()` marks the current View fragment dirty and propagates the required version to its
ancestors. Repeated native notifications are coalesced while one render is already pending.

Root rendering always uses the current `GpuiApplication.Theme`. Retained child fragments receive
the same theme. A theme change invalidates every fragment in each window because ambient theme
input is not represented by props.

Event handlers run against mounted View targets and may change state, call controllers, and request
rerender. `Task` and `ValueTask` handlers are observed by the session.

Pass `Lifetime` to asynchronous work that must not outlive route replacement or window closure:

```csharp
private async ValueTask LoadAsync()
{
    try
    {
        var data = await service.LoadAsync(Lifetime);
        if (!IsMounted)
        {
            return; // Protect against APIs that complete despite cancellation.
        }

        _data = data;
        Invalidate();
    }
    catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
    {
        // Normal View teardown.
    }
}
```

Avoid retaining View and controller references beyond their owner's lifetime. Such references keep
ordinary managed objects reachable, but they neither keep the UI mounted nor make runtime methods
valid after unmount.

## Virtual rows

A virtual List/Table row is a cached element snapshot produced by its owning View, not a mounted
View. It has no `OnMounted`, `OnUnmounted`, child slots, or independent controller lifetime.

Row cache eviction is a virtualization concern. Stable `.ItemId` values preserve native element
identity across datasource splices; they do not create managed row objects.
