# Runtime plan

The runtime uses single-pass grow-before-write managed arenas and native acceptance (ABI 6).
Application-thread ingress, terminal session faults, post-acceptance mounting, resource
presence generations, accepted dirty state, non-reused event IDs, and cached-range leases are described in
[RUNTIME_DESIGN.md](RUNTIME_DESIGN.md). The open work below builds on those boundaries.

## 0. Regression cases first

Reproduce late async completion against a retired View through the production completion path.
Introduce dependency tests with the Signal implementation: conditional reads, row-only reads,
cross-application access, and retirement while an uncooperative producer is still running.

Exit gate: failing regressions identify the production path each phase must fix.

## 1. Reactivity

Build on explicit demand-artifact ownership and dirty flags cleared only by accepted work.
Bind Signals permanently on first tracked read with
application/thread assertions; attach subscriber edges at acceptance and detach them
at teardown. Demand artifacts are their own reactive consumers: invalidating a Signal
read only by a row evicts that artifact without forcing the owner View to rerender.

Exit gate: dependency tracking adds no UI hot-path locks, conditional dependencies resolve correctly, and
teardown cannot leak subscribers through long-lived Signals.

## 2. Structured async, diagnostics, and migration

Make events synchronous and move async production to an explicit View-owned API that
captures an immutable request snapshot plus lifetime, posts completion through ingress,
and discards late results after retirement. Add capture diagnostics, keep the event
surface synchronous, and migrate samples and generators.

Exit gate: late success and failure are discarded without leaks, and View-owned work can start
from `OnMounted` without retaining retired owners.

## 3. Performance and platform verification

Measure warm allocations, subscriber cost, repeated invalidation, cold arena growth,
row-cache churn, and retained bytes; run the native suite and exercise Windows/macOS
behavior (theme switching, resize, scroll, focus, title-bar modes, menu activation).
Do not report visual verification when screen access was unavailable.

Exit gate: numbers recorded for the above, and both platform title-bar/menu paths
still working.
