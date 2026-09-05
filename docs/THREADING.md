# Lifecycle and threading

GPUI.NET has two related but different lifetime models: GPUI's Rust entities and the managed View
tree. Do not infer one from the other.

## GPUI's model

`gpui::App`, `Window`, `Context<T>`, rendering, input dispatch, observers, and effect flushing are
foreground-thread operations. GPUI's `ForegroundExecutor` runs work on the platform main thread
and is deliberately not `Send`.

A GPUI `Entity<T>` is a strong handle to an object in the application's entity map. The entity's
lifetime is determined by strong-handle count, not by whether it appeared in the latest element
tree. Dropping the final handle records the entity for release. GPUI removes it and invokes
`on_release` observers later from `App::flush_effects()` on the foreground thread. Consequently,
dropping a handle and running release cleanup are not the same operation or necessarily the same
thread.

These details are visible in the pinned GPUI sources:

- [`ForegroundExecutor`](https://github.com/zed-industries/zed/blob/2ead8c42fb6792095d7cb02f7b89e467421dc8a0/crates/gpui/src/executor.rs)
- [`Entity<T>` reference counting](https://github.com/zed-industries/zed/blob/2ead8c42fb6792095d7cb02f7b89e467421dc8a0/crates/gpui/src/app/entity_map.rs)
- [`App::flush_effects` and entity release](https://github.com/zed-industries/zed/blob/2ead8c42fb6792095d7cb02f7b89e467421dc8a0/crates/gpui/src/app.rs)

## The binding's model

Each native window owns one GPUI `Entity<ManagedView>`. The C# root and child `View` instances are
not separate GPUI entities. They form a binding-managed retained tree whose fragments are combined
into the snapshot consumed by that native `ManagedView`.

Native-to-managed render, acceptance, virtual-row, dynamic-frame, event, startup, and window-close callbacks
originate from GPUI foreground work. Managed mounting, rendering, event-table access,
reconciliation, and unmounting therefore stay serialized on that thread once a View is prepared.
A root or candidate retired before native acceptance has never mounted, so neither lifecycle
hook runs.

One application-owned execution guard binds to the actual GPUI callback thread and is shared
by every window. It checks root/range rendering, acceptance, dynamic-frame callbacks, event dispatch, and
cleanup before they access retained state. External callback entry cannot reenter an active
callback, including through another window. Internal child rendering remains part of the root
callback. Synchronous synchronization-context dispatch cannot bypass this guard.

During native callbacks, the binding installs a per-window `GpuiSynchronizationContext` for
foreground dispatch. Events are synchronous and do not return `Task` or `ValueTask`. The context
does not confer View ownership on manually detached work; use the explicit owned-work boundary.

Use `WorkScope.Start` for View-owned production. It invokes a static producer with an explicit request
and lifetime token on the calling application thread. The application owns offloading and its
async continuation/context policy; the framework does not schedule the producer onto a worker.
Acquire the scope through `ViewContext.Work`. It owns pending operations and clears their UI state
and route references on retirement. Results return through the stable command route
and are applied only while that owner remains mounted. Retirement drops pending callbacks before
cancellation, even when a producer ignores its token. See [Asynchronous work](ASYNC_WORK.md).

`OnMounted` means that Rust accepted the View's snapshot and the reachable managed tree and props
have committed. It runs outside rendering, parent-before-child, before native materialization.
It does not imply a separate GPUI entity or a completed paint.
`OnUnmounted` means that the window or committed child slot no longer owns that C# View. It is not
triggered merely because GPUI skipped a paint. Terminal, one-shot C# View lifetime is a binding API
contract, not a constraint imposed by GPUI's entity map.

## State split inside a managed View

Mounted Views keep two different runtime objects:

- `ViewCommandRoute` is stable for that mount, safe to acquire from any managed thread, and never
  pooled. It contains only the immutable owner handle and thread-safe session/native command
  entry points, including generic extension commands. Unmount deactivates it before lifecycle
  cleanup.
- `MountedViewAttachment` contains the native owner handle, event-binding passes and entries, and
  resource-key sequence. It is accessible only on the GPUI application thread. Unmount removes,
  completely resets, and then pools it for another View.

This split prevents a stale worker-thread command from observing an attachment after it has been
recycled. The command route serializes dispatch against deactivation and carries its own immutable
owner handle; it never reads pooled state. Internal access to a live `MountedViewAttachment`
asserts the managed thread that prepared it. The command route activates only when mounting begins.

Lifecycle identity remains directly on `ViewBase`: the terminal state and lazily allocated
`CancellationTokenSource` are never pooled. If `Lifetime` is never requested, no source is
allocated.

The mounted attachment needs no monitor: render, event binding/dispatch, and resource-key
allocation are foreground-thread-only. The remaining lifecycle lock protects only rare
mount/unmount and lazy-token races. The command-route lock is also outside rendering; it makes
deactivation linear with any command already entering from another thread.

## Allowed calls by thread

| Operation | Thread contract |
| --- | --- |
| `Render()`, `[GpuiListItem]`, lifecycle hooks, event callbacks | GPUI application thread |
| Child reconciliation, props commit, event binding | GPUI application thread |
| Bound Signal reads and writes | Owning application's GPUI thread; writes forbidden during rendering |
| Read or mutate ordinary View fields | GPUI application thread unless the application adds its own synchronization |
| `Invalidate()` | Any thread while mounted; queues a coalesced request |
| `Dispatcher.Post(...)` | Any thread while mounted; callback runs on the GPUI application thread |
| Window and retained-resource controller commands | Any thread while mounted; GPUI mutation runs on the GPUI application thread |
| `Lifetime` cancellation observation | Any thread |

Any-thread support is an ingress guarantee, not general thread safety for a View. Use `WorkScope.Start`
to compute or perform I/O and apply live results through foreground ingress. Cancellation alone
does not establish ownership: producers may ignore their token, so completion must recheck the
original View's route. `Dispatcher.Post` provides that check for manually posted synchronous work.

Retained-resource commands also require an accepted declaration. Native ingress reads a small
thread-safe presence index and stamps the queued command with its generation. Delivery rechecks
the generation on the application thread; removal and reappearance cannot revive queued commands.
This index contains identities and generations, never GPUI entities or managed retained trees.

## Render and teardown ordering

Managed render and row callbacks are synchronous. Their managed-owned buffers grow before writes
without capacity retry. They must remain deterministic and side-effect free. Posted callbacks are drained before root rendering;
their state changes are included in that render. Each drain has a bounded work budget so a
self-posting callback cannot prevent rendering indefinitely; excess work requests a later frame.

Successful root output awaits a matching native acknowledgement before another render, demand
request, or user event can enter. Rust releases borrowed arena data and publishes resource presence
before acknowledging. Acceptance commits all props and composition before lifecycle hooks. Mount
commands may queue at this point; mount invalidations remain queued for a later frame.

Artifact release is a framework-only cleanup callback. Native batch eviction or source removal
may invoke it while root acceptance is pending; it releases event slots without running user
callbacks or resetting output arenas. Release is also admitted after a session fault, and is
idempotent after owner or session teardown.

Artifact acceptance commits reactive dependencies after native decoding. Signal changes queue
artifact keys and flush one batch per affected session at the outer callback boundary. Native
delivery requests repaint without managed root invalidation. Ordinary bound Signal writes outside
a callback use a short application mutation scope to provide the same flush boundary.

Invalidation publishes a stable, never-pooled View identity with an atomic pending bit. Repeated
requests coalesce before reaching the application thread. Only ingress consumption touches the
retained tree. Its tables, ownership edges, and dirty flags are application-thread-owned and need
no locks. Dirty propagation stops at an already-dirty ancestor, and acceptance clears only the
staged compositions it commits. Requests arriving during rendering or pending acceptance apply
on a later render. Theme and metadata updates likewise enqueue full-tree invalidation. Native
wakeups coalesce per session.

View-bound posted callbacks and owned-work completions recheck their stable command route when
consumed and are discarded after owner retirement. Event dispatch does not retain pending tasks
or install task-completion observers.

The first unexpected render, demand-render, event, lifecycle, or posted-callback failure is
terminal for normal session execution. Later render/event callbacks fail without invoking user
code, queued user work is discarded, and resource commands are no longer forwarded. Other windows
remain usable. Metadata updates cannot clear a terminal fault. Cleanup still visits all owned
Views and preserves the original failure even if cleanup also throws.

Unmount proceeds child-first. For each View, the binding marks it unmounting, removes and
deactivates runtime access, cancels `Lifetime`, invokes `OnUnmounted`, then releases retained props
and marks the instance terminal. Session/native commands are unavailable inside `OnUnmounted`.
See [View lifecycle](VIEW_LIFECYCLE.md) for slot and transactional-render details.
