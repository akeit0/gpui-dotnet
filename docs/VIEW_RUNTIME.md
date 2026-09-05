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
