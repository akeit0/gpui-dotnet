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

Child fragments check root identity, generation, and index before copying. Full managed semantic
validation runs once per assembled root or row batch, preserving diagnostics without rescanning
descendants at every fragment boundary. Native validation remains authoritative at acceptance.
Managed structural validation indexes parent links, child counts, and final Dock operation values once.
An iterative ancestor walk rejects disconnected cycles and resolves nearest Dock areas. Panel IDs
are sorted by area and their UTF-8 bytes, avoiding pairwise ancestry searches and string allocation.
Graph work is O(nodes + edges + operations); panel uniqueness adds O(panels log panels) byte
comparisons. Small scratch buffers use the stack; larger buffers use ArrayPool and return on both
success and failure. Cold pool growth can allocate. Wire flags remain untouched.

Native validation reuses vectors for graph and resource indexes. Its graph traversal propagates
the nearest Dock area from parent to child; final active-index and region-side operations are
indexed once. Resource keys and Dock panel IDs use sorted UTF-8 offset records, adding
O(keys log keys) byte comparisons without allocating key strings. Dock snapshots lazily allocate
eight scratch bytes per node and retain that capacity for reuse; ordinary row snapshots allocate
no Dock buffer. Scratch retains no borrowed arena pointers.

Acceptance activates only newly mounted Views. Factory-owned child slots reuse accepted children;
unaccepted candidates retain ownership edges solely for failed-render cleanup. Retirement scratch
collections clear their View references immediately after use while retaining collection capacity.

## View and Signal creation

Release measurements on Windows x64 / .NET 10.0.11:

| Construction pattern | Managed B/instance |
| --- | ---: |
| Empty `View` subclass | 216 |
| `View<int>` subclass, props not yet supplied | 232 |
| Empty View followed by its first `Lifetime` access | 264 |
| View containing one initialized `Signal<int>` field | 280 |
| `new Signal<int>(0)` | 56 |
| `new Signal<int>(0)` exposed as `IReadOnlySignal<int>` | 56 |

The empty View includes its construction ownership scope, runtime identity, and lifecycle lock. Dispatcher is a value handle,
so it adds no separate allocation or stored runtime field. No mounted
attachment, command route, cancellation source, or work scope exists yet. The Signal-owning View
adds an eight-byte reference field and the 56-byte Signal. First lifetime access adds a 48-byte
cancellation source. The construction probe stores every instance in a preallocated array, keeping
the objects observable while excluding array allocation. It warms type initialization; these are
fresh object costs, not process startup costs.

The first accepted render of an already constructed test root returning only constant Text allocates **1,416 managed
bytes** in the session fixture. This includes preparation, retained/render bookkeeping, first
capacity growth, and acceptance/mounting. View/application/session construction and disposal are
outside that interval. The fixture renders roots sequentially, so the bounded attachment pool is
warm. This is not the cost of opening a native window: native allocation, layout, and painting
are outside the managed allocation counter.

## Signal dependencies

Signal reads reuse consumer edges and make no native call. Accepted subscriptions use linked
edges, so writes traverse actual subscribers without locks or temporary collections. Conditional
dependencies detach at acceptance; retirement clears them before user cleanup. Tracking stable
dependencies and coalesced writes allocate nothing after edge and collection warmup. Consumers
reuse up to eight detached edges with cleared Signal references. Artifact keys batch at the
outer callback boundary using reusable managed storage; native ingress copies that batch once.

The isolated dependency probe reads Signals inside `ReactiveConsumer.Begin()` and commits the
observations. Consumer, Signal, and session creation are outside the measurement. First-subscription
cases use a fresh consumer for each operation; collection storage and edges allocated by tracking are
included. These numbers are per complete pass, not per Signal:

| Tracking pattern | Managed B/pass |
| --- | ---: |
| First read/accept of one distinct Signal | 128 |
| First read/accept of eight distinct Signals | 720 |
| First read/accept of 32 distinct Signals | 2,880 |
| First read/accept of 65 distinct Signals, including dictionary conversion | 7,912 |
| Stable read/accept of 1, 8, 32, or 128 Signals | 0 |
| Read the same Signal 32 times, then accept, after warmup | 0 |
| First read/accept of 1 / 8 Signals through `IReadOnlySignal<int>` | 128 / 720 |
| Stable read/accept of 1 or 8 Signals through `IReadOnlySignal<int>` | 0 |
| Alternate between disjoint sets of 1 or 8 dependencies, after warmup | 0 |
| Alternate between disjoint sets of 32 dependencies, after warmup | 1,728 |
| Alternate between disjoint sets of 128 dependencies, after warmup | 8,640 |
| Accept no dependencies, then read/accept the previous Signal again | 0 per detach/resubscribe pair after warmup |

Unbound reads, equal-value writes, and changed writes without subscribers each measure **0 B/op**
after warmup. Exposing a Signal through the read-only interface introduces no wrapper and does
not change these allocation costs or the underlying dependency identity. The first-dependency
costs include array creation/growth and dictionary conversion
for large sets, as well as edges;
each edge is 72 bytes in this runtime. Each consumer retains at most eight spare records (576 bytes)
after removing their Signal references. It releases that storage on retirement. This bounds the
retained-memory tradeoff while making common conditional branches allocation-free after warmup.
Switching 32 dependencies reuses eight records and allocates the remaining 24 (1,728 bytes).
Collection tests cover both removed accepted dependencies and rejected observations beyond the
spare limit. Active edges remain attached until acceptance; their storage cannot be reused early.

The integration probe uses real retained child Views under one parent. Each reader tracks its own
following flag and, while active, a shared selector plus the selected data Signals. Child keys and
props remain stable. Each cycle measures writes separately from managed rendering and acceptance:

| Dependency/update pattern | Write B/cycle | Render + accept B/cycle |
| --- | ---: | ---: |
| One Signal shared by 1, 8, or 32 child Views | 0 | 0 |
| One child reading 8 or 32 Signals; change all before rendering | 0 | 0 |
| Eight readers switch between two data Signals | 0 | 0 |
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

### Dependency lookup strategy

One `_edges` field holds either a dense array or a dictionary. Linear search handles up to 64
active/provisional edges. Adding the 65th replaces the array with a dictionary referencing the
same edge objects. The dictionary remains until retirement; shrinking or rejecting a render does
not repeatedly convert storage. Array acceptance/rejection compacts survivors and clears vacated
slots; dictionary acceptance/rejection removes entries before recycling edges.

`DependencyLookupCost` compares whole tracked read/accept passes with alternating forward/reverse
read order. Local Release measurements on the same Windows x64 / .NET 10.0.11 environment are:

| Dependencies | Dictionary-only baseline ns/pass | Array-only prototype ns/pass | Array switching to dictionary ns/pass |
| --- | ---: | ---: | ---: |
| 1 | 23.0 | 16.3 | 20.6 |
| 4 | 85.9 | 48.9 | 58.7 |
| 8 | 158.8 | 93.3 | 109.1 |
| 16 | 334.6 | 190.5 | 215.6 |
| 32 | 645.1 | 434.6 | 493.8 |
| 64 | 1,361.2 | 1,283.5 | 1,411.7 |
| 256 | 5,508.5 | 13,817.3 | 6,065.9 |

Each value is the median of five batches of 4,096 passes after three warmup batches. Tiered
compilation was disabled for this comparison to avoid measuring different JIT tiers. Setup,
assertions, and reporting are outside the timed interval. These are separate local runs without
CPU affinity or clock control, not portable latency guarantees or CI speed assertions. The results
support linear storage for small sets and conversion near the measured crossover; the switching
version adds type-dispatch overhead and does not improve every size. First-subscription allocations
also fall from the dictionary baseline's 288/1,568/4,384 bytes at 1/8/32 dependencies to the current
128/720/2,880 bytes. Reproduce current timing in PowerShell with:

```powershell
$env:DOTNET_TieredCompilation = "0"
dotnet test tests/Gpui.Tests/Gpui.Tests.csproj --no-restore -m:1 -c Release --filter "FullyQualifiedName~DependencyLookupCost" --logger "console;verbosity=detailed"
Remove-Item Env:DOTNET_TieredCompilation
```

## Explicit dispatch

`Dispatcher` is a readonly struct; obtaining and copying a handle adds no managed allocation.
Default handles reject commands. `Post(state, static callback)` avoids the closure needed to
capture per-call state. Both overloads defer execution and drop delivery after the original View
retires. For example:

```csharp
dispatcher.Post((view, value), static state => state.view.Apply(state.value));
```

The warmed `DispatcherAllocationPatterns` test measures queue admission with the same Release
runtime, batch sizes, and native stub as the View/Signal probes. Draining the queue and rendering
are outside the interval:

| Pattern | Managed B/post |
| --- | ---: |
| `Post(static () => ...)`, no state | 32 |
| `Post(state, static state => ...)`, reference state | 40 |
| `Post(() => ...)`, fresh local capture | 120 |

The explicit-state form allocates one typed ingress record; larger value-type states increase
its size. It does not eliminate queue storage or application allocations. Reproduce with the
same test command and `--filter "FullyQualifiedName~DispatcherAllocationPatterns"`.

## Task observation

`ViewConstruction.Work` acquires an optional View-owned handle; `EffectScope.Work` owns operations
for one accepted relationship. Production requires an active scope. `WorkScope.Start` invokes the producer on
the calling UI thread; application code owns offloading. One operation object owns completion
state, observes the Task, and enters ingress. Pending tasks additionally need one continuation
delegate. There is no framework worker job, async wrapper Task, or per-operation weak reference.

`RuntimeExecutionTests.WorkAllocationPatterns` measures these patterns in Release on Windows x64 /
.NET 10.0.11. All three measured batches produced the same values:

| Pattern | Start (B/op) | Task completion and observation (B/op) | Total (B/op) |
| --- | ---: | ---: | ---: |
| Completed Task, static callbacks | 104 | 0 | 104 |
| Pending Task, static callbacks | 168 | 0 | 168 |
| Already-cancelled Task | 104 | 0 | 104 |
| Pending Task cancelled by its source | 168 | 80 | 248 |
| Already-faulted Task, handled | 296 | 0 | 296 |
| Pending Task faulted by its source, handled | 168 | 440 | 608 |
| Producer throws a failure | 280 | 0 | 280 |
| Producer throws cancellation | 280 | 0 | 280 |
| Completed Task, fresh callback capturing `this` | 168 | 0 | 168 |
| Completed Task, cached instance callback | 104 | 0 | 104 |
| Completed Task, fresh callback capturing a local | 192 | 0 | 192 |
| Completed Task, cached multicast callback | 104 | 0 | 104 |
| Producer creates a completed Task with `Task.FromResult(42)` | 176 | 0 | 176 |
| Producer awaits a pending Task with `ConfigureAwait(false)` | 280 | 0 | 280 |

The probe uses `GC.GetAllocatedBytesForCurrentThread`, four warmup batches, and three measured
batches of 128 operations. Task sources complete inline on the calling thread with no current
synchronization context. Scope creation, initial capacity growth, Task/source/exception construction,
assertions, and UI ingress draining/rendering are excluded. The fresh-Task and async-producer rows
intentionally include those application allocations. Each fault uses a separate exception to avoid
accumulating stack history across operations. Source cancellation/fault bookkeeping is included in
the observation column; already-completed Task observation occurs inside Start.

The baseline is one 104-byte operation object. Pending observation adds one 64-byte delegate.
Fresh capturing callbacks add delegate allocation; a local capture also adds a closure object.
Callback admission does not inspect method metadata. Caching a callback avoids repeated delegate
construction. Static callbacks with explicit
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

Each List/Table row engine retains its active frame batches and up to four idle batches. Scrolling inside retained batches
requires no managed call. A missing batch requires one `list_render_range` call containing all
rows in that batch and one `accept_artifact` call after validation.

Validation/grouping scratch belongs to the row engine and is reused across serial decodes;
cached batches retain no scratch or string-interner tables. Snapshot strings remain independently
owned after the temporary batch interner is dropped. A snapshot without data-valued operations
does not allocate an operation-string table. Numeric operations and callback tokens need no such
table; snapshots using font-family or other string-valued operations allocate it lazily.

Frame layout/prepaint pins every requested batch. After prepaint, trimming retains those batches
plus at most four idle batches; the viewport and overdraw demand determine the live working set.
Source removal and explicit invalidation still release affected batches immediately.

Each batch retains its own managed event lease. Eviction or invalidation adds one artifact-release
callback per retired batch, never per row. Cache hits require no managed call. Binding storage is
reused after release, while external event IDs never alias a later binding. Two row engines using
the same generated renderer have independent source IDs and leases.
Each View with demand artifacts keeps a dense lease index. Eviction removes an entry by moving
the last lease into its slot; View retirement visits only that owner's leases, including pending
publications and rows without events. The index adds one optional reference per retained View,
one slot number per session artifact entry, and a lazily allocated list of artifact IDs per row
owner. Empty lists retain their capacity for reuse until the View retires.
Equivalent row bindings search only their current artifact's live slots. Each artifact stores a
head index; event entries link to the next slot, so binding and release need no per-artifact list.
Release walks the chain before clearing and recycling its slots. External tokens remain unique
even when a different artifact reuses the same storage. Root bindings have a
separate slot index for lookup and retirement, independent of cached batches and unused storage.
Root retirement compacts this index; released entries still receive fresh external IDs on reuse.
This adds a lazy list per event registry containing root bindings; small linear searches remain
within each scope rather than introducing a global callback dictionary.

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

### Running timing probes

Ordinary `dotnet test` excludes tests marked `Category=Performance`. Allocation contract tests
remain in the correctness suite. Timing probes run separately in Release with tiered compilation
disabled, using the same production runtime code and fixtures:

```powershell
./eng/measure-runtime.ps1
```

The script restores the caller's environment and working directory. Its default filter selects
all timing probes, which share one xUnit collection and execute sequentially. Use `-Filter` to
select a particular probe or allocation report. Explicit `dotnet test --filter` overrides the
default exclusion; use Release and `DOTNET_TieredCompilation=0` for timing comparisons. Tests
assert behavior and applicable allocation contracts, never machine-specific elapsed-time limits.

Native trace tests own independent accumulator instances. They do not toggle the production
singleton or share a test mutex, so unrelated native tests can exercise instrumented code in
parallel without contaminating trace assertions. Production tracing retains one atomic enablement
load per span and reports only when enabled.

### Validation and retained-render workloads

`SemanticValidationCost`, `DeepRetainedTreeCost`, and `RootRenderWithCachedRowsCost` exercise full
managed validation, retained View rendering/acceptance, and root event binding with accepted row
artifacts. Windows x64 / .NET 10.0.11 Release measurements with tiering disabled compare the
implementation at `b6275b0` against indexed validation and root-event slots:

| Workload | Baseline µs/op | Indexed µs/op |
| --- | ---: | ---: |
| Validate 128 nested Dynamic wrappers | 16.56 | 4.23 |
| Validate 1,024 nested Dynamic wrappers | 724.66 | 34.50 |
| Validate 128 Dock panels | 1,400.98 | 7.43 |
| Validate 512 Dock panels | 173,425.69 | 33.81 |
| Render root, no cached batches | 0.45 | 0.30 |
| Render root, 64 cached batches | 0.64 | 0.30 |
| Render root, 512 cached batches | 1.86 | 0.30 |
| Reuse clean tree, depth 8 | 1.03 | 1.06 |
| Reuse clean tree, depth 64 | 5.21 | 5.58 |
| Invalidate leaf and render, depth 8 | 3.07 | 3.07 |
| Invalidate leaf and render, depth 64 | 26.68 | 28.00 |

Each result is the median of five measured batches after four warmup batches. Validation uses 16
iterations per batch, tree workloads 32, and cached-row workloads 128. All five measured batches
allocated zero managed bytes per operation. Input construction, initial subscription, row loading,
assertions, and reporting are outside the measured intervals. The validation probe builds an arena
once; the retained-tree probes include rendering and acceptance on every operation.

Each cached batch contains 48 element-only rows with shared callbacks and its own event lease.
The 512-batch case stresses registry scaling with 24,576 rows; it is not a claim about a typical
viewport's native cache size. Fixtures use the real managed root/range/acceptance paths with a native
notification stub. They do not measure native decoding, layout, painting, or end-to-end frame time.
The simple deep trees show no speedup; indexed graph validation adds some work to ordinary layouts.
Separate before/after runs had no CPU affinity or clock control, so small timing differences should
not be interpreted as portable regressions or guarantees. The large Dock improvement addresses
repeated diagnostic scans, not the cost of displaying 512 panels.

### Artifact ownership workloads

`RetirementWithUnrelatedArtifactsCost` measures acceptance removing 64 child Views while the root
retains unrelated 48-row batches. Windows x64 / .NET 10.0.11 Release with tiering disabled:

| Unrelated batches | Session scan µs/op (`705e8aa`) | Owner index µs/op |
| --- | ---: | ---: |
| 0 | 16.36 | 15.85 |
| 512 | 116.25 | 16.61 |
| 4,096 | 782.88 | 16.20 |

Each value is the median of five measured batches of eight operations after four warmup batches.
All measured batches allocated zero managed bytes per operation. Child creation, row loading,
root publication, assertions, and reporting are outside the interval; acceptance and teardown are
inside it. The large cache is a scaling stress case, not a typical viewport.

`RowArtifactChurnCost` loads, accepts, and releases one 48-row batch per operation with 0, 64, or
512 unrelated batches retained. It uses shared static callbacks and keeps assertions outside the
measured interval. `RowBatchAllocationPatterns` separates text, shared callbacks, captured
callbacks, and Signal reads through the native callback entry points. Windows x64 / .NET 10.0.11
Release with tiering disabled, five measured batches of 32 operations after four warmup batches:

| Row pattern | List of event slots B/batch (`d9274a8`) | Linked event slots B/batch |
| --- | ---: | ---: |
| Constant Text, 1 / 48 / 512 rows | 88 | 88 |
| Shared click handler, 1 / 48 / 512 rows | 160 | 88 |
| Shared click handler and one shared Signal, 48 rows | 288 | 216 |
| Fresh callback capturing each row index, 48 rows | 4,960 | 4,312 |

The remaining 88 bytes are the artifact's reactive consumer. One observed Signal adds 128 bytes
for dependency storage. Capturing each row index adds 88 bytes per row in application code; prefer
a shared callback with the native event payload when that expresses the same behavior. These
measurements include managed rendering, validation, acceptance, and release, with warmed arena,
registry, and dictionary capacity. They exclude native decoding, frame work, and assertions.
The allocation contracts cover both 1-row and 512-row batches; timing is exploratory.

The earlier 2,800-byte churn result included 1,152 bytes of fixture closure allocation and 1,488
bytes of assertion overhead. The comparable framework-only baseline is 160 bytes, not 2,800.

### Native decoding and cache workloads

Run the opt-in native probes separately from correctness tests:

```powershell
./eng/measure-native.ps1
```

The script runs ignored tests matching `native_workload_measurements` in Release on one test thread and
restores the working directory. Windows x64 measurements use five measured batches after four
warmups. Decode and load/release use 64 operations per batch; trimming measures one fully prepared
cache per batch. Each row has a keyed Button with width, height, and a shared click token.

| Workload | Baseline µs/op (`95c6d02`) | Current µs/op |
| --- | ---: | ---: |
| Decode 48 rows into reused storage | 3.81 | 3.70 |
| Decode 512 rows into reused storage | 41.39 | 38.09 |
| Load, accept, and release a new 48-row batch | 5.72 | 4.76 |
| Load, accept, and release a new 512-row batch | 45.77 | 41.56 |

Warm decode reuses the snapshot, string interner, and validation scratch against an unchanged
borrowed arena. Load/release reuses engine scratch while creating and destroying a native cached batch through the production
range/accept/release paths. Its Rust callback fixture constructs the arena and records callbacks
in preallocated vectors; it measures no managed runtime or cross-language transition. Neither
probe includes native materialization, text layout, painting, or complete frame time.

Idle trimming selects the four newest idle batches in one scan and releases the rest in another.
All batches requested by the current frame remain pinned. Selection uses four stack entries,
without another heap buffer. Compared with per-batch scratch/interner retention and eager
operation-string tables at `95c6d02`, shared scratch and leaner snapshots reduce both retained
buffers and destruction work:

| Idle 48-row batches | Baseline trim µs/op | Current trim µs/op | Baseline buffer bytes before trim | Current buffer bytes before trim |
| --- | ---: | ---: | ---: | ---: |
| 16 | 5.00 | 2.40 | 211,792 | 99,781 |
| 128 | 28.90 | 7.20 | 1,694,336 | 771,781 |
| 512 | 245.80 | 34.40 | 6,777,344 | 3,075,781 |

Trimming includes native batch destruction and release callbacks, with loading outside the timed
interval. Large cases stress viewport contraction; they are not typical idle cache sizes.
After trimming to four idle batches, these cases retain 27,781 buffer bytes, compared with
52,948 at baseline. The memory columns sum `capacity × element size` for snapshot and
validation/grouping vectors, counting shared engine scratch only once.
It excludes string allocations, hash-table storage, allocator overhead, ListState, GPUI elements,
and GPU memory, so it is a retained-buffer measure rather than total heap usage. These probes
use no data-valued operations. One 512-row snapshot plus scratch uses 103,045 buffer bytes,
compared with 9,781 for 48 rows. Dropping batch interner tables saves additional storage that is
outside this counter.

The tradeoff is engine-owned high-water scratch capacity: emptying the cache retains 3,781 bytes
after 48-row batches or 39,509 after 512-row batches in these probes, until the engine is dropped.
These numeric buffers retain no snapshot strings, event callbacks, or borrowed arena pointers.
Separate runs have no CPU affinity or clock control; small differences are inconclusive.

The validation probe uses the production validator with reused scratch, 16 operations per batch,
four warmup batches, and five measured batches. Resource cases declare Scroll nodes with distinct
UTF-8 keys and one owner. Dock cases declare one panel per tab group under a split; the depth case
adds 128 ancestor splits. These are validation timings, excluding decoding and rendering:

| Validation workload | Baseline µs/op (`203beef`) | Current µs/op |
| --- | ---: | ---: |
| 128 resource keys | 47.30 | 5.83 |
| 1,024 resource keys | 2,708.71 | 46.04 |
| 128 Dock panels | 109.88 | 13.42 |
| 512 Dock panels | 1,608.02 | 54.44 |
| 128 Dock panels, 128 additional ancestor splits | 1,287.89 | 15.28 |

Resource scratch capacity is unchanged at 6,837 and 54,325 bytes respectively. Dock scratch
capacity grows from 19,430 to 22,518 bytes for 128 panels and from 77,414 to 89,718 bytes for 512.
Ordinary row decode timings remain comparable in the same probe run (3.58 µs for 48 rows,
36.75 µs for 512); these small differences do not establish a row throughput improvement.

### Dispatcher callback validation cost

`DispatcherAdmissionCost` compares current Post admission with an experimental call to
the removed reflection-based callback inspection immediately before Post (retained only as a
test baseline). Windows x64, .NET 10.0.11, Release,
`DOTNET_TieredCompilation=0`: five warmup batches, seven measured batches of 512 posts, median
timing. Ingress draining is outside the measured region; the real warmed route/queue is used.
No CPU affinity or clock control is applied, so timing is exploratory.

| Callback | Current ns/post | With validation ns/post | Current B/post | With validation B/post |
| --- | ---: | ---: | ---: | ---: |
| Cached static | 34.6 | 44.5 | 32 | 32 |
| Fresh local capture | 66.4 | 151.6 | 120 | 216 |

Method-contract caching does not remove per-delegate reflection costs. The additional 96 bytes
on fresh captures and repeated lookup cost are why no runtime callback admission performs this
inspection. Events, WorkScope, menus, and Dispatcher rely on the synchronous API contract and
compile-time diagnostics. Runtime thread, phase, lifetime, and null checks remain.

### End-to-end targets

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

## Owned derivation and effects

A warm single-entry memo hit allocates zero managed bytes in the focused runtime test. Cache
construction and first calculation are separate costs. Effect handles and registries allocate only
when declared in construction; unchanged accepted inputs reuse the scope. Latest requests add their
linked cancellation source and replacement bookkeeping. The work table above measures independent
Start operations, not StartLatest. These managed measurements do not measure native frames or
cross-platform performance.
