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

Native-to-managed render, virtual-row, dynamic-frame, event, startup, and window-close callbacks
originate from GPUI foreground work. Managed mounting, rendering, event-table access,
reconciliation, and unmounting therefore stay serialized on that thread once a View begins
mounting. A root retired before its first native render has never mounted, so neither lifecycle
hook runs.

During native callbacks, the binding installs a per-window `GpuiSynchronizationContext`. Normal
`await` continuations from an event handler are posted to that session and drained at the start of
a later root-render callback. `ConfigureAwait(false)` deliberately leaves this context; code
running there must use one of the any-thread entry points to return to the View.

`OnMounted` means that a C# View has joined a managed window session. It does not mean that GPUI
created another entity or that the View has already appeared in a committed snapshot.
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
asserts the managed thread that mounted it.

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
| Read or mutate ordinary View fields | GPUI application thread unless the application adds its own synchronization |
| `Invalidate()` | Any thread while mounted |
| `Dispatcher.Post(...)` | Any thread while mounted; callback runs on the GPUI application thread |
| Window and retained-resource controller commands | Any thread while mounted; GPUI mutation runs on the GPUI application thread |
| `Lifetime` cancellation observation | Any thread |

Any-thread support is an ingress guarantee, not general thread safety for a View. A worker should
compute or perform I/O, then use `Dispatcher.Post` or a captured synchronization-context
continuation to modify View state. Passing `Lifetime` to the work prevents a continuation from
treating a terminal View as mounted.

## Render and teardown ordering

Managed render and row callbacks are synchronous. Native arena growth can retry them, so they must
remain deterministic and side-effect free. Posted callbacks are drained before root rendering;
their state changes are included in that render.

Unmount proceeds child-first. For each View, the binding marks it unmounting, removes and
deactivates runtime access, cancels `Lifetime`, invokes `OnUnmounted`, then releases retained props
and marks the instance terminal. Session/native commands are unavailable inside `OnUnmounted`.
See [View lifecycle](VIEW_LIFECYCLE.md) for slot and transactional-render details.
