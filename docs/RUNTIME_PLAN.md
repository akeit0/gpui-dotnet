# Runtime plan

Starting point: render output already uses single-pass grow-before-write managed arenas
(ABI 4). What remains is lifecycle, dispatch, reactivity, and async ownership. Each
phase lists its exit gate; do not skip ahead to Signals or async before the
acceptance and token work those phases depend on.

## 0. Regression cases first

Reproduce each hazard below as a failing test before changing production code:

- mounting runs inside `Render` (`ManagedSession.Rendering.cs` calls `AttachRoot` from
  the render path; `View.Runtime.cs` invokes `OnMounted` during attach);
- a released dynamic event index aliases a new callback under the same live View
  (`View.Events.cs` recycles `FreeEventIds` indices into tokens that encode only View
  handle plus index);
- rendering list range B retires events still held by cached batch A;
- a late async completion applies to a retired View.

Exit gate: one failing test per hazard, each naming the production route it exercises
rather than a test-only builder.

## 1. Application-thread ownership and fault boundary

Today `ManagedSession.Invalidate` mutates retained state directly (`MarkDirty` in
`ManagedSession.Lifecycle.cs` locks `_renderStateGate` and bumps versions up the
ancestor chain), and failure is recorded in three places (`ManagedApplication._failure`,
`ManagedSession._failure`/`_renderFailure`, pending async failure). Introduce one
application-owned execution object: owner-thread assertion, current phase, ingress
queue, and a single terminal session fault that stops normal dispatch while cleanup
continues. Route cross-thread invalidation through stable never-pooled handles with an
atomically set coalescing bit instead of mutating retained trees off-thread.

Exit gate: UI state is touched only on the application thread, faults stop normal
dispatch, and cleanup still runs to completion.

## 2. Acceptance boundary and retained lifetimes

Split attach into preparation (stable handle, declaration storage, no user code) and
post-acceptance mounting. A never-mounted Prepared View retires silently; mounting
runs parent-before-child only after props, composition, dependencies, and resource
presence commit together. Controller commands additionally require an accepted
resource-presence generation so deferred commands cannot cross removal and
reappearance.

Exit gate: `OnMounted` never runs during `Render`, and no external callback observes a
partial commit.

## 3. Event tokens and demand-artifact leases

Give dynamic event tokens a session-scoped slot-plus-generation registry (or non-reused
external IDs) validated at dispatch, so retired indices never alias live callbacks.
Separately, tie event and dependency leases to each demand artifact: cache eviction,
invalidation, source removal, and session shutdown release exactly that artifact's
bindings. Rendering another range must not release a still-cached artifact. Reverse
range callbacks must identify their bound source, not just the renderer method, so two
lists sharing one `[GpuiListItem]` method stay independent.

Exit gate: stale tokens never resolve, cached artifact A stays interactive after
rendering B, and evicting A releases only A's bindings.

## 4. Dirty state and reactivity

Once ingress is queued (phase 1) and acceptance is explicit (phase 2), replace
retained-tree locks and version-chain propagation with UI-thread dirty flags cleared
only by accepted work. Bind Signals permanently on first tracked read with
application/thread assertions; attach subscriber edges at acceptance and detach them
at teardown. Demand artifacts are their own reactive consumers: invalidating a Signal
read only by a row evicts that artifact without forcing the owner View to rerender.

Exit gate: no UI hot-path locks, conditional dependencies resolve correctly, and
teardown cannot leak subscribers through long-lived Signals.

## 5. Structured async, diagnostics, and migration

Make events synchronous and move async production to an explicit View-owned API that
captures an immutable request snapshot plus lifetime, posts completion through ingress,
and discards late results after retirement. Add capture diagnostics, keep the event
surface synchronous, and migrate samples and generators.

Exit gate: late success and failure are discarded without leaks, and accepted resource
use from `OnMounted` works.

## 6. Performance and platform verification

Measure warm allocations, subscriber cost, repeated invalidation, cold arena growth,
row-cache churn, and retained bytes; run the native suite and exercise Windows/macOS
behavior (theme switching, resize, scroll, focus, title-bar modes, menu activation).
Do not report visual verification when screen access was unavailable.

Exit gate: numbers recorded for the above, and both platform title-bar/menu paths
still working.
