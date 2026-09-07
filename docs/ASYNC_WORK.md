# Owned asynchronous work

`ViewConstruction.Work` supplies a stable View-owned work handle during construction.
`EffectScope.Work` supplies work for one accepted effect generation. Obtaining a handle does not
start work; production requires an active owner and is forbidden during rendering or construction.

## Independent and latest-request operations

`Start(state, request, produce, complete, failed?, cancelled?)` starts an independent operation.
`StartLatest(...)` replaces the previous latest request on that same WorkScope. Independent starts
do not supersede latest requests. Use distinct effect scopes when independent relationships need
their own latest-request streams.

Both methods validate admission before invoking the producer. Requests are explicit immutable input
snapshots. Completion state is separate and may contain the View; producer inputs must not contain
Views, bound Signals, or controllers.

```csharp
private readonly WorkScope _work;

public SearchView(ViewConstruction construction, SearchProps initialProps) : base(construction)
{
    _work = construction.Work;
}

private void Search(string query) =>
    _work.StartLatest(
        this,
        query,
        static (input, lifetime) => SearchService.FindAsync(input, lifetime),
        static (view, result) => view._results.Value = result);
```

The application supplies its service and result Signal. Input-driven work can instead start in effect
setup using `scope.Work`; replacing that effect revokes its results even when the View stays mounted.

Replacing a latest request revokes its delivery before requesting cancellation. A producer that ignores
cancellation cannot overwrite a newer result. Pending operation identity supplies this check, including
when the old result was already queued. Each latest operation has a linked cancellation token.
Operations are not retried automatically and no debounce or queue-limit policy is implied.

## Scheduling and callbacks

Producers execute immediately on the calling application thread and must return promptly. The
framework does not call `Task.Run`, suppress execution-context flow, or replace synchronization
context. Application code selects CPU offloading and await policy explicitly:

```csharp
_work.StartLatest(this, snapshot,
    static (input, lifetime) => Task.Run(() => Analyze(input), lifetime),
    static (view, result) => view._analysis.Value = result);
```

A producer's synchronous prefix still runs on the caller. Honor cancellation inside long CPU
calculations when appropriate; merely cancelling scheduling cannot interrupt running code.
A slow first calculation needs a precomputed result or an explicit loading/cached-result UI.

`GPUI016` requires a static producer lambda or static method supplied directly at the call site.
The analyzer cannot prove deep immutability or inspect every reachable service. `GPUI017` rejects
directly supplied async completion handlers. `GPUI018` rejects visible async event, effect, menu,
and dispatcher callbacks or detached task results. Indirect delegates carry the same synchronous
contract; there is no runtime delegate reflection. Prefer explicit state with static callbacks to
avoid per-call capturing delegates.

## Observation and retirement

Every producer outcome is observed. Neither synchronous completion nor producer failure calls an
application completion handler inline. The observer publishes a typed operation through application
ingress; delivery rechecks ownership and removes the operation before invoking application code.

Cancelled tasks and directly thrown OperationCanceledException use the optional cancellation handler.
Without one, cancellation retires the operation quietly. A faulted Task remains a failure even when
its exception is OperationCanceledException. Failures reach the supplied synchronous failure handler,
or fault the live session if none was supplied. Expected load failures should become application error
state. A completion-handler exception faults its session.

View retirement, effect replacement, and terminal session cleanup revoke completion state and queued
callbacks before cancelling or disposing user registrations. Observers can outlive retirement, but
the framework releases their View targets, callbacks, scopes, and routes. Application-created tasks
still own their own request data, captured execution context, and synchronization context.

A WorkScope uses one operation object for registration, task observation, and ingress. Pending entries
form a dense list with constant-time removal. Completed tasks need no observer continuation; pending
tasks register one awaiter callback. Operation objects are not pooled while tasks may retain them.
The View's non-pooled runtime owns its optional work handle; pooled attachments contain no task state.

The Analysis sample exercises construction-time handle acquisition, explicit worker scheduling, and
latest-request delivery. Runtime tests cover synchronous and pending completion, cancellation,
replacement, effect lifetime, stale results, terminal faults, callback retention, and allocation costs.
