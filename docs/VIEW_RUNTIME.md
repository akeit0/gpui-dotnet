# Managed View runtime

The authoring object owns application state and defines rendering. Runtime identity, candidate
ownership, native attachment storage, and optional facilities have separate responsibilities.

| Owner | Responsibility | Lifetime |
| --- | --- | --- |
| ViewSpec | Typed factory and current input declaration | Value; root declaration retained until construction |
| ViewOwnership | Construction registrations, memos, effect handles, owning window | Begins before user construction; terminal retirement |
| ViewBase / View<TProps> | Application fields, rendering, explicit/committed inputs | One application View |
| ViewRuntime | Stable identity, lifetime, command admission, optional View WorkScope | One View, never pooled |
| ViewCommandRoute | Any-thread ingress and terminal deactivation | One attachment identity, never pooled |
| MountedViewAttachment | Event registry, UI handle, resource-key sequence | Application-thread storage with bounded reuse |
| EffectScope | One accepted relationship's cleanup, callbacks, cancellation, and work | One effect generation |
| ManagedSession | Composition, publication, acceptance, faults, and subtree teardown | One native window |

## Construction and publication

Generated Spec methods produce typed values; a declaration does not execute user construction.
Roots and children call the same direct generic factory under a construction owner.
The owner exists outside the constructor, so registered local resources are released when
construction throws. Initial props seed local state; later props are passed to Render explicitly.
Effect handles and work/controller handles can be initialized into readonly fields before rendering.

Root creation is deferred until the application-thread render callback. There is no preconstructed
root API. A pending window can close without creating application objects. Child fragments stage
declarations and reactive reads; only native acceptance makes them reusable.

Acceptance commits the whole reachable tree, retires removed ownership and replaced effects,
activates every new route, then executes effect setup parent-first. Clean reused fragments keep
their accepted effects. Effects are declared by owner-bound handles, not positional hook indices.

## Revocation and cleanup

An externally retained View may outlive its UI ownership. Its stable runtime and command route
must never observe recycled attachment state. Any-thread commands acquire only that route and
recheck admission on delivery; they do not inspect the application-thread attachment.

Retirement revokes routes and effect/work callbacks before cancellation and application cleanup.
Attachments reset every event target and key before entering their bounded pool. Owned caches
clear their inputs/results, effect scopes dispose registrations in reverse order, and props
release after cleanup. Local resources also release for candidates that never reach acceptance.
The session coordinates child-first retirement and preserves the first fault while completing cleanup.

Effect callbacks use their generation's ingress. Replacement clears queued callback targets before
old subscriptions are disposed. Work observation separates producer input from foreground completion
state, allowing retirement to release the latter even when production never finishes.

## Cost and invalidation

Memo handles retain a single pure result, with no speculative copy or native crossing. Read Signals
outside memo calculations to preserve dependencies on cache hits. Effect declarations compare
equatable inputs and retain unchanged scopes. Optional lists, work, cancellation, and callback
registries allocate when used. Clean native repaints do not enter managed rendering.

Signals continue to use accepted reactive consumers for View fragments and row artifacts. Small
dependency sets use dense arrays; sets above 64 edges switch to a dictionary. Each consumer may
retain eight cleared detached edges. No reactive scheduler is added for View-local memoization.

Hot Reload invalidates fragments, memo entries, and effect code generations through queued application
ingress. Constructors and semantic state are preserved. Native row caches are invalidated separately
by the existing code-update command. See [performance](PERFORMANCE.md) for measurements and
[View lifecycle](VIEW_LIFECYCLE.md) for the public contract.
