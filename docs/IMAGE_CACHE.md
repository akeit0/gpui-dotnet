# Image cache

`Image` nodes carry a filesystem path. Native code decodes them through GPUI's image loader
and keeps the results in a per-view `ManagedImageCache`
(`crates/gpui-dotnet/src/images.rs`). This document explains the policy, why it exists, and
how it compares to other frameworks.

## Problem

Plain GPUI `img` loads resolve through GPUI's global asset map, which has no eviction: every
distinct path ever displayed stays decoded for the application lifetime, in RAM (BGRA frames)
and in each window's GPU sprite atlas. An image viewer that navigates between files therefore
accumulates every previously displayed file. Reproduced by
`images::tests::global_asset_cache_retains_every_navigated_image` and measured by
`images::tests::eviction_comparison_reports_retained_bytes`:

```sh
cargo test --manifest-path crates/gpui-dotnet/Cargo.toml eviction_comparison -- --nocapture
```

Navigating six 512x512 files one at a time:

```text
off (global asset map): retained 6/6 images, 6291456 decoded bytes
on  (live + LRU spill): retained 3/6 images, 3145728 decoded bytes
```

## Policy

Three tiers, reconciled once per snapshot revision in `Render` (`retain_live_images`):

| Tier | Contents | Eviction |
| --- | --- | --- |
| Live (pinned) | Images declared by mounted snapshot nodes plus cached List/Table item batches | Never, while declared |
| Spill (LRU) | Recently-visible images, most-recently-used first | Oldest-first while over budget |
| Dropped | Everything else | Immediate |

Details:

- The snapshot is authoritative. The live set unions mounted nodes (`live_image_hashes`)
  with virtual-item batch snapshots (`CollectionRegistry::cached_image_hashes`), so item
  images are never wrongly evicted.
- Spilled images keep decoded bytes but release GPU textures eagerly: re-upload from RAM is
  cheap, decode from disk is not. Eviction from the spill drops the bytes too.
- The budget is measured in decoded bytes (`DEFAULT_MAX_SPILL_BYTES`, 100 MiB) with a count
  backstop (`DEFAULT_MAX_SPILL_ENTRIES`, 64). An image that alone exceeds the budget is
  dropped on the next reconcile, so one oversized file cannot pin itself or flush history.
- Recency covers live and spilled entries (`load` promotes on every hit), so back-navigation
  refreshes before the next trim.
- View teardown releases everything through `observe_release`.
- Revision gating keeps re-renders of an unchanged tree allocation-free: the same revision
  describes the same live set.

## Managed setting

`GpuiApplication.SetImageCacheBudget(ulong maxBytes, ulong maxEntries)` overrides the spill
tier process-wide; omitting it keeps the native defaults above. A zero byte budget disables
the spill tier (pure live-set, minimal memory); a zero entry count leaves the entry count
uncapped. The value is remembered before `Run` and forwarded when the native host attaches,
and all windows reconcile on their next render so a shrunken budget trims promptly. There is
intentionally no per-image retain option: anything the snapshot declares is pinned, so an app
that wants an image kept simply keeps its node mounted.

## Comparison with other frameworks

| Framework | Live (pinned) | Bounded spill | Second chance | Notes |
| --- | --- | --- | --- | --- |
| gpui-dotnet (this) | Snapshot-declared images | LRU, 100 MiB / 64 entries | Spill keeps decoded bytes; textures re-uploaded | Per-view scope |
| Flutter `ImageCache` | `live` (referenced by a widget) | `keepAlive` LRU, 1000 entries / 100 MiB, tunable | Oversized singles not cached | Docs: `painting/ImageCache-class.html`; breaking-change note on large images |
| Glide (Android) | Active resources (weak refs, displayed now) | Memory LRU (heap fraction, auto-trims on memory pressure) | Disk cache: transformed bytes, then original bytes | Cache keys include target size: decodes at display size |
| Coil (Android) | Weak refs for in-use | Strong memory LRU (~20% of app memory) | Disk cache; `trimMemory` support | Public `MemoryCache.maxSize`/`trimToSize` API |
| Upstream GPUI | None (global map never evicts) | `RetainAllImageCache` is unbounded with manual `clear()`; gallery example hand-rolls a count-based LRU | None | Verified in the pinned checkout (`crates/gpui/src/elements/image_cache.rs`, `crates/gpui/examples/image_gallery.rs`) |
| Zed image viewer | Current + in-flight image | None: previous image released immediately on change and on view close | None | Verified in the pinned checkout (`crates/image_viewer/src/image_viewer.rs`) |

Common ideas deliberately adopted: live pinning plus a byte-budgeted LRU spill (Flutter),
eager texture release with cheap re-upload (all GPU-backed loaders). Common ideas
deliberately deferred:

- **Decode at display size** (Glide's `inSampleSize`): usually the biggest memory win for
  large photos, but GPUI's asset loader does not know the layout size at decode time.
- **Neighbor prefetch** (±1 for viewers): no framework guesses; it needs a managed-to-native
  hint channel (new schema op or resource command).
- **OS memory-pressure hook** (Glide/Coil `trimMemory`): the byte budget already bounds the
  worst case; a pressure signal would only clear the spill earlier.
