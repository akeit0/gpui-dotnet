use std::{
    collections::{HashMap, HashSet},
    path::PathBuf,
    sync::Arc,
};

use futures::FutureExt as _;
use gpui::{
    App, AppContext as _, Asset as _, AssetLogger, Entity, ImageAssetLoader, ImageCache,
    ImageCacheError, ImageCacheItem, RenderImage, Resource, Window, hash,
};

use crate::{
    semantic::{NativeAdapter, component_metadata},
    snapshot::ValidatedSnapshot,
};

/// The GPUI resource backing an image node. Image data is a filesystem path; keeping this
/// constructor in one place guarantees the materialized `img` source and the live-set hashes
/// below always agree.
pub(crate) fn image_resource(path: &str) -> Resource {
    Resource::Path(PathBuf::from(path).into())
}

/// Hashes of every image the snapshot declares. The snapshot is authoritative: anything cached
/// that is not in this set is no longer displayed by any mounted or virtual item.
pub(crate) fn live_image_hashes(snapshot: &ValidatedSnapshot) -> HashSet<u64> {
    let mut live = HashSet::new();
    for node in &snapshot.nodes {
        let Some(metadata) = component_metadata(node.component) else {
            continue;
        };
        if metadata.adapter != NativeAdapter::Image || node.data.is_empty() {
            continue;
        }
        live.insert(hash(&image_resource(node.data.as_ref())));
    }
    live
}

/// Default spill budget: recently-viewed images kept decoded past their live range.
/// Mirrors Flutter's byte-budgeted keepAlive tier (100 MiB) with a count backstop.
pub(crate) const DEFAULT_MAX_SPILL_BYTES: usize = 100 << 20;
pub(crate) const DEFAULT_MAX_SPILL_ENTRIES: usize = 64;

/// Process-wide spill budget set from managed code (`GpuiApplication.SetImageCacheBudget`).
/// `None` keeps the built-in defaults above. Views pick the budget up lazily, so the setting
/// applies to current and future views without enumerating them.
static GLOBAL_BUDGET: std::sync::OnceLock<std::sync::Mutex<Option<(u64, u64)>>> =
    std::sync::OnceLock::new();

fn global_budget_store() -> &'static std::sync::Mutex<Option<(u64, u64)>> {
    GLOBAL_BUDGET.get_or_init(|| std::sync::Mutex::new(None))
}

pub(crate) fn set_global_budget(max_bytes: u64, max_entries: u64) {
    *global_budget_store().lock().expect("image budget lock") = Some((max_bytes, max_entries));
}

fn global_budget() -> Option<(u64, u64)> {
    global_budget_store()
        .lock()
        .expect("image budget lock")
        .clone()
}

#[cfg(test)]
fn reset_global_budget() {
    *global_budget_store().lock().expect("image budget lock") = None;
}

fn saturating_usize(value: u64) -> usize {
    usize::try_from(value).unwrap_or(usize::MAX)
}

/// Per-view image cache with live-set pinning and a byte-budgeted LRU spill.
///
/// GPUI's default `img` path stores decoded images in a global asset map that is never evicted,
/// so an image viewer that navigates between files accumulates every previously displayed image
/// (decoded BGRA frames plus GPU sprite-atlas textures) for the lifetime of the application.
/// This cache instead scopes decoded images to the owning view:
/// - images the current snapshot declares (mounted nodes plus cached virtual-item batches)
///   are pinned and never evicted while declared;
/// - recently-visible images spill into an LRU tier bounded by decoded bytes, so navigating
///   back within budget needs no disk reload or re-decode;
/// - spilled images keep decoded bytes but release their GPU textures eagerly (re-upload
///   from retained bytes is cheap); only eviction from the spill drops the bytes, and an
///   image that alone exceeds the budget is dropped on the next reconcile.
pub(crate) struct ManagedImageCache {
    entries: HashMap<u64, ImageCacheItem>,
    /// Every entry key exactly once, most-recently-used first. Recency covers both live and
    /// spilled entries so back-navigation promotes before the next trim.
    order: std::collections::VecDeque<u64>,
    max_spill_bytes: usize,
    max_spill_entries: usize,
}

impl ManagedImageCache {
    pub(crate) fn new(cx: &mut App) -> Entity<Self> {
        let entity = cx.new(|_| Self {
            entries: HashMap::new(),
            order: std::collections::VecDeque::new(),
            max_spill_bytes: DEFAULT_MAX_SPILL_BYTES,
            max_spill_entries: DEFAULT_MAX_SPILL_ENTRIES,
        });
        cx.observe_release(&entity, |cache, cx| {
            for (_, mut item) in std::mem::take(&mut cache.entries) {
                if let Some(Ok(image)) = item.get() {
                    cx.drop_image(image, None);
                }
            }
            cache.order.clear();
        })
        .detach();
        entity
    }

    #[cfg(test)]
    pub(crate) fn set_budget(&mut self, bytes: usize, entries: usize) {
        self.max_spill_bytes = bytes;
        self.max_spill_entries = entries;
    }

    /// Copies the managed-configured budget, if any. `max_bytes == 0` disables the spill
    /// tier (pure live-set); `max_entries == 0` leaves the entry count uncapped.
    fn apply_global_budget(&mut self) {
        if let Some((max_bytes, max_entries)) = global_budget() {
            self.max_spill_bytes = saturating_usize(max_bytes);
            self.max_spill_entries = saturating_usize(max_entries);
        }
    }

    fn touch(&mut self, key: u64) {
        if let Some(position) = self.order.iter().position(|candidate| *candidate == key) {
            self.order.remove(position);
        }
        self.order.push_front(key);
    }

    /// Decoded byte size of one entry, resolving an in-flight load where it has finished.
    /// Unfinished or failed loads cost no spill budget.
    fn entry_bytes(&mut self, key: u64) -> usize {
        let Some(item) = self.entries.get_mut(&key) else {
            return 0;
        };
        match item.get() {
            Some(Ok(image)) => (0..image.frame_count())
                .filter_map(|frame| image.as_bytes(frame))
                .map(|bytes| bytes.len())
                .sum(),
            _ => 0,
        }
    }

    fn spill_bytes(&mut self, live: &HashSet<u64>) -> usize {
        let keys: Vec<u64> = self
            .entries
            .keys()
            .copied()
            .filter(|key| !live.contains(key))
            .collect();
        keys.into_iter().map(|key| self.entry_bytes(key)).sum()
    }

    pub(crate) fn load(
        &mut self,
        source: &Resource,
        window: &mut Window,
        cx: &mut App,
    ) -> Option<Result<Arc<RenderImage>, ImageCacheError>> {
        self.apply_global_budget();
        let key = hash(source);
        if let Some(item) = self.entries.get_mut(&key) {
            let cached = item.get();
            self.touch(key);
            return cached;
        }

        let asset = AssetLogger::<ImageAssetLoader>::load(source.clone(), cx);
        let task = cx.background_executor().spawn(asset).shared();
        self.entries
            .insert(key, ImageCacheItem::Loading(task.clone()));
        self.touch(key);

        let view = window.current_view();
        window
            .spawn(cx, {
                async move |cx| {
                    _ = task.await;
                    cx.on_next_frame(move |_, cx| {
                        cx.notify(view);
                    });
                }
            })
            .detach();
        None
    }

    /// Reconciles the cache against the snapshot's live set. Live images stay pinned with
    /// their GPU textures. Recently-visible images spill into the LRU tier: they keep decoded
    /// bytes for instant back-navigation but release GPU textures eagerly, since re-upload
    /// from retained bytes is cheap and decode from disk is not. Eviction drops the oldest
    /// non-live entries first while the tier is over budget, so a single oversized image
    /// cannot pin itself: it ages out like everything else. Completed loads that are still
    /// referenced elsewhere stay alive through their own `Arc`s; only this cache's handle
    /// is released.
    pub(crate) fn retain_live(&mut self, live: &HashSet<u64>, window: &mut Window, cx: &mut App) {
        self.apply_global_budget();
        // Eagerly release GPU textures for everything the snapshot no longer declares.
        // Re-upload from retained bytes is cheap; decode from disk is not.
        let spilled: Vec<u64> = self
            .entries
            .keys()
            .copied()
            .filter(|key| !live.contains(key))
            .collect();
        for key in spilled {
            if let Some(item) = self.entries.get_mut(&key)
                && let Some(Ok(image)) = item.get()
            {
                cx.drop_image(image, Some(window));
            }
        }
        // Evict oldest-first while over budget. Live entries are pinned unconditionally: a
        // displayed image must never be dropped, or every frame would reload it.
        while (self.max_spill_entries > 0 && self.spill_count(live) > self.max_spill_entries)
            || self.spill_bytes(live) > self.max_spill_bytes
        {
            let Some(key) = self.oldest_spilled(live) else {
                break;
            };
            if let Some(mut item) = self.entries.remove(&key) {
                if let Some(position) = self.order.iter().position(|candidate| *candidate == key) {
                    self.order.remove(position);
                }
                if let Some(Ok(image)) = item.get() {
                    cx.drop_image(image, Some(window));
                }
            }
        }
    }

    fn spill_count(&self, live: &HashSet<u64>) -> usize {
        self.entries
            .keys()
            .filter(|key| !live.contains(key))
            .count()
    }

    /// Oldest non-live entry. Because eviction always takes the oldest first, a single
    /// image larger than the whole budget is dropped as soon as it ages out of (or never
    /// fits alongside) the rest of the spill, so one oversized file cannot pin useless
    /// history.
    fn oldest_spilled(&self, live: &HashSet<u64>) -> Option<u64> {
        self.order
            .iter()
            .rev()
            .find(|key| !live.contains(key))
            .copied()
    }

    /// Drops one path immediately, releasing decoded bytes and its GPU texture. Unknown
    /// paths are a silent no-op. Returns whether an entry was present.
    pub(crate) fn evict_path(
        &mut self,
        source: &Resource,
        window: &mut Window,
        cx: &mut App,
    ) -> bool {
        let key = hash(source);
        let Some(mut item) = self.entries.remove(&key) else {
            return false;
        };
        if let Some(position) = self.order.iter().position(|candidate| *candidate == key) {
            self.order.remove(position);
        }
        if let Some(Ok(image)) = item.get() {
            cx.drop_image(image, Some(window));
        }
        true
    }

    #[cfg(test)]
    pub(crate) fn len(&self) -> usize {
        self.entries.len()
    }

    #[cfg(test)]
    pub(crate) fn contains(&self, source: &Resource) -> bool {
        self.entries.contains_key(&hash(source))
    }

    /// Test-only peek at a cached entry without a window. Layout/paint drive the same
    /// transition through [`Self::load`]; this only observes it.
    #[cfg(test)]
    pub(crate) fn loaded(
        &mut self,
        source: &Resource,
    ) -> Option<Result<Arc<RenderImage>, ImageCacheError>> {
        self.entries
            .get_mut(&hash(source))
            .and_then(ImageCacheItem::get)
    }
}

impl ImageCache for ManagedImageCache {
    fn load(
        &mut self,
        resource: &Resource,
        window: &mut Window,
        cx: &mut App,
    ) -> Option<Result<Arc<RenderImage>, ImageCacheError>> {
        ManagedImageCache::load(self, resource, window, cx)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{
        Context, ImageSource, ImgResourceLoader, IntoElement, ParentElement, Render, Styled as _,
    };

    /// The process-wide budget is shared by every cache under test; serialize the view tests
    /// so one test's budget cannot leak into another's loads.
    static TEST_SERIAL: std::sync::Mutex<()> = std::sync::Mutex::new(());

    fn serial() -> std::sync::MutexGuard<'static, ()> {
        TEST_SERIAL.lock().expect("image test lock")
    }

    /// A minimal image viewer: shows one `img` per declared source, exactly like the viewer in
    /// issue #26, optionally through the scoped cache.
    struct ImageProbe {
        cache: Option<Entity<ManagedImageCache>>,
        sources: Vec<Resource>,
    }

    impl Render for ImageProbe {
        fn render(&mut self, _window: &mut Window, _cx: &mut Context<Self>) -> impl IntoElement {
            let mut row = gpui::div().flex().flex_row();
            for source in &self.sources {
                let mut image = gpui::img(ImageSource::Resource(source.clone()));
                if let Some(cache) = &self.cache {
                    image = image.image_cache(cache);
                }
                row = row.child(image);
            }
            row
        }
    }

    fn write_bmp(path: &std::path::Path, width: u32, height: u32, seed: u8) {
        let stride = ((width * 3 + 3) / 4) * 4;
        let pixels = stride * height;
        let file_size = 54 + pixels;
        let mut bytes = Vec::with_capacity(file_size as usize);
        bytes.extend_from_slice(b"BM");
        bytes.extend_from_slice(&file_size.to_le_bytes());
        bytes.extend_from_slice(&0u32.to_le_bytes());
        bytes.extend_from_slice(&54u32.to_le_bytes());
        bytes.extend_from_slice(&40u32.to_le_bytes());
        bytes.extend_from_slice(&(width as i32).to_le_bytes());
        bytes.extend_from_slice(&(height as i32).to_le_bytes());
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&24u16.to_le_bytes());
        bytes.extend_from_slice(&0u32.to_le_bytes());
        bytes.extend_from_slice(&pixels.to_le_bytes());
        bytes.extend_from_slice(&0i32.to_le_bytes());
        bytes.extend_from_slice(&0i32.to_le_bytes());
        bytes.extend_from_slice(&0u32.to_le_bytes());
        bytes.extend_from_slice(&0u32.to_le_bytes());
        for y in 0..height {
            for x in 0..width {
                bytes.push((x as u8).wrapping_add(seed));
                bytes.push((y as u8).wrapping_add(seed));
                bytes.push((x as u8 ^ y as u8).wrapping_add(seed));
            }
            for _ in (width * 3)..stride {
                bytes.push(0);
            }
        }
        std::fs::write(path, bytes).unwrap();
    }

    fn image_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("gpui-dotnet-{name}-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn redraw(window: &mut gpui::VisualTestContext) {
        window.update(|window, cx| {
            window.refresh();
            window.draw(cx).clear(cx);
        });
    }

    fn loaded_image(
        window: &mut gpui::VisualTestContext,
        cache: &Entity<ManagedImageCache>,
        source: &Resource,
    ) -> Option<Arc<RenderImage>> {
        window.update(|_, cx| {
            cache
                .update(cx, |cache, _| cache.loaded(source))
                .and_then(Result::ok)
        })
    }

    /// Pumps background decodes and view frames until every source is decoded. The final
    /// redraw paints with all images loaded, so their textures are in the sprite atlas.
    fn pump_until_loaded(
        window: &mut gpui::VisualTestContext,
        cache: &Entity<ManagedImageCache>,
        sources: &[Resource],
    ) -> bool {
        for _ in 0..500 {
            window.run_until_parked();
            redraw(window);
            if sources
                .iter()
                .all(|source| loaded_image(window, cache, source).is_some())
            {
                redraw(window);
                return true;
            }
        }
        false
    }

    fn global_loaded(window: &mut gpui::VisualTestContext, source: &Resource) -> bool {
        // `get_asset` only observes the global load without touching view state, so it is
        // legal outside the draw phases; the probe view's `img` elements drive the loads.
        window.update(|window, cx| {
            window
                .get_asset::<ImgResourceLoader>(source, cx)
                .is_some_and(|result| result.is_ok())
        })
    }

    #[gpui::test]
    fn global_asset_cache_retains_every_navigated_image(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        // Reproduction for https://github.com/akeit0/gpui-dotnet/issues/26: plain `img` loads
        // through GPUI's global asset map, which has no eviction. Navigating an image viewer
        // from file to file leaves every previously displayed image resident.
        let dir = image_dir("global-cache");
        let paths = ["a.bmp", "b.bmp", "c.bmp"];
        for (index, name) in paths.iter().enumerate() {
            write_bmp(&dir.join(name), 64, 64, index as u8 * 40);
        }
        let sources: Vec<Resource> = paths
            .iter()
            .map(|name| image_resource(dir.join(name).to_string_lossy().as_ref()))
            .collect();

        // The viewer shows every navigated image at once; all three decode and paint.
        let (_view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: None,
            sources: sources.clone(),
        });
        for _ in 0..500 {
            window.run_until_parked();
            redraw(window);
            if sources.iter().all(|source| global_loaded(window, source)) {
                break;
            }
        }

        // Every navigated image is decoded and still resident: nothing was ever evicted.
        for source in &sources {
            assert!(
                global_loaded(window, source),
                "expected navigated image to stay decoded in the global asset cache",
            );
        }
        window.cx.update(|cx| {
            for source in &sources {
                assert!(
                    cx.has_asset::<ImgResourceLoader>(source),
                    "global asset cache dropped an image without any eviction call",
                );
            }
        });
    }

    #[gpui::test]
    fn managed_cache_spills_images_outside_the_live_set(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        let dir = image_dir("managed-cache");
        write_bmp(&dir.join("a.bmp"), 64, 64, 11);
        write_bmp(&dir.join("b.bmp"), 64, 64, 77);
        let first = image_resource(dir.join("a.bmp").to_string_lossy().as_ref());
        let second = image_resource(dir.join("b.bmp").to_string_lossy().as_ref());

        let cache = cx.update(ManagedImageCache::new);
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![first.clone()],
        });

        // The viewer shows the first image until it finishes decoding.
        assert!(
            pump_until_loaded(window, &cache, std::slice::from_ref(&first)),
            "expected the first image to decode",
        );
        window.update(|_, cx| assert_eq!(cache.read(cx).len(), 1));

        // Navigating to the second image loads it alongside the first ...
        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![second.clone()];
            cx.notify();
        });
        assert!(
            pump_until_loaded(window, &cache, std::slice::from_ref(&second)),
            "expected the second image to decode",
        );
        window.update(|_, cx| assert_eq!(cache.read(cx).len(), 2));

        // ... until the new snapshot reconciles: the first image is no longer declared, so
        // it spills into the LRU tier instead of being dropped. Decoded bytes stay resident
        // within budget; only the GPU texture is released eagerly.
        let live: HashSet<u64> = [hash(&second)].into_iter().collect();
        window.update(|window, cx| {
            cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
        });
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 2);
            assert!(cache.contains(&first));
            assert!(cache.contains(&second));
        });

        // Navigating back is instant: the spilled bytes serve the image with no disk reload
        // or re-decode, so one frame suffices and no background pump is needed.
        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![first.clone()];
            cx.notify();
        });
        redraw(window);
        assert!(
            loaded_image(window, &cache, &first).is_some(),
            "expected back-navigation to hit spilled bytes without reloading",
        );
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 2);
            assert!(cache.contains(&first));
        });
    }

    #[gpui::test]
    fn spill_budget_evicts_oldest_first(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        let dir = image_dir("spill-budget");
        for (index, name) in ["a.bmp", "b.bmp", "c.bmp", "d.bmp"].iter().enumerate() {
            write_bmp(&dir.join(name), 64, 64, index as u8 * 11);
        }
        let sources: Vec<Resource> = ["a.bmp", "b.bmp", "c.bmp", "d.bmp"]
            .iter()
            .map(|name| image_resource(dir.join(name).to_string_lossy().as_ref()))
            .collect();

        let cache = cx.update(ManagedImageCache::new);
        // Each 64x64 image decodes to 16 KiB; the budget fits two spilled images.
        cx.update(|cx| cache.update(cx, |cache, _| cache.set_budget(40_000, 64)));
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![sources[0].clone()],
        });

        for source in &sources {
            view.update(&mut window.cx, |probe, cx| {
                probe.sources = vec![source.clone()];
                cx.notify();
            });
            assert!(
                pump_until_loaded(window, &cache, std::slice::from_ref(source)),
                "expected image to decode",
            );
            let live: HashSet<u64> = [hash(source)].into_iter().collect();
            window.update(|window, cx| {
                cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
            });
        }

        // Live d plus the two most recent spills fit; the oldest spill (a) is evicted.
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 3);
            assert!(!cache.contains(&sources[0]));
            assert!(cache.contains(&sources[1]));
            assert!(cache.contains(&sources[2]));
            assert!(cache.contains(&sources[3]));
        });
    }

    #[gpui::test]
    fn oversized_spill_entry_is_dropped(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        let dir = image_dir("spill-oversize");
        write_bmp(&dir.join("a.bmp"), 64, 64, 5);
        write_bmp(&dir.join("b.bmp"), 64, 64, 9);
        let first = image_resource(dir.join("a.bmp").to_string_lossy().as_ref());
        let second = image_resource(dir.join("b.bmp").to_string_lossy().as_ref());

        let cache = cx.update(ManagedImageCache::new);
        // A 16 KiB image can never fit a 1 KiB spill tier.
        cx.update(|cx| cache.update(cx, |cache, _| cache.set_budget(1_000, 64)));
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![first.clone()],
        });
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&first)
        ));

        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![second.clone()];
            cx.notify();
        });
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&second)
        ));
        let live: HashSet<u64> = [hash(&second)].into_iter().collect();
        window.update(|window, cx| {
            cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
        });
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 1);
            assert!(!cache.contains(&first));
            assert!(cache.contains(&second));
        });
    }

    #[gpui::test]
    fn managed_cache_releases_atlas_textures_on_spill(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        let dir = image_dir("managed-atlas");
        write_bmp(&dir.join("a.bmp"), 32, 32, 3);
        let source = image_resource(dir.join("a.bmp").to_string_lossy().as_ref());

        let cache = cx.update(ManagedImageCache::new);
        let (_view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![source.clone()],
        });

        assert!(
            pump_until_loaded(window, &cache, std::slice::from_ref(&source)),
            "expected the test image to decode",
        );
        // The probe view already painted the image, so its texture is in the sprite atlas.
        let image = loaded_image(window, &cache, &source).expect("image finished loading");
        window.update(|window, _| {
            assert!(
                window.has_image_atlas_entry(&image),
                "expected the painted image to own a sprite-atlas texture",
            );
        });
        window.update(|window, cx| {
            cache.update(cx, |cache, cx| {
                cache.retain_live(&HashSet::new(), window, cx)
            });
        });
        window.update(|window, cx| {
            // Spilled bytes stay cached within budget, but the texture is gone: back-navigation
            // re-uploads from RAM instead of re-decoding from disk.
            assert_eq!(cache.read(cx).len(), 1);
            assert!(
                !window.has_image_atlas_entry(&image),
                "spilled image texture must leave the sprite atlas",
            );
        });
    }

    #[gpui::test]
    fn evict_path_releases_bytes_texture_and_reloads(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        let dir = image_dir("evict-path");
        write_bmp(&dir.join("a.bmp"), 64, 64, 41);
        let source = image_resource(dir.join("a.bmp").to_string_lossy().as_ref());

        let cache = cx.update(ManagedImageCache::new);
        let (_view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![source.clone()],
        });
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&source)
        ));
        let image = loaded_image(window, &cache, &source).expect("image decoded");
        window.update(|window, _| {
            assert!(window.has_image_atlas_entry(&image));
        });

        // Unknown paths are a silent no-op.
        let missing = image_resource(dir.join("missing.bmp").to_string_lossy().as_ref());
        window.update(|window, cx| {
            assert!(
                !cache.update(cx, |cache, cx| cache.evict_path(&missing, window, cx)),
                "unknown path must evict nothing"
            );
        });
        window.update(|_, cx| assert_eq!(cache.read(cx).len(), 1));

        // Explicit eviction drops decoded bytes and the GPU texture ...
        window.update(|window, cx| {
            assert!(cache.update(cx, |cache, cx| cache.evict_path(&source, window, cx)));
        });
        window.update(|window, cx| {
            assert_eq!(cache.read(cx).len(), 0);
            assert!(!window.has_image_atlas_entry(&image));
        });

        // ... and the next paint reloads the file from disk.
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&source)
        ));
        window.update(|_, cx| assert_eq!(cache.read(cx).len(), 1));
    }

    #[gpui::test]
    fn global_budget_overrides_cache_tier(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        struct ResetBudget;
        impl Drop for ResetBudget {
            fn drop(&mut self) {
                reset_global_budget();
            }
        }
        let _reset = ResetBudget;

        let dir = image_dir("global-budget");
        write_bmp(&dir.join("a.bmp"), 64, 64, 21);
        write_bmp(&dir.join("b.bmp"), 64, 64, 33);
        let first = image_resource(dir.join("a.bmp").to_string_lossy().as_ref());
        let second = image_resource(dir.join("b.bmp").to_string_lossy().as_ref());

        let cache = cx.update(ManagedImageCache::new);
        cx.update(|cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.max_spill_bytes, DEFAULT_MAX_SPILL_BYTES);
            assert_eq!(cache.max_spill_entries, DEFAULT_MAX_SPILL_ENTRIES);
        });

        // A managed budget below one decoded image disables the spill tier in practice.
        set_global_budget(1_000, 64);
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![first.clone()],
        });
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&first)
        ));
        window.update(|_, cx| {
            assert_eq!(cache.read(cx).max_spill_bytes, 1_000);
        });

        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![second.clone()];
            cx.notify();
        });
        assert!(pump_until_loaded(
            window,
            &cache,
            std::slice::from_ref(&second)
        ));
        let live: HashSet<u64> = [hash(&second)].into_iter().collect();
        window.update(|window, cx| {
            cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
        });
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 1);
            assert!(!cache.contains(&first));
            assert!(cache.contains(&second));
        });
    }

    /// Practical off/on comparison for issue #26: navigate the same files through the
    /// global asset map (eviction off, the old behavior) and through the scoped cache with
    /// live-set reconciliation (eviction on, the fix), then report what stayed resident.
    /// Run with `-- --nocapture` to see the printed table.
    #[gpui::test]
    fn eviction_comparison_reports_retained_bytes(cx: &mut gpui::TestAppContext) {
        let _serial = serial();
        const COUNT: usize = 6;
        const SIZE: u32 = 512;
        let dir = image_dir("eviction-comparison");
        let mut sources = Vec::with_capacity(COUNT);
        for index in 0..COUNT {
            let name = format!("img{index}.bmp");
            write_bmp(&dir.join(&name), SIZE, SIZE, (index * 37) as u8);
            sources.push(image_resource(dir.join(&name).to_string_lossy().as_ref()));
        }

        fn decoded_bytes(image: &Arc<RenderImage>) -> usize {
            (0..image.frame_count())
                .filter_map(|frame| image.as_bytes(frame))
                .map(|bytes| bytes.len())
                .sum()
        }

        // Eviction off: plain img through the global asset map, one file at a time.
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: None,
            sources: vec![sources[0].clone()],
        });
        for source in &sources {
            view.update(&mut window.cx, |probe, cx| {
                probe.sources = vec![source.clone()];
                cx.notify();
            });
            for _ in 0..500 {
                window.run_until_parked();
                redraw(window);
                if global_loaded(window, source) {
                    break;
                }
            }
        }
        let mut off_count = 0;
        let mut off_bytes = 0;
        for source in &sources {
            let resident =
                window.update(|window, cx| window.get_asset::<ImgResourceLoader>(source, cx));
            if let Some(Ok(image)) = resident {
                off_count += 1;
                off_bytes += decoded_bytes(&image);
            }
        }

        // Eviction on: scoped cache with live-set pinning plus a 2 MiB LRU spill, so only
        // the live image and the most recent history survive. Each 512x512 image is 1 MiB.
        let cache = cx.update(ManagedImageCache::new);
        cx.update(|cx| cache.update(cx, |cache, _| cache.set_budget(2 << 20, 64)));
        let (view, window) = cx.add_window_view(|_, _| ImageProbe {
            cache: Some(cache.clone()),
            sources: vec![sources[0].clone()],
        });
        for source in &sources {
            view.update(&mut window.cx, |probe, cx| {
                probe.sources = vec![source.clone()];
                cx.notify();
            });
            for _ in 0..500 {
                window.run_until_parked();
                redraw(window);
                if loaded_image(window, &cache, source).is_some() {
                    break;
                }
            }
            let live: HashSet<u64> = [hash(source)].into_iter().collect();
            window.update(|window, cx| {
                cache.update(cx, |cache, cx| cache.retain_live(&live, window, cx));
            });
        }
        let mut on_count = 0;
        let mut on_bytes = 0;
        for source in &sources {
            if let Some(image) = loaded_image(window, &cache, source) {
                on_count += 1;
                on_bytes += decoded_bytes(&image);
            }
        }
        // Back-navigation within spill is instant: no pump, just one frame.
        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![sources[COUNT - 2].clone()];
            cx.notify();
        });
        redraw(window);
        assert!(
            loaded_image(window, &cache, &sources[COUNT - 2]).is_some(),
            "expected back-navigation to hit spill without reloading",
        );

        println!(
            "image cache eviction comparison ({COUNT} files navigated, {SIZE}x{SIZE}px, 2 MiB spill):\n  \
             off (global asset map): retained {off_count}/{COUNT} images, {off_bytes} decoded bytes\n  \
             on  (live + LRU spill): retained {on_count}/{COUNT} images, {on_bytes} decoded bytes",
        );
        assert_eq!(off_count, COUNT);
        // Live image plus two most recent spills fit the 2 MiB tier (1 MiB each).
        assert_eq!(on_count, 3);
    }

    #[test]
    fn image_resource_matches_filesystem_source_hash() {
        let via_helper = image_resource("C:/pictures/photo.png");
        let via_path: Resource = PathBuf::from("C:/pictures/photo.png").into();
        assert_eq!(hash(&via_helper), hash(&via_path));
    }

    #[test]
    fn live_image_hashes_collects_declared_image_paths() {
        use crate::{
            native_workloads::WorkloadArena,
            semantic::{COMPONENT_DIV, COMPONENT_IMAGE},
        };
        let mut arena = WorkloadArena::default();
        let path = "C:/pictures/photo.png";
        let root = arena.node(COMPONENT_DIV, None);
        arena.node_with_data(COMPONENT_IMAGE, Some(root), path);
        arena.node(COMPONENT_DIV, Some(root));
        let snapshot = arena.decode();

        let live = live_image_hashes(&snapshot);
        assert_eq!(live.len(), 1);
        assert!(live.contains(&hash(&image_resource(path))));
    }
}
