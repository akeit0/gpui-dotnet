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
- generated event binding constructs a 64-bit value token, not a delegate/registry entry;
- framework child creation uses generated static factories rather than reflection.

Run:

```powershell
dotnet run -c Release --project dotnet/samples/Gpui.Net.Bench
```

The key managed metric remains bytes allocated in the measured element-building loop after warmup.

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

## Managed child views

Each retained managed View keeps its own fragment arena. A local invalidation increments that View's fragment version and walks one parent pointer per ancestor. The previous multi-parent set traversal is intentionally gone because managed View ownership is a tree.

Framework child resolution adds dictionary lookups only for actual managed View boundaries, not for normal render Elements. Prefer Element/helper composition for purely visual decomposition.

Dynamic route replacement is staged. Native root-arena growth reuses already-created candidate Views and successfully-rendered child fragments instead of allocating/remounting them on each retry.

Changed `View<TProps>` props dirty only the child's fragment; unchanged props do not force the child `Render()` to execute.

The root snapshot still copies cached fragment records into the Rust-owned destination arena. A future native fragment-reference representation can remove that copy for very large clean subtrees if profiling justifies the protocol complexity.

## Keys

Use numeric keys for high-volume dynamic collections when convenient. String keys are appropriate for low-count semantic slots such as `"content"` or `"sidebar"`. `ChildKey` itself is a value type; numeric keys do not require boxing.

A framework View is created only when no retained/candidate View of the requested type matches the slot. It is not created once per render.

## Event path

ABI v2 event dispatch avoids:

- process-wide callback ID allocation;
- per-view delegate dictionaries;
- per-snapshot callback owner registration;
- closure allocation for user handler registration.

GPUI still creates its native listener closure as part of element materialization, which is appropriate on the Rust side.

## Render purity is a performance contract

Arena growth retries the root `Render()`. Side effects in render would make growth behavior semantically observable and force the runtime toward expensive transactional/replay machinery. Keep application render logic pure instead.

The child runtime contains staging specifically so framework-owned child creation remains retry-safe, but this is not permission for arbitrary user side effects inside `Render()`.

## Benchmark before specializing

Prioritize measurements in this order:

1. managed allocations after warmup;
2. number of managed/native crossings per interaction;
3. managed View fragment cache hit rate and route replacement cost;
4. dirty snapshot native allocations;
5. snapshot decode/materialization time by node count;
6. large subtree fragment-copy cost;
7. virtualized data-source batch cost when that layer is added.
