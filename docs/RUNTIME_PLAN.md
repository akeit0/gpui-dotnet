# Runtime plan

The runtime uses single-pass grow-before-write managed arenas and native acceptance (ABI 7).
Application-thread ingress, terminal session faults, post-acceptance mounting, resource
presence generations, accepted dirty state, non-reused event IDs, and cached-range leases are described in
[RUNTIME_DESIGN.md](RUNTIME_DESIGN.md). Signal ownership and accepted dependency tracking are described
in [REACTIVITY.md](REACTIVITY.md). The open work below builds on those boundaries.

## 0. Regression cases first

Reproduce late async completion against a retired View through the production completion path.
Cover retirement while an uncooperative producer is still running and shared Signal writes from
live completion callbacks.

Exit gate: failing regressions identify the production path each phase must fix.

## 1. Structured async, diagnostics, and migration

Make events synchronous and move async production to an explicit View-owned API that
captures an immutable request snapshot plus lifetime, posts completion through ingress,
and discards late results after retirement. Add capture diagnostics, keep the event
surface synchronous, and migrate samples and generators.

Exit gate: late success and failure are discarded without leaks, and View-owned work can start
from `OnMounted` without retaining retired owners.

## 2. Performance and platform verification

Measure warm allocations, subscriber cost, repeated invalidation, cold arena growth,
row-cache churn, and retained bytes; run the native suite and exercise Windows/macOS
behavior (theme switching, resize, scroll, focus, title-bar modes, menu activation).
Do not report visual verification when screen access was unavailable.

Exit gate: numbers recorded for the above, and both platform title-bar/menu paths
still working.
