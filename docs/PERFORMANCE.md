# Performance contract

The objective is not merely “FFI is fast”; it is to avoid crossings and allocations that do not need to exist.

## Root rendering

A dirty managed tree normally requires one Rust → managed render callback. Clean GPUI repaints reuse the decoded snapshot and do not call managed `Render()`.

After arena warmup:

- `RenderContext` is stack-only;
- `Element<TTag>` is a small value;
- child APIs accept spans;
- generated event/list tokens are values rather than delegate registry entries;
- framework View activation uses generated factories;
- native snapshot ops/children use flat ranges and reusable scratch buffers.

## Scroll

Wheel/trackpad movement updates `ScrollHandle` entirely in Rust. C# is involved only when:

- the owning managed View is dirty for another reason;
- an explicit `ScrollController` command is issued.

Do not add per-scroll-delta reverse callbacks to the default component.

Native resource commands use a lossless queue. Ordinary managed invalidation notifications are atomically coalesced to at most one pending queue entry, so repeated `Invalidate()` calls do not grow the native message queue while a frame is already pending.

## Virtual list crossing budget

GPUI's List item closure may request individual indices, but that must not become one FFI callback per row. A cache miss is aligned to the configured managed batch size (default 48):

```text
GPUI index requests: 4200, 4201, ...
Rust cache miss:     batch 4176..4223
Managed crossings:  1
```

At most four batches are retained per List resource. Clean scrolling within cached batches requires no managed callback. Scrolling into a missing batch requires one `list_render_range` call regardless of how many rows in that batch are subsequently materialized.

A successful managed root snapshot conservatively invalidates managed row snapshot batches because arbitrary View state can affect row output. `ListState` remains retained so scroll/viewport state is not lost.

## Row rendering

A `[GpuiListItem]` method is called once per requested managed item in the batch, but all rows are written to one arena and returned through one ABI call. It must remain synchronous and side-effect-free because arena growth can retry the entire batch.

Avoid creating managed View objects per datasource row. If a list contains 100,000 models, only visible/overdraw row snapshots should exist natively.

## Event payloads

Use the callback `ulong payload` for row/model identity rather than allocating a closure per item. The payload travels in `OpRecord.B` and through the native click callback unchanged.

## Measurement benchmark targets

Track at least:

- dirty root render time/allocations;
- clean repaint managed callback count (must be zero);
- Scroll wheel managed callback count (must be zero);
- List crossings while scrolling a fixed viewport;
- range batch size/cache hit rate;
- row arena high-water capacities;
- `ListState` reset/splice behavior after model mutation.
