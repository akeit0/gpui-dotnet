# Performance contract

The primary objective is not “FFI is fast”; it is **avoid unnecessary crossings and allocations altogether**.

## Crossing budget

A dirty view snapshot should normally require one Rust → managed render callback plus event/control transitions that actually occurred.

Do not create APIs that perform one P/Invoke for each style property, child, row, frame, pointer move, or text-edit operation.

Clean GPUI repaints must not call managed `Render()`.

## Managed hot path

After arena warmup:

- `RenderContext` is stack-only;
- `Element<TTag>` is a small value type;
- child APIs use `ReadOnlySpan<Element>`;
- UTF-8 literals copy directly into the arena;
- interpolated text uses the UTF-8 interpolated-string handler;
- generated event binding constructs a 64-bit value token, not a delegate/registry entry.

Run:

```powershell
dotnet run -c Release --project dotnet/samples/Gpui.Net.Bench
```

The key managed metric remains bytes allocated in the measured loop after warmup.

## Native dirty-render path

Decoded snapshots use:

```text
Vec<SnapshotNode>
Vec<OpRecord>     with per-node start/length ranges
Vec<u32> children with per-node start/length ranges
```

They do not create one `Vec` header/allocation per node for operations or children.

`SnapshotScratch` is retained by the native managed view and reuses parent tables, visitation buffers, grouping counts/cursors, and grouped child memory at their high-water capacities.

`RetainedStrings` swaps its current/previous sets and clears the recycled set, preserving HashSet capacity while retaining only consecutive generations of text.

## Child views

Unchanged child views keep managed fragment arenas. A child invalidation dirties its ancestor path; unchanged sibling fragment rendering remains cached.

The current root snapshot still copies cached fragment records into the Rust-owned destination arena. A future native fragment-reference representation can remove that copy for very large clean subtrees if profiling justifies the extra protocol complexity.

## Event path

ABI event dispatch avoids:

- process-wide callback ID allocation;
- per-view delegate dictionaries;
- per-snapshot callback owner registration;
- closure allocation for user handler registration.

GPUI still creates its native listener closure as part of element materialization, which is appropriate on the Rust side.

## Render purity is a performance contract

Arena growth retries `Render()`. Side effects in render would make growth behavior semantically observable and force the runtime toward expensive transactional/replay machinery. Keep render pure instead.

## Benchmark before specializing

Prioritize measurements in this order:

1. managed allocations after warmup;
2. number of managed/native crossings per interaction;
3. dirty snapshot native allocations;
4. snapshot decode/materialization time by node count;
5. large subtree fragment-copy cost;
6. virtualized data-source batch cost when that layer is added.
