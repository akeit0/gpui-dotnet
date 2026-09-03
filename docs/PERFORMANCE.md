# Performance contract

The performance goal is to avoid unnecessary crossings and allocations, not merely to make each
FFI call fast.

## Dirty and clean frames

A dirty managed tree normally requires one native-to-managed root render callback. The callback
writes a whole semantic tree into a flat arena. Rust validates and retains the decoded snapshot.

A clean GPUI repaint must not call managed `Render()`.

After arena warmup:

- `RenderContext` is stack-only;
- `Element<TTag>` is a small value;
- child APIs accept spans;
- event tokens are compact recyclable registry indices;
- framework child activation uses generated factories;
- native snapshot buffers and decode scratch are reused.

Do not replace this model with per-element or per-style P/Invoke calls.

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

Managed invalidation notifications are atomically coalesced to one pending native message. Resource
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
rows in that batch.

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
- arena high-water capacities and growth retries;
- Input UTF-8 allocation and decode-on-demand behavior;
- list/table reset, splice, refresh, and theme-change behavior.

Set `GPUI_DOTNET_TRACE=1` to print per-stage native timings and cumulative list-cache telemetry while
running the sample.
