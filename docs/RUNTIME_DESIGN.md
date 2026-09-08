# Runtime design

ABI 7 uses single-pass managed-owned arenas, explicit native acceptance, and cached-range artifact
leases. Arenas grow before writes without repeating user rendering. The authoring and acceptance
contracts are defined in [View lifecycle](VIEW_LIFECYCLE.md).

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

A session retains its first exception, including rendering, event dispatch, and unhandled
owned-work completion failures. Once faulted, it rejects normal callback entry and discards queued user
work. Cleanup is still admitted and visits the entire owned tree, even when a cleanup
callback throws. The application's final error report may retain a session's failure;
it does not act as an independent recovery mechanism. Metadata updates invalidate
healthy sessions but cannot revive a faulted session; restarting is required.

The outer execution boundary flushes item invalidations and retires failed sessions. Cleanup can
invalidate items in surviving windows, so the boundary drains that work and any resulting failures
before returning, retaining the first error. The ordinary successful path needs one flush.

## Acceptance and resource presence

Runtime preparation allocates identity and declaration storage without invoking user code.
Acceptance follows the [whole-tree sequence](VIEW_LIFECYCLE.md#acceptance-and-retirement-order):
commit reachable state, retire removed relationships, activate all new routes, then start effects
parent-before-child. Retirement of an unaccepted candidate cancels lifetime and releases registered
local resources without starting effects. Setup failures fault the session; cleanup remains child-first.

Managed acceptance alone must not be confused with Rust accepting the published
arena. An explicit native acceptance acknowledgement follows validation of the decoded root
and retained resource declarations. The acknowledgement commits
managed state and activates routes and effects before normal external dispatch resumes.
The render-output callback only publishes a candidate snapshot.

The acknowledgement protocol uses a non-reused 64-bit revision returned by root rendering.
Rust decodes the borrowed arena and reconciles resource declarations, then calls
`render_completed(session, revision, status)` exactly once for successfully published output.
Status zero accepts it; a decode failure faults the session without activating candidates.
Until acknowledgement, reject new root/range rendering and user dispatch. Commit all managed
props and composition before whole-tree route activation and parent-first effect setup. Invalidation from effect setup queues a later
frame; it never changes the accepted snapshot in place. The acknowledgement is a required ABI callback.

Signal updates while publication awaits acknowledgement must survive clearing staged ancestors.
Those View invalidations enter the existing coalesced ingress queue. During acceptance itself,
consumers commit parent-before-child, so revision-gap invalidations can mark their already-committed
ancestor path immediately. Effect setup runs after the entire tree commits.

Native item cache ownership also follows the frame: layout/prepaint pins requested batches, and
post-prepaint trimming evicts only idle batches. This does not delay explicit source or owner
revocation. See [Runtime boundaries](RUNTIME_BOUNDARIES.md).

A resource has a stable controller identity and a separate presence generation.
Commands require a mounted owner and an accepted declaration, capture that generation,
and are discarded when it ends. Native materialization may defer a command only within
the same generation. Removal followed by reappearance creates a new presence even if
the controller and UTF-8 key are unchanged. Base and extension commands need the same
rule, including UTF-8 Input commands. Native ingress assigns the generation from a small
accepted-presence index; the queued command retains it and validates it again before delivery.
This avoids duplicating native resource-key decoding in managed code or adding generation
fields to every command ABI record. Publish the index before the acceptance acknowledgement,
so commands from effect setup see accepted resources even before physical materialization.

## Events and demand artifacts

Separate renderer definitions, bound sources, and cached artifacts. A renderer method
does not identify a List or Table: two controls may call the same method. Range requests
must carry source identity plus artifact identity. An artifact owns its event and reactive
dependency leases until native eviction, revision/theme invalidation, source removal,
or shutdown. Release is explicit and idempotent; rendering range B never retires A.

Use monotonically allocated external event IDs with reusable internal slots, checking
exhaustion rather than wrapping. A retired ID is a no-op, never an alias for a new
callback. A small ID-to-slot lookup is preferable to permanently retaining delegate
entries just to avoid reuse. Lease release clears callback/target references promptly.

Test the production dispatch and cache routes: remove/rebind under one live View,
retain A while rendering B, evict only A, and bind two sources to the same method.
Tests must establish callback liveness as well as retained-memory release.

The concrete transport uses ABI 7. A native collection engine receives a process-unique, non-reused
64-bit source ID when created. Each range request carries that source ID alongside the renderer
token and returns a session-unique artifact ID. Managed code owns an artifact's event slots;
the native cached batch owns the corresponding release obligation. Batch eviction, invalidation,
decode failure, or source destruction releases `(session, source, artifact)` exactly once.
Repeated release is harmless. Release invokes no user code and is allowed while a root awaits
acceptance, since native resource reconciliation may retire collection engines at that boundary.

Dynamic event tokens retain the owner handle in their upper 32 bits, with a monotonically
allocated 31-bit external ID and dynamic marker below. Internal storage slots can be recycled;
an ID-to-slot map contains only live entries. Equivalent bindings reuse an ID only within the
same root-render scope or the same demand artifact. Artifact A never shares a slot with B,
even when both call the same delegate. Releasing an artifact clears its targets and delegates
immediately. Exhaustion fails explicitly; neither event IDs nor artifact/source IDs wrap.
Retired IDs and retired owner handles are ignored; malformed or never-issued identities remain
protocol errors. Root bindings and demand bindings cannot retire one another.

## Retained dirty state

Each retained View starts dirty. Entering composition marks its fragment dirty, and completing
managed rendering only stages output. Native root acceptance clears dirty flags for the reachable
Views whose composition was staged, before effect setup. A rejected publication or failed render
faults the session and retires its owned Views at the application callback boundary. A clean child copies its accepted fragment without rerunning
user rendering; the root still renders whenever native requests a new managed snapshot.

Any-thread requests continue to enter the existing coalesced ingress queue. Only the application
thread consumes them and touches retained state. Consumption marks the target and its ancestors
dirty, stopping at the first already-dirty View. Each dirty ancestor already has a path to the
root; props changes are the local exception because their parent is already composing that path.
Ambient invalidation marks all states directly. Requests arriving during rendering or while its
output awaits acceptance stay queued, so clearing accepted dirty flags cannot consume later work.

The retained state table, ownership edges, fragment arenas, and composition collections use the
application execution guard instead of locks. Teardown before the first callback owns no UI state
and remains valid on the window-opening thread. Dirty state requires no native ABI or public
authoring API. Tests cover dirty state before and after acknowledgement, rejection, queued
invalidation across acceptance, deep propagation, and reuse of unaffected sibling fragments.

## Reactivity and structured work

Signal dependencies use accepted dirty state and explicit artifact leases. The concrete graph,
ownership checks, and range transport are described in [Reactivity](REACTIVITY.md). A Signal binds
permanently on its first tracked read to an application identity without strongly
retaining the application. Both reads and writes then assert the owner thread.
Accept dependency edges with their consumer and detach them on retirement. Reuse
unchanged edges; an item-only dependency invalidates its artifact rather than its View.

Event callbacks are synchronous. A View-owned asynchronous operation receives an
explicit request snapshot and cancellation token on the calling UI thread. The application
owns offloading and async context policy. A task observer posts success or failure
through ingress. Check the one-shot owner lifetime when consuming the completion,
even if production ignored cancellation. Only a live owner may run the apply callback.
Producer diagnostics require directly supplied static lambdas or methods, without claiming
that arbitrary request objects can be proven deeply immutable. Event diagnostics reject
visible async handlers and discarded tasks. Indirect delegates carry the same synchronous
contract without runtime delegate reflection. See [Asynchronous work](ASYNC_WORK.md) for the
ownership and diagnostic contracts.

## Verification gates

Execution tests cover worker invalidation, coalescing, invalidation during rendering,
cross-window thread identity, nested callbacks, first-fault retention, dispatch after
fault, and complete teardown after a fault. Use the actual session renderer and native
callback entry points with a small fake native API for notification observation.

Acceptance tests cover candidate cleanup without effect setup, whole-tree props commit, route
activation, effect ordering and failure, unmatched acknowledgements, and exclusion of dispatch
while awaiting acceptance. Native tests cover acknowledgement after decoding, absence rejection,
and generation checks at command
delivery. Event and artifact tests cover stale-token dispatch, independent sources and ranges,
capture release, bounded slot reuse, native eviction and invalidation, and declaration removal
while a frame retains the engine. Late-completion and dependency regressions exercise revocation
and accepted observation boundaries. Measure warm allocations and native crossings after
correctness is established. Run binding verification, managed/native suites, formatting,
and sample builds. Windows behavior and macOS behavior need separate platform evidence;
a managed test or Windows build does not verify the macOS title bar or menu path.
