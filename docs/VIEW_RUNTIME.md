# Managed View runtime

The application View is an authoring object, not the runtime's shared implementation container.
Its base declares rendering and lifecycle hooks and exposes small authoring conveniences. Props
belong to `View<TProps>`. Runtime algorithms and storage belong to composed internal objects.

| Owner | Responsibility | Lifetime |
| --- | --- | --- |
| `ViewBase` | Application hooks and access to its runtime identity | One application View |
| `ViewRuntime` | Mount/retire transitions, cancellation, invalidation identity, lifecycle orchestration | One View; never pooled |
| `ViewCommandRoute` | Admission of commands from any thread; terminal deactivation | One attachment; never pooled |
| `MountedViewAttachment` | UI handle, resource-key sequence, and optional mounted capabilities | UI-thread storage; bounded reuse |
| `ViewEventRegistry` | Event tokens, typed binding, render passes, and artifact leases | Part of the mounted attachment |
| `WorkScope` | Pending tasks and live completion delivery | Optional capability acquired through `ViewContext.Work` |
| `ManagedSession` | Window ingress, retained composition, native acceptance, and terminal faults | One native window |

`ViewBase` has no event registry, scheduler, operation list, attachment pool, or implementation
classes. Runtime callers address the appropriate composed object directly rather than adding a
forwarding method for every feature to the base. Render and lifecycle invocation bridges remain
small because they are the boundary to protected application overrides.

The non-pooled runtime identity is deliberate: an externally held View may survive retirement,
and an any-thread command cannot safely use recycled identity. The attachment and event storage
can be reused because only the UI thread accesses them. Retirement deactivates command admission,
retires optional capabilities, resets and returns mounted storage, cancels lifetime, and invokes
application cleanup. Every callback and target reference must be released before storage reuse.

Async work does not change this division. The producer runs on the caller's UI thread; application
code owns offloading and await policy. A work operation is one object used for registration,
observation, and ingress. The scope clears its state and ownership references on retirement.
The View has no async start method and no knowledge of operation bookkeeping.

Composition introduces a fixed runtime object per View and reusable event-registry storage per
attachment. It must not introduce per-event forwarding closures or repeated callback reflection,
change accepted event identities, weaken cross-view Signal ownership, or reset virtual lists.
Validation uses the existing native-callback, lifecycle, event-lease, Signal, and allocation tests.

## Allocation and API design

`Dispatcher` is a readonly value handle over the stable View runtime. Obtaining or copying the
handle requires no heap object, and copying it does not create a new lifecycle identity. A default
handle rejects commands. `Post(state, static callback)` carries explicit state in the ingress record
and avoids a caller closure; `Post(Action)` remains available for already-created callbacks. Both
forms defer execution and recheck the original route, including when posted from another thread.
This is an explicit application dispatch API; it does not choose where producers run.

Reactive consumers may retain at most eight cleared, detached edge records for their own future
reads. Reuse begins only after acceptance or rejection has removed an edge from its Signal and the
consumer's active storage. A spare record has no Signal reference. This storage stays with its
one consumer, is never shared across threads or consumers, and is discarded at retirement. Accepted
edges must stay attached until the new snapshot commits, even when rendering has switched branches.
The bound covers small conditional branches without retaining an unbounded historical dependency
graph.

Active dependencies use a dense array with a live count. Small sets use reference-equality linear
lookup, avoiding dictionary objects, buckets, hashing, and dictionary-entry enumeration during
acceptance. Acceptance and rejection compact surviving edges in place and clear vacated array
slots. Edge objects retain their identity while linked to Signals; moving an array slot must not
change a subscription. Above 64 active/provisional edges, the same storage field switches to a
dictionary containing those edge objects, releasing the array. There is no separate index field or
duplicate collection. Dictionary storage remains until retirement, avoiding repeated conversion when
a large branch temporarily shrinks. Acceptance and rejection remove entries before recycling edges.
The cutoff follows complete read/accept measurements at 1, 4, 8, 16, 32, 64, and 256 dependencies,
including reversed read order; it is not a universal hardware crossover point.

Validation compares construction, explicit-state/capturing dispatch, stable dependencies, and
conditional-switch allocations. Regressions also cover default/copied dispatch handles, queued work
after retirement, rejected observations, and collection of Signals after dependency removal.
