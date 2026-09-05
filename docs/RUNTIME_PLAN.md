# Runtime plan

The runtime uses single-pass grow-before-write managed arenas and native acceptance (ABI 7).
Application-thread ingress, terminal session faults, post-acceptance mounting, resource
presence generations, accepted dirty state, non-reused event IDs, and cached-range leases are described in
[RUNTIME_DESIGN.md](RUNTIME_DESIGN.md). Signal ownership and accepted dependency tracking are described
in [REACTIVITY.md](REACTIVITY.md). View-owned production, completion ingress, and capture diagnostics
are described in [ASYNC_WORK.md](ASYNC_WORK.md). The open work below builds on those boundaries.

## Performance and platform verification

Measure warm allocations, subscriber cost, repeated invalidation, cold arena growth,
row-cache churn, and retained bytes; run the native suite and exercise Windows/macOS
behavior (theme switching, resize, scroll, focus, title-bar modes, menu activation).
Do not report visual verification when screen access was unavailable.

Exit gate: numbers recorded for the above, and both platform title-bar/menu paths
still working.
