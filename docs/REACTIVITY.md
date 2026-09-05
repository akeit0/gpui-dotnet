# Reactivity

## Fundamentals and assessment

A Signal is a value plus accepted rendering dependencies. It is not an event stream, a scheduler,
or an owner of Views. `Signal<T>(initialValue, comparer?)`, `Value`, and `Set(value)` provide
replacement semantics. `Set` compares once and reports whether the value changed. Mutating an
object inside a Signal does not notify; comparers must be stable and side-effect free. Computed
values, effects, deep observation, and public subscription callbacks are separate concerns and
are outside this primitive.

The existing runtime supplies application-thread entry, terminal session faults, retained dirty
flags, root acceptance, and explicit demand artifacts. These are sufficient for a small graph,
but two gaps require explicit treatment: observations precede acceptance, and native row caches
must be invalidated without requesting a managed root render.

## Ownership and access

Construction and untracked access leave a Signal unbound. The first tracked read permanently
binds it to the current application's execution identity. That identity has no back-reference to
the application; a Signal alone must not keep an application or window alive. Bound reads and
writes assert the owning thread and reject another application's active callback, even on the
same thread. Unbound values are ordinary initialization state, not concurrent containers.

Rendering may read but never write Signals, including equal-value writes and writes to unbound
Signals. Equality comparison must not reenter Signal mutation. A changed value invalidates its
accepted consumers synchronously and schedules later native work. It never renders or invokes
application callbacks. Writes from mount hooks are valid after the entire graph has committed.

## Accepted dependencies

Each retained View and each native demand artifact is a distinct consumer. A synchronous tracking
scope restores its parent when a child render ends; child reads therefore do not subscribe the
parent. Reads outside rendering assert affinity but do not create dependencies.

Consumers reuse a dictionary of edges, and each Signal links only accepted edges. A render stamps
the edges it reads. Acceptance attaches new edges, reuses unchanged edges, and detaches conditional
dependencies no longer read. Failure discards provisional edges; terminal teardown detaches every
edge before user cleanup and clears consumer references. A long-lived Signal must not retain a
retired View, its session, captured objects, or native artifact.

An observation also records the Signal's change revision. If a value changes between observation
and acceptance, acceptance installs the dependency and immediately invalidates that consumer.
This closes the subscription gap without subscribing speculative output. Signal revisions detect
this gap; they do not replace the retained tree's dirty flags. Revisions never wrap.

## Demand artifacts and native transport

ABI 7 adds required `accept_artifact(session, source, artifact)` after native range decoding and
row-count validation, after the output borrow ends. Root dependencies commit at the existing root
acknowledgement. Range dependencies commit only at this new acknowledgement. Decode or acceptance
failure releases the artifact; duplicate or mismatched acceptance is a protocol fault.

A row-only Signal change queues the artifact's non-reused `(source, artifact)` identity once.
At the outer managed callback boundary, each affected session sends a single
`invalidate_artifacts(session, keys, count)` batch. Native ingress copies the keys and the GPUI
thread evicts just those batches, clears their cached measurements, and requests a repaint without
marking the managed root dirty. Late keys cannot invalidate a replacement artifact or source.
The existing release callback then detaches the retired artifact's dependencies and events.

Ordinary Signal writes outside a framework callback use a short mutation scope on the bound
application thread so queued artifact work is still flushed. Shared Signals may affect several
windows in one application. Faulted or stopped sessions admit no new reactive work.

Notification failure does not roll back a changed value. Propagation must visit every accepted
subscriber, even if notifying one window fails, and must flush queued artifact invalidations before
reporting the first error with its original stack. Each failed native notification faults its own
session. If the error escapes an application callback, that callback's session also follows the
normal terminal-fault policy. Healthy subscribers must not depend on a later write to catch up;
an equal-value retry does not trigger another update.

## Cross-view sample

The Signal example targets shared View state. The Activity List uses an ordinary selected index
and explicit `Refresh`/`RefreshRanges` for the previous and next selection, with a stable content
revision. Reading one selection Signal in every row would subscribe every cached batch, broadening
invalidation beyond the two changed items. Demand-driven List improvements are separate work;
they must also preserve measurement invalidation for items whose row batches have been evicted.

The component gallery's **Reactivity** page passes one `Signal<int>` by reference through props
to sibling Views. The parent owns its lifetime but never reads its value during rendering. A
controls View writes it from synchronous events; two reader Views independently read it and show
the count and its double. No View calls `Invalidate()` or forwards value changes through props.

The second reader can pause, which removes its count dependency on the next accepted render.
Resuming reads the latest value. Removing that reader ends its View lifetime; showing it again
creates a fresh reader subscribed to the same surviving Signal. This separates shared value
ownership, conditional dependencies, and subscriber lifetime in an interactive example.

To assess it, increment and reset the count, pause the doubled reader while changing the count,
resume it, then remove it, change the count, and show it again. The first reader stays live
throughout. The equal-value button exercises the no-change path. Changing routes retires the
whole example and starts it fresh when returning. Automated runtime tests, rather than render-time
side effects in the sample, verify invalidation and subscription counts.

## Cost and verification

Steady-state tracked reads and unchanged edges allocate nothing, acquire no locks, and make no
native calls. Writes visit actual accepted subscribers. View invalidation uses existing dirty
propagation and notification coalescing; artifact invalidation batches at callback exit. Edge,
dictionary, and transport-buffer growth are cold costs. Native ingress retains its existing
thread-safe message routing; there is no second reactive scheduler.

Tests exercise production root/range callbacks: conditional and nested reads, sharing across
windows, wrong-thread and cross-application access, render-time writes, equality, the observation
gap, rejection, teardown retention, independent sources, and row-only invalidation. Native tests
cover acceptance after decoding, selective eviction, stale keys, and repaint without root dirtiness.
Allocation checks cover warm tracking and repeated writes with an already-pending notification.

Structured async remains a separate ownership change. Producers receive an explicit snapshot;
they cannot access bound Signals from workers. Their eventual live completion may replace Signal
values through application-thread ingress.
