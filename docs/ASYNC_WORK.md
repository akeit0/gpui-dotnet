# View-owned asynchronous work

## Ownership boundary

`StartWork(request, produce, complete, failed?)` starts an independent operation from a mounted
View's application thread, including `OnMounted`. The request is an explicit immutable snapshot.
The producer receives only that snapshot and the View's cancellation token and runs on the thread
pool without the caller's execution or synchronization context. Rendering, unmounted Views, and
faulted sessions cannot start work.

The producer must be a static lambda or static method supplied directly at the call site.
`GPUI016` rejects captures, instance methods, and delegate variables whose targets cannot be
verified. Producers must not reach a View, a bound Signal, or a controller through requests or
static state. The diagnostic cannot prove deep immutability or the absence of UI references inside
application objects. `GPUI017` rejects directly supplied async completion/failure handlers;
all completion handlers must be synchronous, including delegates supplied indirectly.

The View owns success/failure callbacks in a lazy registry. Workers carry only weak references to
the View and its stable command route plus a non-reused operation ID. They never carry completion
callbacks. Removing a View clears the registry before cancellation and user cleanup, releasing
callback captures even if a producer ignores cancellation forever.

## Completion and failure

Every producer outcome is observed. Completion posts through the existing application ingress;
neither synchronous producer completion nor failure invokes application code inline. Delivery
rechecks the original route and removes the operation from the registry before calling application
code. Retirement discards both late results and late failures. Lifetime cancellation is normal;
other failures go to the optional failure callback or fault the live session through ingress.
Completion handlers may replace Signals and issue commands on the application thread.

Operations are independent and complete in arrival order. There is no implicit latest-request
policy, automatic retry, or automatic invalidation. Application code can supply a request revision
and ignore obsolete live results. Mutable state inside a request is not copied by the framework.

## Event integration and verification

Event bindings accept synchronous `Action` callbacks only. Dispatch invokes them directly; there
is no event task or session-bound continuation observer. Start production from a synchronous event
with `StartWork`, as in the counter sample.

`GPUI018` rejects async event lambdas, async-void method groups, and discarded task results inside
event lambdas. View-bound runtime registration also rejects async-void delegates, including delegates passed
indirectly or inside a multicast delegate, before any handler runs. Runtime checks cannot inspect
ordinary method bodies for manually detached work; application code must preserve this boundary.

Regressions exercise the production worker and completion path: foreground delivery, mount-time
start, worker affinity, producer failures, retirement before and after completion is queued,
uncooperative producers, captured callback retention, execution-context isolation, and render-time
rejection. A worker must not retain a retired View or its session while waiting for external I/O.
