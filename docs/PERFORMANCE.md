# Performance contract

The performance goal is to avoid unnecessary crossings and allocations, not merely to make each
FFI call fast.

## Dirty and clean frames

A dirty managed tree normally requires one root render callback and one acceptance callback. Rendering
writes a whole semantic tree into a flat arena. Rust validates and retains the decoded snapshot.

A clean GPUI repaint must not call managed `Render()`.

After arena warmup:

- `RenderContext` is stack-only;
- `Element<TTag>` is a small value;
- child APIs accept spans;
- event tokens use compact non-reused IDs mapped to recyclable registry slots;
- framework child activation uses generated factories;
- native snapshot buffers and decode scratch are reused;
- retained composition uses application-thread dirty flags without locks or version counters;
- invalidation walks stop at an already-dirty ancestor, and clean sibling fragments are reused.

Do not replace this model with per-element or per-style P/Invoke calls.

## View and Signal creation

Release measurements on Windows x64 / .NET 10.0.11:

| Construction pattern | Managed B/instance |
| --- | ---: |
| Empty `View` subclass | 152 |
| `View<int>` subclass, props not yet supplied | 168 |
| Empty View followed by its first `Lifetime` access | 200 |
| View containing one initialized `Signal<int>` field | 216 |
| `new Signal<int>(0)` | 56 |

The empty View includes its runtime identity, lifecycle lock, and Dispatcher. No mounted
attachment, command route, cancellation source, or work scope exists yet. The Signal-owning View
adds an eight-byte reference field and the 56-byte Signal. First lifetime access adds a 48-byte
cancellation source. The construction probe stores every instance in a preallocated array, keeping
the objects observable while excluding array allocation. It warms type initialization; these are
fresh object costs, not process startup costs.

The first accepted render of a new root returning only constant Text allocates **1,400 managed
bytes** in the session fixture. This includes preparation, retained/render bookkeeping, first
capacity growth, and acceptance/mounting. View/application/session construction and disposal are
outside that interval. The fixture renders roots sequentially, so the bounded attachment pool is
warm. This is not the cost of opening a native window: native allocation, layout, and painting
are outside the managed allocation counter.

## Signal dependencies

Signal reads reuse consumer edges and make no native call. Accepted subscriptions use linked
edges, so writes traverse actual subscribers without locks or temporary collections. Conditional
dependencies detach at acceptance; retirement clears them before user cleanup. Tracking stable
dependencies and coalesced writes allocate nothing after edge and collection warmup. Returning to
a removed dependency allocates a new edge. Artifact keys batch at the
outer callback boundary using reusable managed storage; native ingress copies that batch once.

The isolated dependency probe reads Signals inside `ReactiveConsumer.Begin()` and commits the
observations. Consumer, Signal, and session creation are outside the measurement. First-subscription
cases use a fresh consumer for each operation; the dictionary and edges allocated by tracking are
included. These numbers are per complete pass, not per Signal:

| Tracking pattern | Managed B/pass |
| --- | ---: |
| First read/accept of one distinct Signal | 288 |
| First read/accept of eight distinct Signals | 1,568 |
| First read/accept of 32 distinct Signals | 4,384 |
| Stable read/accept of 1, 8, or 32 Signals | 0 |
| Read the same Signal 32 times, then accept, after warmup | 0 |
| Alternate between two dependencies, accepting each change | 72 |
| Accept no dependencies, then read/accept the previous Signal again | 72 per detach/resubscribe pair |

Unbound reads, equal-value writes, and changed writes without subscribers each measure **0 B/op**
after warmup. The first-dependency costs include dictionary creation/growth as well as edges;
each edge is 72 bytes in this runtime. Removed edges are not retained for possible future branches.
Repeated switching therefore continues allocating after warmup. This trades allocation for releasing
references to Signals that the View no longer reads.

The integration probe uses real retained child Views under one parent. Each reader tracks its own
following flag and, while active, a shared selector plus the selected data Signals. Child keys and
props remain stable. Each cycle measures writes separately from managed rendering and acceptance:

| Dependency/update pattern | Write B/cycle | Render + accept B/cycle |
| --- | ---: | ---: |
| One Signal shared by 1, 8, or 32 child Views | 0 | 0 |
| One child reading 8 or 32 Signals; change all before rendering | 0 | 0 |
| Eight readers switch between two data Signals | 0 | 576 |
| Eight readers, four paused; change the shared Signal | 0 | 0 |
| Equal write with eight readers | 0 | 0 |
| 32 changed writes before rendering eight readers | 0 | 0 |

The tests check dirty state, observed values, reader render counts, and notification coalescing
outside the allocation intervals. Paused readers stay clean, equal writes rerender no child, and
changed writes produce one notification per cycle. Constant text prevents application number
formatting from contaminating the render measurements. Zero allocation does not imply constant
execution time: writes still traverse subscribers, and rendering still visits affected Views.

All creation, tracking, and integration measurements use `GC.GetAllocatedBytesForCurrentThread`,
four warmup batches, and three measured batches of 128 operations. All three measured batches
produced the values above. Test setup, assertions, and output are excluded. The integration fixture
uses a native notification stub; native decoding, layout, painting, and virtual-row cache costs
are not measured. These are allocated bytes, not retained memory or GC pause measurements.

Reproduce the View/Signal measurements with:

```sh
dotnet test tests/Gpui.Tests/Gpui.Tests.csproj --no-restore -m:1 -c Release --filter "FullyQualifiedName~CreationAllocations|FullyQualifiedName~FirstAcceptedRenderAllocations|FullyQualifiedName~SignalAccessAllocations|FullyQualifiedName~TrackingAllocations|FullyQualifiedName~UpdateAllocations" --logger "console;verbosity=detailed"
```

## Task observation

`ViewContext.Work` is an optional mounted capability. `WorkScope.Start` invokes the producer on
the calling UI thread; application code owns offloading. One operation object owns completion
state, observes the Task, and enters ingress. Pending tasks additionally need one continuation
delegate. There is no framework worker job, async wrapper Task, or per-operation weak reference.

`RuntimeExecutionTests.WorkAllocationPatterns` measures these patterns in Release on Windows x64 /
.NET 10.0.11. All three measured batches produced the same values:

| Pattern | Start (B/op) | Task completion and observation (B/op) | Total (B/op) |
| --- | ---: | ---: | ---: |
| Completed Task, static callbacks | 96 | 0 | 96 |
| Pending Task, static callbacks | 160 | 0 | 160 |
| Already-cancelled Task | 96 | 0 | 96 |
| Pending Task cancelled by its source | 160 | 80 | 240 |
| Already-faulted Task, handled | 288 | 0 | 288 |
| Pending Task faulted by its source, handled | 160 | 440 | 600 |
| Producer throws a failure | 272 | 0 | 272 |
| Producer throws cancellation | 272 | 0 | 272 |
| Completed Task, fresh callback capturing `this` | 256 | 0 | 256 |
| Completed Task, cached instance callback | 96 | 0 | 96 |
| Completed Task, fresh callback capturing a local | 280 | 0 | 280 |
| Completed Task, cached multicast callback | 96 | 0 | 96 |
| Producer creates a completed Task with `Task.FromResult(42)` | 168 | 0 | 168 |
| Producer awaits a pending Task with `ConfigureAwait(false)` | 272 | 0 | 272 |

The probe uses `GC.GetAllocatedBytesForCurrentThread`, four warmup batches, and three measured
batches of 128 operations. Task sources complete inline on the calling thread with no current
synchronization context. Scope creation, initial capacity growth, Task/source/exception construction,
assertions, and UI ingress draining/rendering are excluded. The fresh-Task and async-producer rows
intentionally include those application allocations. Each fault uses a separate exception to avoid
accumulating stack history across operations. Source cancellation/fault bookkeeping is included in
the observation column; already-completed Task observation occurs inside Start.

The baseline is one 96-byte operation object. Pending observation adds one 64-byte delegate.
Fresh capturing callbacks add delegate allocation and runtime method metadata used by synchronous
callback validation; a local capture also adds a closure object. Caching a callback avoids both
repeated delegate construction and method inspection allocation. Static callbacks with explicit
state are the default authoring pattern. Cancellation is detected from Task status without creating
a TaskCanceledException. Fault observation reads the Task's first exception without rethrowing it,
but accessing Task.Exception still allocates its aggregate representation.

These are warmed registration and observation allocations, not retained memory, full application
cost, or a cross-thread scheduling benchmark. Context capture, service implementation, first-use
metadata, and different result/state layouts can change the numbers. Reproduce with:

```sh
dotnet test tests/Gpui.Tests/Gpui.Tests.csproj --no-restore -m:1 -c Release --filter "FullyQualifiedName~WorkAllocationPatterns" --logger "console;verbosity=detailed"
```

## Native interaction loops

Keep continuous interaction in Rust:

- Scroll wheel/trackpad and scrollbar drag update `ScrollHandle` natively.
- Input editing, selection, caret movement, IME, and horizontal reveal stay native.
- Slider pointer drag and keyboard stepping stay native.
- Dock tab dragging, drop targeting, split resizing, activation, and focus stay native.
- title-bar drag and caption hit testing stay native.
- overlay positioning and dismissal stay native.

Managed callbacks should represent application state transitions: clicks, opted-in control events,
menu actions, and dirty rendering. Movement and scroll-wheel observer events are the deliberate
opt-in exception: they cross per pointer event, but only while a binding is registered, and
their handlers must record cheaply and return. Unregistered elements pay nothing.

Dock panel content is materialized from the retained root snapshot during native frames. A dirty
managed render updates panel content proxies, but an unchanged structural declaration does not
rebuild the native Dock layout. This preserves user tab placement and splitter sizes without a
layout event crossing on each pointer delta.

`ui.Dynamic(active, child)` is the explicit exception for app-defined visual animation. Native
GPUI synchronizes requests to display frames and deduplicates active wrappers by owning View, but
each frame still performs managed rendering, snapshot decode, and materialization. Keep the dynamic
subtree compact and disable the wrapper as soon as the animation completes.

## Notifications and resource commands

Managed invalidation requests coalesce per stable View identity before application-thread
consumption; native notifications also coalesce per session. No worker invalidation traverses or
locks the retained tree. Resource
commands use a lossless queue because order can be semantically important.

If a new command is high-frequency, first decide whether the state belongs natively. If it must
cross, define an explicit coalescing or batching policy rather than relying on queue speed.

## UTF-8 path

The render arena stores text and keys as UTF-8. Prefer UTF-8 APIs on hot paths:

- `Utf8InputOptions` writes caller bytes directly;
- `InputController.SetValue(ReadOnlySpan<byte>)` avoids a managed payload array;
- `InputEvent.Utf8Value` owns copied bytes for async safety;
- `InputEvent.Value` decodes lazily.

Do not introduce unconditional UTF-16 round trips for keys, row data, or native control events.

## Virtual list crossing budget

GPUI may request individual indices, but Rust aligns a cache miss to the configured batch size
(default 48):

```text
GPUI requests:       4200, 4201, ...
native cache miss:   batch 4176..4223
managed crossings:  1
```

At most four batches are retained per List/Table row engine. Scrolling inside retained batches
requires no managed call. A missing batch requires one `list_render_range` call containing all
rows in that batch and one `accept_artifact` call after validation.

Each batch retains its own managed event lease. Eviction or invalidation adds one artifact-release
callback per retired batch, never per row. Cache hits require no managed call. Binding storage is
reused after release, while external event IDs never alias a later binding. Two row engines using
the same generated renderer have independent source IDs and leases.

`ListDataSource(count, contentRevision)` lets batches survive unrelated root renders. Increment the
revision only when row output can change. Theme changes and table column changes invalidate row
snapshots while preserving viewport and measurement state.

## Row rendering

The generated row dispatcher invokes the `[GpuiListItem]` method once per requested row, but all
rows are written into one arena and returned through one callback.

Avoid:

- managed View objects per datasource row;
- one closure allocation per row;
- string-formatted native element IDs in the row hot path;
- nested retained resources;
- side effects that would run twice after arena growth.

Use a stable `.ItemId` and the unmanaged click payload for model identity.

## Measurement targets

Track at least:

- dirty root render time and managed allocations;
- clean repaint managed callback count (must be zero);
- scroll-delta managed callback count (must be zero);
- virtual row crossings, cache hits, evictions, and invalidations;
- cache retention across unrelated renders;
- arena high-water capacities, growth counts, and bytes relocated (capacity rerenders must be zero);
- Input UTF-8 allocation and decode-on-demand behavior;
- list/table reset, splice, refresh, and theme-change behavior.

Set `GPUI_DOTNET_TRACE=1` to print per-stage native timings and cumulative list-cache telemetry while
running the sample.
