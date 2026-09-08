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

/// Per-view image cache with live-set eviction.
///
/// GPUI's default `img` path stores decoded images in a global asset map that is never evicted,
/// so an image viewer that navigates between files accumulates every previously displayed image
/// (decoded BGRA frames plus GPU sprite-atlas textures) for the lifetime of the application.
/// Scoping decoded images to the owning view and dropping whatever the current snapshot no
/// longer declares keeps a viewer at roughly its visible working set instead.
pub(crate) struct ManagedImageCache {
    entries: HashMap<u64, ImageCacheItem>,
}

impl ManagedImageCache {
    pub(crate) fn new(cx: &mut App) -> Entity<Self> {
        let entity = cx.new(|_| Self {
            entries: HashMap::new(),
        });
        cx.observe_release(&entity, |cache, cx| {
            for (_, mut item) in std::mem::take(&mut cache.entries) {
                if let Some(Ok(image)) = item.get() {
                    cx.drop_image(image, None);
                }
            }
        })
        .detach();
        entity
    }

    pub(crate) fn load(
        &mut self,
        source: &Resource,
        window: &mut Window,
        cx: &mut App,
    ) -> Option<Result<Arc<RenderImage>, ImageCacheError>> {
        let key = hash(source);
        if let Some(item) = self.entries.get_mut(&key) {
            return item.get();
        }

        let asset = AssetLogger::<ImageAssetLoader>::load(source.clone(), cx);
        let task = cx.background_executor().spawn(asset).shared();
        self.entries
            .insert(key, ImageCacheItem::Loading(task.clone()));

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

    /// Drops every cached image the snapshot no longer declares, releasing both the decoded
    /// frames and their GPU sprite-atlas textures. Completed loads that are still referenced
    /// elsewhere stay alive through their own `Arc`s; only this cache's handle is released.
    pub(crate) fn retain_live(&mut self, live: &HashSet<u64>, window: &mut Window, cx: &mut App) {
        let stale: Vec<u64> = self
            .entries
            .keys()
            .copied()
            .filter(|key| !live.contains(key))
            .collect();
        for key in stale {
            if let Some(mut item) = self.entries.remove(&key)
                && let Some(Ok(image)) = item.get()
            {
                cx.drop_image(image, Some(window));
            }
        }
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
    fn managed_cache_evicts_images_outside_the_live_set(cx: &mut gpui::TestAppContext) {
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
        // the cache releases it and its GPU texture instead of holding it indefinitely.
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

        // Eviction is not poison: navigating back reloads the first image on demand.
        view.update(&mut window.cx, |probe, cx| {
            probe.sources = vec![first.clone()];
            cx.notify();
        });
        assert!(
            pump_until_loaded(window, &cache, std::slice::from_ref(&first)),
            "expected the first image to decode again after eviction",
        );
        window.update(|_, cx| {
            let cache = cache.read(cx);
            assert_eq!(cache.len(), 2);
            assert!(cache.contains(&first));
        });
    }

    #[gpui::test]
    fn managed_cache_releases_atlas_textures_on_evict(cx: &mut gpui::TestAppContext) {
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
            assert_eq!(cache.read(cx).len(), 0);
            assert!(
                !window.has_image_atlas_entry(&image),
                "evicted image texture must leave the sprite atlas",
            );
        });
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
