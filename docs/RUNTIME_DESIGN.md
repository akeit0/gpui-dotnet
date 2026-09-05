# Runtime design

This design refines [RUNTIME_PLAN.md](RUNTIME_PLAN.md). The current ABI 4 single-pass
arenas remain the rendering transport. The older runtime specification's capacity
retry protocol is superseded: user rendering must never be repeated to grow storage.

## Execution and fault ownership

One application-owned execution object binds to the actual native callback thread,
not the thread that constructs a window. Every session shares that identity and a
non-reentrant callback phase. Root rendering, range rendering, events, and cleanup
enter explicit scopes before accessing session state. Internal child rendering is
part of its outer scope, not a new external callback.

Any-thread invalidation publishes a stable, never-pooled View identity into session
ingress. An atomic pending bit coalesces repeated requests for that identity. Only
the application thread consumes requests and marks retained fragments dirty. Clear
the pending bit before applying an invalidation so a concurrent request can enqueue
the next one. Requests arriving during rendering belong to a later render. Ambient
theme and metadata invalidations use the same queued principle. Keep queues scoped
to windows so one faulted window cannot retain or execute another window's work.

A session retains its first exception, including rendering and asynchronous event
failures. Once faulted, it rejects normal callback entry and discards queued user
work. Cleanup is still admitted and visits the entire owned tree, even when a cleanup
callback throws. The application's final error report may retain a session's failure;
it does not act as an independent recovery mechanism. Metadata updates invalidate
healthy sessions but cannot revive a faulted session; restarting is required.

The first implementation slice is this execution/ingress/fault boundary. Keep the
existing retained version representation until acceptance is explicit; removing
version counters is a separate change with its own correctness tests. Regression
tests for each subsequent phase should be introduced immediately before its fix so
the normal suite remains an executable acceptance gate throughout migration.

## Acceptance and resource presence

Preparation allocates identity and declaration storage without invoking user code.
After validation, commit the entire reachable composition and props before invoking
any lifecycle callback. Mount parent before child outside user rendering. Retirement
of an unaccepted candidate cancels and releases storage without lifecycle callbacks.
Mounting failures fault the session and cleanup remains child-first.

Managed acceptance alone must not be confused with Rust accepting the published
arena. Introduce an explicit native acceptance acknowledgement when the decoded root
and retained resource declarations have been validated. The acknowledgement commits
managed state and schedules mounting before normal external dispatch resumes. Do not
mount inside the render-output callback and call that native acceptance.

A resource has a stable controller identity and a separate presence generation.
Commands require a mounted owner and an accepted declaration, capture that generation,
and are discarded when it ends. Native materialization may defer a command only within
the same generation. Removal followed by reappearance creates a new presence even if
the controller and UTF-8 key are unchanged. Base and extension commands need the same
rule, including UTF-8 Input commands. This requires a coordinated ABI revision, generated
bindings, native/managed layout tests, and [ABI.md](ABI.md) updates.

## Events and demand artifacts

Separate renderer definitions, bound sources, and cached artifacts. A renderer method
does not identify a List or Table: two controls may call the same method. Range requests
must carry source identity plus artifact identity. An artifact owns its event and future
dependency leases until native eviction, revision/theme invalidation, source removal,
or shutdown. Release is explicit and idempotent; rendering range B never retires A.

Use monotonically allocated external event IDs with reusable internal slots, checking
exhaustion rather than wrapping. A retired ID is a no-op, never an alias for a new
callback. A small ID-to-slot lookup is preferable to permanently retaining delegate
entries just to avoid reuse. Lease release clears callback/target references promptly.

Test the production dispatch and cache routes: remove/rebind under one live View,
retain A while rendering B, evict only A, and bind two sources to the same method.
Tests must establish callback liveness as well as retained-memory release.

## Reactivity and structured work

After execution, acceptance, and artifact leases are established, replace retained
version propagation with dirty flags cleared only by accepted work. A Signal binds
permanently on its first tracked read to an application identity without strongly
retaining the application. Both reads and writes then assert the owner thread.
Accept dependency edges with their consumer and detach them on retirement. Reuse
unchanged edges; a row-only dependency invalidates its artifact rather than its View.

Keep event callbacks synchronous. A View-owned asynchronous operation receives an
explicit request snapshot and cancellation token, then posts success or failure
through ingress. Check the one-shot owner lifetime when consuming the completion,
even if production ignored cancellation. Only a live owner may run the apply callback.
Capture diagnostics should flag View/controller/dispatcher captures, without claiming
that arbitrary request objects can be proven deeply immutable.

## Verification gates

Execution tests cover worker invalidation, coalescing, invalidation during rendering,
cross-window thread identity, nested callbacks, first-fault retention, dispatch after
fault, and complete teardown after a fault. Use the actual session renderer and native
callback entry points with a small fake native API for notification observation.

Acceptance, stale-event, cached-range, and late-completion regressions remain required
gates for their respective phases. Measure warm allocations and native crossings after
correctness is established. Run binding verification, managed/native suites, formatting,
and sample builds. Windows behavior and macOS behavior need separate platform evidence;
a managed test or Windows build does not verify the macOS title bar or menu path.
