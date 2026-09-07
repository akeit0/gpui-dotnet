# View lifecycle

Construction establishes local state and owned facilities. Rendering computes a declaration.
Native acceptance activates its external relationships. Retirement permanently ends ownership.

## Construction and declarations

A window owns its root and a committed child slot owns its child. CLR references do not retain UI
ownership. Views are not publicly disposable and cannot remount after removal.

Both roots and children use generated typed declarations:

```csharp
application.OpenWindow(DocumentView.Spec(props), options);
ui.Child("document", DocumentView.Spec(props));
```

Creating a `ViewSpec<TView>` or `ViewSpec<TView, TProps>` does not allocate a View or execute its
constructor. Window opening may be requested from another thread. The framework constructs the
root on the application thread during its first render callback. Closing a pending window releases
its declaration without construction. Child creation uses the same factory and ownership scope.

Apply `[GpuiView]` to a partial class. An explicit constructor takes `ViewConstruction`, followed
by initial props for a props-bearing View. When the class has no explicit constructor, the generator
supplies that constructor. Generated factories use direct calls and support NativeAOT.

```csharp
[GpuiView]
internal sealed partial class DocumentView : View<DocumentProps>
{
    private readonly Signal<string> _draft;
    private readonly Memo<AnalysisInput, Analysis> _analysis;

    public DocumentView(ViewConstruction construction, DocumentProps initialProps)
        : base(construction)
    {
        _draft = new(initialProps.InitialText);
        _analysis = construction.Memo<AnalysisInput, Analysis>();
    }

    protected override Element Render(in DocumentProps props, ref RenderContext ui)
    {
        var analysis = _analysis.Get(
            new(_draft.Value, props.Options),
            static input => Analyze(input));
        return ui.Text(analysis.Summary);
    }
}
```

Here the application supplies equatable input records and its pure `Analyze` calculation.
Constructor inputs seed one instance. Later declarations change render inputs without resetting
its draft. Use a different child key when a new document should create a new editing session.
State that should survive View removal belongs to a document, workspace, or application model.

`ViewConstruction` is a short-lived readonly ref struct. It supplies:

- `Own(resource)` for local synchronous disposable storage;
- `Memo<TInput, TResult>()` and `Effect<TInput>(setup)`;
- `Work`, `Dispatcher`, and controller handles;
- the owning `Window` and `Application`.

The scope exists before field initializers and user construction. If construction throws, registered
resources are released even if the factory never returns a View. Construction can seed fresh Signals
and create handles, but cannot mutate existing Signals, start work, subscribe to external services,
or issue runtime commands. Signal sampling during construction is untracked and does not subscribe
the parent's render. The context cannot be retained or reused for another instance.

## Props and slots

`View<TProps>` renders through `Render(in TProps props, ref RenderContext ui)`.
`TProps : IEquatable<TProps>` remains mandatory. Prefer immutable records or record structs.
Equality does not turn a mutable object into an immutable snapshot.

Every child declaration supplies current props. The parent, key, and concrete type retain local
View identity; props are not identity. `ui.Child(MyView.Spec())` uses the next positional slot.
Use `ui.Child(key, MyView.Spec(...))` for conditional, repeated, or reorderable content.
A positional slot cannot change its accepted View type; a keyed slot can.

The framework stages the latest props during rendering and commits accepted props throughout the
tree before activating effects. `CommittedProps` always means the accepted value, including when
read from an event. It throws before the first acceptance. Render methods use their explicit
argument. Bind a particular displayed value as event state when an event needs that snapshot.

Props-bearing `[GpuiListItem]` methods receive accepted props explicitly:
`Element Row(int index, in TProps props, ref RenderContext ui)`.
No-props methods use `Element Row(int index, ref RenderContext ui)`.

## Render work and caches

Rendering may allocate local data, mutate scratch storage, and populate correctly keyed pure caches.
It must not change observable application state, perform I/O, start tasks, issue commands, invalidate
a View, or rely on a one-time side effect. Signal writes remain forbidden, including equal writes.
Event binding, effect declarations, and ref-bound controller identity are supported declarations.
Managed arenas grow before writes without retrying user rendering.

Elements belong to one arena generation on its creating thread. Reusing the arena invalidates old
elements and contexts; disposing it or retiring its child View releases the buffers and invalidates
all remaining handles. Invalid use throws before reading freed memory. An Element contains a managed
owner reference, so it cannot be used with `stackalloc`. Span composition remains supported; use a
local `[InlineArray(N)]` buffer for a fixed number of elements, or reusable array storage for variable
counts. Clear reusable arrays after composition so they do not retain arena owners unnecessarily.

A `Memo<TInput, TResult>` stores one input/result entry. Its first `Get(input, calculate)` computes
and returns the result in that same render. Equal inputs reuse it. Read Signals while assembling
the input, before calling `Get`; the calculation and equality comparison cannot read Signals or
perform framework effects. This keeps reactive dependencies alive on cache hits.

Cache inputs must cover every dependency, including relevant data revisions and environmental
values. Results must be ordinary recomputable data, not editing state, subscriptions, native handles,
or arena-bound `Element` values. The framework does not deeply inspect the result object graph.
A cache can retain a correctly keyed calculation from unaccepted output: it does not represent
committed application state. A thrown calculation does not replace the previous entry.

Retirement clears framework-owned cached inputs/results. Hot Reload clears them before rerendering.
Ordinary application fields remain application-owned. A memo cannot eliminate the first computation's
cost; use precomputed data or explicit background work and a loading/cached state when necessary.

## Accepted effects

Create an effect handle once in construction, then declare its current input during rendering:

```csharp
private readonly Effect<Document> _watch;

public InspectorView(ViewConstruction construction, InspectorProps initialProps)
    : base(construction)
{
    _watch = construction.Effect<Document>(Watch);
}

private void Watch(EffectScope scope, Document document)
{
    scope.Own(document.Subscribe(scope.Bind(this, static view => view.Invalidate())));
}

protected override Element Render(in InspectorProps props, ref RenderContext ui)
{
    ui.Effect(_watch, props.Document);
    return ui.Text(props.Document.Title);
}
```

The application's `Document` supplies an equality contract and a disposable subscription.
The handle identifies the effect without positional hook ordering. Declare it at most once in an
owning View render. Effects are not permitted in virtual-row or standalone element contexts.

| Accepted declaration | Behavior |
| --- | --- |
| First declaration | Create an active scope and invoke setup |
| Equal input | Keep the current scope |
| Changed input | Revoke and dispose the old scope, then run new setup |
| Omitted declaration | Revoke and dispose the scope |
| View retired | Revoke and dispose all owned scopes |
| Rejected output | Start no proposed effects; terminal failure retires existing ownership |

Use `Effect<NoProps>` with `default` for constant input. A reused clean fragment retains its
accepted effects. Setup receives the accepted input value; cleanup should retain the old registration
or input it actually owns. Register cleanup as acquisition succeeds. A setup exception still disposes
all registrations already made.

`EffectScope.Own` releases registrations in reverse order. `Lifetime` is cancelled on replacement
or retirement. `Work` owns operations for that particular effect generation.
`Post(state, callback)` queues foreground delivery. `Bind(state, callback)` supplies a repeatable
parameterless callback suitable for subscriptions; invoking it queues delivery through that scope.
Both release queued callback state and reject stale delivery when the scope ends. Callbacks and
effect setup are synchronous; use owned work for asynchronous production.

## Acceptance and retirement order

Native validates and reconciles resource presence before acknowledging a root publication.
The managed acceptance sequence is:

1. Commit reachable composition, props, reactive reads, and effect declarations.
2. Retire removed subtrees child-first; stop replaced or omitted effects.
3. Activate every newly accepted View route.
4. Start new/replacement effects parent-before-child.

Reused clean subtrees keep their accepted dependencies and effects. Their boundary View still
accepts newly supplied equal props, including a distinct but equal object; descendants receive
new props only when that View renders and declares them again.

All newly accepted routes are active before any setup callback. Do not depend on sibling setup order.
Acceptance authorizes commands against accepted resources even before materialization. It does not
mean layout or painting completed. Effect state changes request a subsequent render; they do not
rewrite the already accepted output. External events and nested rendering cannot enter the barrier.

Unmount revokes the View route and effect/work delivery, resets pooled attachment storage, cancels
lifetime, disposes effect registrations and local resources, and releases props. Cleanup is terminal
and must not command the retiring View. It may run before the View was ever painted. Cleanup failures
do not prevent the remaining registrations or descendants from being released.

Unexpected render, callback, or setup failure faults that window session. Owned Views retire at the
application callback boundary; an off-thread failure is retired on application-thread ingress.
Artifact release and acceptance failures defer user cleanup to a normal ingress wake so cleanup
cannot reenter native resource reconciliation.
The original failure remains authoritative. Other windows remain independent.

## Hot Reload and virtual rows

Code updates preserve semantic state, View identity, and native resources. They clear memo entries
and replace effects on the next accepted render, even when inputs are equal. Setup method-body edits
therefore take effect; constructors and field initializers are not rerun. Pending effect work and
subscriptions may be replaced. Constructor/factory-shape changes require the normal runtime restart.

Virtual rows remain cached element snapshots, not Views. They have no constructors, effects, child
slots, or independent controllers. Native artifacts own their events and reactive dependencies.
Eviction, source removal, and retirement release those artifacts precisely. Cache row-derived data
by stable item identity/revision under an explicitly bounded owner.

See [asynchronous work](ASYNC_WORK.md), [reactivity](REACTIVITY.md), and
[threading](THREADING.md). The sample's Analysis page demonstrates a props-bearing View as both
a child and a root, with a local query, pure memo, document subscription, and latest-request work.
