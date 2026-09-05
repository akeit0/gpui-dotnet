# View-owned asynchronous work

## Ownership boundary

Acquire the optional `WorkScope` from `ViewContext.Work` during `OnMounted`. Its
`Start(state, request, produce, complete, failed?, cancelled?)` method observes an independent operation.
The mounted attachment owns and retires the scope; `ViewBase` has no async machinery.
The request is an explicit immutable snapshot.
The producer receives that snapshot and the View's cancellation token and is invoked immediately
on the calling application thread. It must return promptly. The application owns offloading,
scheduler selection, and the continuation/context behavior of its asynchronous code. `WorkScope.Start`
does not call `Task.Run`, suppress execution-context flow, or replace the synchronization context.
Rendering, unmounted Views, and faulted sessions cannot start work.

The producer must be a static lambda or static method supplied directly at the call site.
`GPUI016` rejects captures, instance methods, and delegate variables whose targets cannot be
verified. Producers must not reach a View, a bound Signal, or a controller through requests or
static state. The diagnostic cannot prove deep immutability or the absence of UI references inside
application objects. `GPUI017` rejects directly supplied async success/failure/cancellation handlers;
all completion handlers must be synchronous, including delegates supplied indirectly. Runtime
validation checks every success, failure, and cancellation handler before invoking the producer,
including handlers inside multicast delegates. Validation caches method metadata without retaining
delegate targets; reused delegates do not repeat reflection or allocate validation records after
warmup. A fresh delegate can still allocate runtime method metadata when its method is inspected.

Completion state is always explicit; prefer static completion callbacks.
For example, `_work.Start(this, request, static (input, token) => Produce(input, token),
static (view, result) => view.Apply(result))` allows the compiler to cache both delegates.
Completion state can contain UI references; the producer's request cannot. All completion handlers
receive the same state. A lambda capturing `this` usually allocates a delegate on every call
(without a separate closure object). Static completion callbacks avoid that allocation.

The work scope owns a dense list of pending operations. Each operation holds its callbacks and state
while registered. Completion removes it in constant time by moving the last list entry into its
slot. Retirement clears each operation's scope, route, state, and callback references before
cancellation and user cleanup, even if a producer ignores cancellation forever. The observer may
outlive retirement but then has no UI ownership references. This boundary does not strip references
from application-created tasks: their request data, execution context, and captured synchronization
context remain the application's responsibility.

One operation object stores completion state, observes the Task, and enters ingress. Already
completed tasks need no continuation delegate. Pending tasks register one observer callback with
their awaiter; there is no framework async wrapper or wrapper Task. Observation only extracts the
result/failure and publishes the operation itself as a typed ingress entry. It never reads UI state
or runs application completion code on the task's completion thread. There is no separate callback
record, operation-ID dictionary, weak-reference graph, or closure adapter for posting. Operation
objects are not pooled while application tasks may still reference them.

The producer callback is retained in the API to validate ownership and render phase before work
starts and to supply the correct lifetime token. Accepting an already-started Task would move
those checks after application side effects. Producer execution and scheduling remain application
concerns; the operation owns only observation and delivery. The dense list and completion state
are confined to the UI thread. External completion accesses only its outcome and the stable route,
which is atomically detached on retirement and rechecked at ingress.

## Completion and failure

Every producer outcome is observed. Completion posts through the existing application ingress;
neither synchronous producer completion nor failure invokes application code inline. Delivery
rechecks the original route and removes the operation from the registry before calling application
code. Retirement discards all outcomes. A cancelled Task, or an OperationCanceledException thrown
directly by the producer, reports cancellation through the optional `cancelled(state)` callback.
Without that callback cancellation simply retires the operation; it does not fault the window.
A faulted Task remains a failure even if its exception is an OperationCanceledException. Failures
use the first Task exception, matching await, without throwing it during observation. They go to
the optional failure callback or fault the live session through ingress. Exceptions thrown
by any application completion handler still fault the session.
Completion handlers may replace Signals and issue commands on the application thread.

Operations are independent and complete in arrival order. There is no implicit latest-request
policy, automatic retry, or automatic invalidation. Application code can supply a request revision
and ignore obsolete live results. Mutable state inside a request is not copied by the framework.

The scope does not join Tasks or control application await continuations. A producer that captures
the window synchronization context can retain it until that producer completes. Retirement clears
the scope's references; it cannot rewrite application state machines. Keep producer inputs to data
and services, honor the lifetime token, and choose await/offloading policy in application code.

## Event integration and verification

Event bindings accept synchronous `Action` callbacks only. Dispatch invokes them directly; there
is no event task or session-bound continuation observer. Start production from a synchronous event
with `WorkScope.Start`, as in the counter sample.

`GPUI018` rejects async event lambdas, async-void method groups, and discarded task results inside
event lambdas. View-bound runtime registration also rejects async-void delegates, including delegates passed
indirectly or inside a multicast delegate, before any handler runs. Runtime checks cannot inspect
ordinary method bodies for manually detached work; application code must preserve this boundary.

Regressions exercise producer invocation on the calling thread, preserved caller context,
foreground delivery, mount-time start, producer failures, retirement before and after completion
is queued, uncooperative producers, captured callback retention, and render-time rejection.
The observer must not retain a retired View or its session while waiting for an application task.
