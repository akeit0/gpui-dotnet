use std::{cell::RefCell, rc::Rc};

use gpui::{
    AbsoluteLength, AnyElement, App, AvailableSpace, Bounds, ContentMask, DefiniteLength,
    DispatchPhase, Element, ElementId, FocusHandle, GlobalElementId, Hitbox, HitboxBehavior,
    InspectorElementId, InteractiveElement, IntoElement, LayoutId, Length, ParentElement, Pixels,
    Point, ScrollDelta, ScrollWheelEvent, Size, Style, Styled, Window, canvas, div, point, px,
    size,
};

use super::{cursor::CollectionCursor, engine::CollectionEngine};
use crate::{
    materializer::{CollectionItem, collection_focus_id, handle_collection_navigation},
    resources::{ResourceKey, ResourceStore},
    scrolling::{
        FINISH_THRESHOLD, ScrollbarMetrics, adjusted_bounds, adjusted_size, eased_axis,
        foundation_scrollbar, horizontal_wheel_delta, queue_scroll_delta,
    },
};

/// One item's width: the estimate until the item renders, then its measurement. A single
/// vector keeps widths and their measured flags from skewing across splice/refresh paths.
#[derive(Clone, Copy)]
struct HorizontalWidth {
    width: Pixels,
    measured: bool,
}

/// Per-item width state for a horizontal list. Widths start at the estimated item width and
/// converge to measured values as items render, mirroring the vertical height-hint contract.
pub(crate) struct HorizontalState {
    widths: Vec<HorizontalWidth>,
    /// `None` pins the viewport to the end edge (Bottom alignment). Any explicit scroll
    /// (wheel, drag, keys, controller) replaces it with an absolute offset.
    scroll_left: Option<Pixels>,
    pub(crate) viewport: Size<Pixels>,
    viewport_bounds: Bounds<Pixels>,
    hinted_viewport_height: Option<Pixels>,
    drag_start_width: Option<Pixels>,
    estimated_width: Pixels,
}

impl HorizontalState {
    pub(crate) fn new(count: usize, estimated_width: Pixels, end_anchored: bool) -> Self {
        let mut state = Self {
            widths: Vec::new(),
            scroll_left: Some(px(0.)),
            viewport: Size::default(),
            viewport_bounds: Bounds::default(),
            hinted_viewport_height: None,
            drag_start_width: None,
            estimated_width,
        };
        state.reset(count, estimated_width, end_anchored);
        state
    }

    pub(crate) fn reset(&mut self, count: usize, estimated_width: Pixels, end_anchored: bool) {
        self.widths.clear();
        self.widths.resize(
            count,
            HorizontalWidth {
                width: estimated_width,
                measured: false,
            },
        );
        self.scroll_left = if end_anchored { None } else { Some(px(0.)) };
        self.drag_start_width = None;
        self.estimated_width = estimated_width;
    }

    pub(crate) fn set_end_anchored(&mut self, end_anchored: bool) {
        self.scroll_left = if end_anchored {
            None
        } else {
            Some(self.effective_scroll())
        };
    }

    pub(crate) fn total_width(&self) -> Pixels {
        self.widths
            .iter()
            .map(|entry| entry.width)
            .reduce(|a, b| a + b)
            .unwrap_or(px(0.))
    }

    pub(crate) fn max_offset(&self) -> Pixels {
        (self.total_width() - self.viewport.width).max(px(0.))
    }

    pub(crate) fn effective_scroll(&self) -> Pixels {
        let maximum = self.max_offset();
        match self.scroll_left {
            Some(left) => left.max(px(0.)).min(maximum),
            None => maximum,
        }
    }

    pub(crate) fn set_scroll(&mut self, left: Pixels) {
        let maximum = self.max_offset();
        self.scroll_left = Some(left.max(px(0.)).min(maximum));
    }

    pub(crate) fn scroll_by(&mut self, delta: Pixels) {
        self.set_scroll(self.effective_scroll() + delta);
    }

    pub(crate) fn width_at(&self, index: usize) -> Pixels {
        self.widths
            .get(index)
            .map(|entry| entry.width)
            .unwrap_or(px(0.))
    }

    pub(crate) fn left_edge(&self, index: usize) -> Pixels {
        self.widths[..index.min(self.widths.len())]
            .iter()
            .map(|entry| entry.width)
            .reduce(|a, b| a + b)
            .unwrap_or(px(0.))
    }

    /// Moves the viewport the minimum distance that makes `index` fully visible.
    pub(crate) fn reveal(&mut self, index: usize) {
        if self.widths.is_empty() {
            return;
        }
        let index = index.min(self.widths.len() - 1);
        let left = self.left_edge(index);
        let right = left + self.widths[index].width;
        let scroll = self.effective_scroll();
        if left < scroll {
            self.set_scroll(left);
        } else if right > scroll + self.viewport.width {
            self.set_scroll(right - self.viewport.width);
        } else {
            // Already fully visible, but normalize a stale explicit offset.
            self.set_scroll(scroll);
        }
    }

    /// Pages the viewport by its width and returns the new first-visible item.
    pub(crate) fn page(&mut self, forward: bool) -> Option<usize> {
        if self.widths.is_empty() || self.viewport.width <= px(0.) {
            return None;
        }
        let target = self.effective_scroll()
            + if forward {
                self.viewport.width
            } else {
                -self.viewport.width
            };
        self.set_scroll(target);
        let scroll = self.effective_scroll();
        let mut left = px(0.);
        for (index, entry) in self.widths.iter().enumerate() {
            if left + entry.width > scroll {
                return Some(index);
            }
            left += entry.width;
        }
        Some(self.widths.len() - 1)
    }

    /// Visible item range covering the viewport plus overdraw on both sides.
    pub(crate) fn visible_range(&self, overdraw: Pixels) -> std::ops::Range<usize> {
        let count = self.widths.len();
        if count == 0 || self.viewport.width <= px(0.) {
            return 0..0;
        }
        let scroll = self.effective_scroll();
        let start_edge = (scroll - overdraw).max(px(0.));
        let end_edge = scroll + self.viewport.width + overdraw;
        let mut left = px(0.);
        let mut start = count;
        let mut end = count;
        for (index, entry) in self.widths.iter().enumerate() {
            let right = left + entry.width;
            if start == count && right > start_edge {
                start = index;
            }
            if left >= end_edge {
                end = index;
                break;
            }
            left = right;
        }
        if start == count {
            return 0..0;
        }
        start..end.min(count).max(start)
    }

    pub(crate) fn record_measurement(&mut self, index: usize, width: Pixels) {
        if let Some(entry) = self.widths.get_mut(index) {
            entry.width = width.max(px(0.));
            entry.measured = true;
        }
    }

    pub(crate) fn rehint_estimates(&mut self, estimated_width: Pixels) {
        self.estimated_width = estimated_width;
        for entry in self.widths.iter_mut().filter(|entry| !entry.measured) {
            entry.width = estimated_width;
        }
    }

    /// Marks every width for remeasurement while keeping current values as hints. Used when
    /// the viewport height changes, since item widths were measured against the old height.
    pub(crate) fn invalidate_measurements(&mut self) {
        for entry in self.widths.iter_mut() {
            entry.measured = false;
        }
    }

    pub(crate) fn splice(&mut self, start: usize, removed: usize, inserted: usize) {
        let start = start.min(self.widths.len());
        let removed = removed.min(self.widths.len().saturating_sub(start));
        self.widths.splice(
            start..start + removed,
            std::iter::repeat_n(
                HorizontalWidth {
                    width: self.estimated_width,
                    measured: false,
                },
                inserted,
            ),
        );
    }

    pub(crate) fn refresh(&mut self, start: usize, count: usize) {
        let end = (start + count).min(self.widths.len());
        if start < end {
            for entry in self.widths[start..end].iter_mut() {
                entry.measured = false;
            }
        }
    }

    pub(crate) fn scrollbar_drag_started(&mut self) {
        self.drag_start_width = Some(self.total_width());
    }

    pub(crate) fn scrollbar_drag_ended(&mut self) {
        self.drag_start_width = None;
    }

    fn frozen_total(&self) -> Pixels {
        self.drag_start_width.unwrap_or_else(|| self.total_width())
    }

    pub(crate) fn note_viewport_height(&mut self, height: Pixels) -> bool {
        if height <= px(0.) || self.hinted_viewport_height == Some(height) {
            return false;
        }
        self.hinted_viewport_height = Some(height);
        self.invalidate_measurements();
        true
    }
}

/// A virtualized horizontal list element. Items flow left-to-right; only the viewport plus
/// overdraw is materialized each frame. Item batches, the keyboard cursor, and selection /
/// activation events are shared with the vertical engine.
pub(crate) struct HorizontalList {
    engine: Rc<RefCell<CollectionEngine>>,
    resources: Rc<ResourceStore>,
    key: ResourceKey,
    cursor: Rc<CollectionCursor>,
    focus: FocusHandle,
    menu_token: u64,
}

impl HorizontalList {
    pub(crate) fn new(
        engine: Rc<RefCell<CollectionEngine>>,
        resources: Rc<ResourceStore>,
        key: ResourceKey,
        cursor: Rc<CollectionCursor>,
        focus: FocusHandle,
        menu_token: u64,
    ) -> Self {
        Self {
            engine,
            resources,
            key,
            cursor,
            focus,
            menu_token,
        }
    }
}

pub(crate) struct HorizontalPrepaint {
    hitbox: Hitbox,
    items: Vec<AnyElement>,
}

impl Element for HorizontalList {
    type RequestLayoutState = ();
    type PrepaintState = HorizontalPrepaint;

    fn id(&self) -> Option<ElementId> {
        None
    }

    fn source_location(&self) -> Option<&'static core::panic::Location<'static>> {
        None
    }

    fn request_layout(
        &mut self,
        _global_id: Option<&GlobalElementId>,
        _inspector_id: Option<&InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (LayoutId, ()) {
        // The host div owns sizing; the strip itself grows and shrinks freely in both axes.
        let zero = Length::Definite(DefiniteLength::Absolute(AbsoluteLength::Pixels(px(0.))));
        let mut style = Style::default();
        style.flex_grow = 1.0;
        style.min_size.width = zero;
        style.min_size.height = zero;
        (window.request_layout(style, None, cx), ())
    }

    fn prepaint(
        &mut self,
        _global_id: Option<&GlobalElementId>,
        _inspector_id: Option<&InspectorElementId>,
        bounds: Bounds<Pixels>,
        _: &mut (),
        window: &mut Window,
        cx: &mut App,
    ) -> HorizontalPrepaint {
        let hitbox = window.insert_hitbox(bounds, HitboxBehavior::Normal);

        let mut engine = self.engine.borrow_mut();
        let viewport = bounds.size;
        engine.horizontal.viewport = viewport;
        engine.horizontal.viewport_bounds = bounds;
        let overdraw = engine.horizontal_overdraw();
        let range = engine.horizontal.visible_range(overdraw);
        let scroll = engine.horizontal.effective_scroll();

        // Items lay out with unconstrained (max-content) width so wide content defines the
        // item width, and with the viewport height so vertical rhythm matches the strip.
        // Prefer fixed item widths; percentage widths resolve against max-content.
        let height_space = if viewport.height > px(0.) {
            AvailableSpace::Definite(viewport.height)
        } else {
            AvailableSpace::MinContent
        };
        let mut items = Vec::with_capacity(range.end.saturating_sub(range.start));
        let mut left = engine.horizontal.left_edge(range.start);
        for index in range {
            let element = engine.render_item(index, &self.resources, &self.key);
            let item = CollectionItem::new(element, self.cursor.clone(), self.focus.clone(), index)
                .with_item_events(engine.cached_item_events(index))
                .with_context_menu(
                    self.menu_token,
                    self.resources.item_menus.clone(),
                    self.engine.clone(),
                );
            let mut item = item.into_any_element();
            let measured =
                item.layout_as_root(size(AvailableSpace::MaxContent, height_space), window, cx);
            engine.horizontal.record_measurement(index, measured.width);
            let origin = point(bounds.origin.x + left - scroll, bounds.origin.y);
            item.prepaint_at(origin, window, cx);
            left += engine.horizontal.width_at(index);
            items.push(item);
        }

        HorizontalPrepaint { hitbox, items }
    }

    fn paint(
        &mut self,
        _: Option<&GlobalElementId>,
        _: Option<&InspectorElementId>,
        bounds: Bounds<Pixels>,
        _: &mut (),
        prepaint: &mut HorizontalPrepaint,
        window: &mut Window,
        cx: &mut App,
    ) {
        let engine = self.engine.clone();
        let hitbox_id = prepaint.hitbox.id;
        let mut accumulated = ScrollDelta::default();
        // Precise trackpad deltas apply directly here. Discrete wheel deltas are smoothed by
        // the sibling overlay, which stops propagation; this handler only sees what it leaves.
        window.on_mouse_event(move |event: &ScrollWheelEvent, phase, window, _cx| {
            if phase == DispatchPhase::Bubble && hitbox_id.should_handle_scroll(window) {
                accumulated = accumulated.coalesce(event.delta);
                if event.delta.precise() {
                    let dx = horizontal_wheel_delta(accumulated.pixel_delta(px(20.)));
                    engine.borrow_mut().horizontal.scroll_by(dx);
                    window.refresh();
                }
            }
        });

        window.with_content_mask(Some(ContentMask { bounds }), |window| {
            for item in &mut prepaint.items {
                item.paint(window, cx);
            }
        });
    }
}

impl IntoElement for HorizontalList {
    type Element = Self;

    fn into_element(self) -> Self::Element {
        self
    }
}

/// Sibling overlay for a horizontal list: wheel smoothing plus the horizontal foundation
/// scrollbar and the width-hint maintenance canvas.
pub(crate) fn horizontal_list_overlay(
    resource: Rc<RefCell<CollectionEngine>>,
    smooth: bool,
    show_scrollbar: bool,
    metrics: ScrollbarMetrics,
    id: ElementId,
) -> gpui::Div {
    let mut overlay = div().absolute().inset_0();

    let hint_resource = resource.clone();
    overlay = overlay.child(
        canvas(
            move |_, _, _| hint_resource.borrow_mut().maintain_width_hints(),
            |_, _, _, _| {},
        )
        .absolute()
        .inset_0(),
    );

    if smooth {
        let resource = resource.clone();
        overlay = overlay.on_scroll_wheel(move |event, window, cx| {
            if event.delta.precise() {
                return;
            }
            let dx = horizontal_wheel_delta(event.delta.pixel_delta(px(20.)));
            queue_scroll_delta(&resource.borrow().interaction.clone(), point(dx, px(0.)));
            start_horizontal_animation(resource.clone(), window);
            cx.stop_propagation();
        });
    }

    if show_scrollbar {
        let handle = HorizontalFoundationHandle::new(resource.clone(), metrics);
        overlay = overlay.child(foundation_scrollbar(&handle, 1, metrics, id));
    }

    overlay
}

fn start_horizontal_animation(resource: Rc<RefCell<CollectionEngine>>, window: &mut Window) {
    let interaction = resource.borrow().interaction.clone();
    if interaction.animating.replace(true) {
        return;
    }
    schedule_horizontal_frame(resource, window);
}

fn schedule_horizontal_frame(resource: Rc<RefCell<CollectionEngine>>, window: &mut Window) {
    window.on_next_frame(move |window, _| {
        let remaining = resource.borrow().interaction.remaining.get().x;
        let step = eased_axis(remaining);
        let before = resource.borrow().horizontal.effective_scroll();
        resource.borrow_mut().horizontal.scroll_by(step);
        let after = resource.borrow().horizontal.effective_scroll();
        let consumed = after - before;
        let next = if consumed == px(0.) && step != px(0.) {
            px(0.)
        } else {
            remaining - consumed
        };
        let interaction = resource.borrow().interaction.clone();
        interaction.remaining.set(point(next, px(0.)));
        window.refresh();

        if next.abs() <= FINISH_THRESHOLD {
            interaction.remaining.set(Point::default());
            interaction.animating.set(false);
        } else {
            schedule_horizontal_frame(resource.clone(), window);
        }
    });
    window.refresh();
}

#[derive(Clone)]
struct HorizontalFoundationHandle {
    resource: Rc<RefCell<CollectionEngine>>,
    metrics: ScrollbarMetrics,
}

impl HorizontalFoundationHandle {
    fn new(resource: Rc<RefCell<CollectionEngine>>, metrics: ScrollbarMetrics) -> Self {
        Self { resource, metrics }
    }
}

impl gpui_base::ScrollbarHandle for HorizontalFoundationHandle {
    fn viewport_bounds(&self) -> Bounds<Pixels> {
        adjusted_bounds(
            self.resource.borrow().horizontal.viewport_bounds,
            self.metrics,
        )
    }

    fn offset(&self) -> Point<Pixels> {
        point(
            -self.resource.borrow().horizontal.effective_scroll(),
            px(0.),
        )
    }

    fn set_offset(&self, offset: Point<Pixels>) {
        self.resource
            .borrow()
            .interaction
            .remaining
            .set(Point::default());
        self.resource.borrow_mut().horizontal.set_scroll(-offset.x);
    }

    fn content_size(&self) -> Size<Pixels> {
        let engine = self.resource.borrow();
        adjusted_size(
            size(
                engine.horizontal.frozen_total(),
                engine.horizontal.viewport.height,
            ),
            self.metrics,
        )
    }

    fn start_drag(&self) {
        self.resource
            .borrow()
            .interaction
            .remaining
            .set(Point::default());
        self.resource
            .borrow_mut()
            .horizontal
            .scrollbar_drag_started();
    }

    fn end_drag(&self) {
        self.resource.borrow_mut().horizontal.scrollbar_drag_ended();
    }
}

pub(crate) fn horizontal_scrollbar_id(key: &ResourceKey) -> ElementId {
    collection_focus_id("managed-hlist-scrollbar", key)
}

/// Horizontal keyboard navigation: Left/Right move one item, Home/End jump to the ends,
/// PageUp/PageDown move by the viewport width. Up/Down are left unconsumed so they can
/// leave the strip.
pub(crate) fn handle_horizontal_key_down(
    event: &gpui::KeyDownEvent,
    window: &mut Window,
    cx: &mut App,
    cursor: &CollectionCursor,
    resource: &Rc<RefCell<CollectionEngine>>,
    item_count: usize,
) {
    let interaction = resource.borrow().interaction.clone();
    handle_collection_navigation(
        event,
        window,
        cx,
        cursor,
        &interaction,
        item_count,
        |current, last| {
            let key = event.keystroke.key.as_str();
            match key {
                "left" => Some(current.saturating_sub(1)),
                "right" => Some((current + 1).min(last)),
                "home" => Some(0),
                "end" => Some(last),
                "pageup" | "pagedown" => {
                    let mut engine = resource.borrow_mut();
                    engine.horizontal.reveal(current);
                    engine.horizontal.page(key == "pagedown")
                }
                _ => None,
            }
            .inspect(|&next| {
                resource.borrow_mut().horizontal.reveal(next);
            })
        },
    )
}
