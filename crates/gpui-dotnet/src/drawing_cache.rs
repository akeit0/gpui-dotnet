use std::{collections::HashMap, rc::Rc};

use gpui::{Bounds, Path, Pixels};

pub(crate) type PaintedPaths = Vec<(Path<Pixels>, u32)>;

struct Entry {
    bounds: Bounds<Pixels>,
    paths: Option<Rc<PaintedPaths>>,
    bytes: usize,
}

/// One bounds variant per Drawing in an immutable decoded snapshot. Oversized entries are
/// rendered without retention; a dense drawing cannot evict every other drawing on each frame.
#[derive(Default)]
pub(crate) struct DrawingCache {
    entries: HashMap<u32, Entry>,
    retained_bytes: usize,
}

impl DrawingCache {
    const MAX_BYTES: usize = 16 * 1024 * 1024;
    const MAX_ENTRIES: usize = 256;

    pub(crate) fn prepare(
        &mut self,
        node: u32,
        bounds: Bounds<Pixels>,
        build: impl FnOnce() -> PaintedPaths,
    ) -> Rc<PaintedPaths> {
        let reusable = self
            .entries
            .get(&node)
            .is_some_and(|entry| entry.bounds == bounds);
        if reusable && let Some(paths) = self.entries[&node].paths.as_ref() {
            return paths.clone();
        }
        if let Some(old) = self.entries.remove(&node) {
            self.retained_bytes -= old.bytes;
        }
        let paths = Rc::new(build());
        let bytes = paths.capacity() * size_of::<(Path<Pixels>, u32)>()
            + paths
                .iter()
                .map(|(path, _)| path.vertices.capacity() * size_of::<gpui::PathVertex<Pixels>>())
                .sum::<usize>();
        if !paths.is_empty() && self.entries.len() < Self::MAX_ENTRIES {
            // A new description/bounds may be used only once (animation, resize, or churn).
            // Retain geometry only after a second use, avoiding a scene clone on cold frames.
            let retain = reusable && bytes <= Self::MAX_BYTES.saturating_sub(self.retained_bytes);
            let bytes = if retain { bytes } else { 0 };
            self.retained_bytes += bytes;
            self.entries.insert(
                node,
                Entry {
                    bounds,
                    paths: retain.then(|| paths.clone()),
                    bytes,
                },
            );
        }
        paths
    }

    #[cfg(test)]
    pub(crate) fn retained_bytes(&self) -> usize {
        self.retained_bytes
    }

    #[cfg(test)]
    pub(crate) fn cached_paths(&self, node: u32) -> Option<Rc<PaintedPaths>> {
        self.entries
            .get(&node)
            .and_then(|entry| entry.paths.clone())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{point, px, size};

    fn bounds(x: f32, width: f32) -> Bounds<Pixels> {
        Bounds::new(point(px(x), px(0.)), size(px(width), px(100.)))
    }

    fn paths(color: u32) -> PaintedPaths {
        let mut path = Path::new(point(px(0.), px(0.)));
        path.line_to(point(px(20.), px(0.)));
        path.line_to(point(px(20.), px(20.)));
        vec![(path, color)]
    }

    #[test]
    fn geometry_reuse_requires_second_use_and_preserves_scene_copy_isolation() {
        let mut cache = DrawingCache::default();
        let first = cache.prepare(0, bounds(0., 100.), || paths(7));
        assert_eq!(Rc::strong_count(&first), 1);
        assert_eq!(cache.retained_bytes(), 0);
        let second = cache.prepare(0, bounds(0., 100.), || paths(7));
        let hit = cache.prepare(0, bounds(0., 100.), || panic!("cache hit must not build"));
        assert!(Rc::ptr_eq(&second, &hit));
        let mut scene_copy = hit[0].0.clone();
        scene_copy.vertices[0].xy_position.x = px(999.);
        assert_ne!(second[0].0.vertices[0].xy_position.x, px(999.));
        let bytes = cache.retained_bytes();
        assert!(bytes > 0);
        let other = cache.prepare(1, bounds(0., 100.), || paths(8));
        assert_eq!(other[0].1, 8);
        assert_eq!(cache.retained_bytes(), bytes);
        for next in [bounds(10., 100.), bounds(10., 200.)] {
            cache.prepare(0, next, || paths(9));
            assert_eq!(cache.retained_bytes(), 0);
            cache.prepare(0, next, || paths(9));
            assert_eq!(cache.retained_bytes(), bytes);
        }
    }

    #[test]
    fn geometry_retention_is_bounded_and_oversized_paths_still_render() {
        let mut cache = DrawingCache::default();
        for _ in 0..2 {
            let oversized = cache.prepare(0, bounds(0., 100.), || {
                let mut result = Vec::with_capacity(
                    DrawingCache::MAX_BYTES / size_of::<(Path<Pixels>, u32)>() + 1,
                );
                result.extend(paths(10));
                result
            });
            assert_eq!(oversized[0].1, 10);
            assert_eq!(Rc::strong_count(&oversized), 1);
            assert_eq!(cache.retained_bytes(), 0);
        }
        for node in 0..DrawingCache::MAX_ENTRIES as u32 {
            cache.prepare(node, bounds(0., 100.), || paths(node));
            cache.prepare(node, bounds(0., 100.), || paths(node));
        }
        assert_eq!(cache.entries.len(), DrawingCache::MAX_ENTRIES);
        let bytes = cache.retained_bytes();
        let fallback = cache.prepare(999, bounds(0., 100.), || paths(999));
        assert_eq!(fallback[0].1, 999);
        assert_eq!(Rc::strong_count(&fallback), 1);
        assert_eq!(cache.retained_bytes(), bytes);
        assert!(bytes <= DrawingCache::MAX_BYTES);
    }

    #[test]
    fn geometry_byte_budget_is_shared_across_drawings_and_reclaimed_on_bounds_change() {
        let mut cache = DrawingCache::default();
        let large = || {
            let mut result = Vec::with_capacity(
                DrawingCache::MAX_BYTES / 2 / size_of::<(Path<Pixels>, u32)>() + 1,
            );
            result.extend(paths(1));
            result
        };
        for node in 0..2 {
            cache.prepare(node, bounds(0., 100.), large);
            cache.prepare(node, bounds(0., 100.), large);
        }
        assert!(cache.cached_paths(0).is_some());
        assert!(cache.cached_paths(1).is_none());
        assert!(cache.retained_bytes() <= DrawingCache::MAX_BYTES);
        cache.prepare(0, bounds(1., 100.), || paths(1));
        assert_eq!(cache.retained_bytes(), 0);
        cache.prepare(1, bounds(0., 100.), large);
        assert!(cache.cached_paths(1).is_some());
    }
}
