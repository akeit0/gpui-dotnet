# Runtime plan

The runtime uses single-pass grow-before-write managed arenas and native acceptance (ABI 5).
Application-thread ingress, terminal session faults, post-acceptance mounting, and resource
presence generations are described in [RUNTIME_DESIGN.md](RUNTIME_DESIGN.md). The open work below
builds on those boundaries. Do not skip token/artifact ownership before Signals or structured async.

## 0. Regression cases first

Reproduce each hazard below as a failing test before changing production code:

- a released dynamic event index aliases a new callback under the same live View
  (`View.Events.cs` recycles `FreeEventIds` indices into tokens that encode only View
  handle plus index);
- rendering list range B retires events still held by cached batch A;
- a late async completion applies to a retired View.

Exit gate: one failing test per hazard, each naming the production route it exercises
rather than a test-only builder.

## 1. Event tokens and demand-artifact leases

Give dynamic event tokens a session-scoped slot-plus-generation registry (or non-reused
external IDs) validated at dispatch, so retired indices never alias live callbacks.
Separately, tie event and dependency leases to each demand artifact: cache eviction,
invalidation, source removal, and session shutdown release exactly that artifact's
bindings. Rendering another range must not release a still-cached artifact. Reverse
range callbacks must identify their bound source, not just the renderer method, so two
lists sharing one `[GpuiListItem]` method stay independent.

Exit gate: stale tokens never resolve, cached artifact A stays interactive after
rendering B, and evicting A releases only A's bindings.

## 2. Dirty state and reactivity

Once demand artifacts have explicit ownership (phase 1), replace
retained-tree locks and version-chain propagation with UI-thread dirty flags cleared
only by accepted work. Bind Signals permanently on first tracked read with
application/thread assertions; attach subscriber edges at acceptance and detach them
at teardown. Demand artifacts are their own reactive consumers: invalidating a Signal
read only by a row evicts that artifact without forcing the owner View to rerender.

Exit gate: no UI hot-path locks, conditional dependencies resolve correctly, and
teardown cannot leak subscribers through long-lived Signals.

## 3. Structured async, diagnostics, and migration

Make events synchronous and move async production to an explicit View-owned API that
captures an immutable request snapshot plus lifetime, posts completion through ingress,
and discards late results after retirement. Add capture diagnostics, keep the event
surface synchronous, and migrate samples and generators.

Exit gate: late success and failure are discarded without leaks, and View-owned work can start
from `OnMounted` without retaining retired owners.

## 4. Performance and platform verification

Measure warm allocations, subscriber cost, repeated invalidation, cold arena growth,
row-cache churn, and retained bytes; run the native suite and exercise Windows/macOS
behavior (theme switching, resize, scroll, focus, title-bar modes, menu activation).
Do not report visual verification when screen access was unavailable.

Exit gate: numbers recorded for the above, and both platform title-bar/menu paths
still working.
