use std::rc::Rc;

use gpui::{
    Bounds, ElementId, InteractiveElement, ParentElement, Pixels, Point, ScrollWheelEvent, Size,
    Styled, Window, canvas, div, point, px, size,
};
use gpui_base::{
    Scrollbar, ScrollbarAxis, ScrollbarHandle as FoundationScrollbarHandle, ScrollbarMode,
};

use crate::resources::{ManagedListResource, ManagedScrollResource, ScrollInteraction};

const SMOOTHING: f32 = 0.24;
const FINISH_THRESHOLD: Pixels = px(0.5);
const BAR_MARGIN: Pixels = px(2.0);
/// Extra hit area beyond the visual bar so imprecise pointers still grab it.
const HIT_FORGIVENESS: Pixels = px(8.0);
pub(crate) const DEFAULT_SCROLLBAR_WIDTH: Pixels = px(8.0);
const MIN_THUMB: Pixels = px(48.0);

/// Scrollbar chrome dimensions resolved from the configured visual width. The hit area and
/// the reserved gutter both derive from it, so the configured width is the single source of
/// truth for bar geometry.
#[derive(Clone, Copy)]
pub(crate) struct ScrollbarMetrics {
    pub(crate) paint: Pixels,
    pub(crate) hit: Pixels,
    pub(crate) gutter: Pixels,
}

impl ScrollbarMetrics {
    pub(crate) fn new(width: Pixels, gutter_enabled: bool) -> Self {
        let hit = width + HIT_FORGIVENESS;
        let gutter = if gutter_enabled {
            hit + BAR_MARGIN * 2.0
        } else {
            px(0.)
        };
        Self {
            paint: width,
            hit,
            gutter,
        }
    }
}

pub(crate) fn scroll_overlay(
    resource: Rc<ManagedScrollResource>,
    axis: u32,
    smooth: bool,
    show_scrollbar: bool,
    metrics: ScrollbarMetrics,
    id: ElementId,
) -> gpui::Div {
    let mut overlay = div().absolute().inset_0();

    if smooth {
        let resource = resource.clone();
        overlay = overlay.on_scroll_wheel(move |event, window, cx| {
            // Pixel deltas already come from a high-resolution trackpad gesture. Let GPUI apply
            // them directly so the OS cadence is preserved and no artificial latency is added.
            if event.delta.precise() {
                return;
            }
            let delta = scroll_delta(event, window, axis);
            queue_scroll_delta(&resource.interaction, delta);
            start_scroll_animation(resource.clone(), window);
            cx.stop_propagation();
        });
    }

    if show_scrollbar {
        let handle = ScrollFoundationHandle::new(resource.clone(), metrics);
        overlay = overlay.child(foundation_scrollbar(&handle, axis, metrics, id));
    }

    overlay
}

pub(crate) fn list_overlay(
    resource: Rc<std::cell::RefCell<ManagedListResource>>,
    smooth: bool,
    show_scrollbar: bool,
    metrics: ScrollbarMetrics,
    id: ElementId,
) -> gpui::Div {
    let mut overlay = div().absolute().inset_0();

    let hint_resource = resource.clone();
    overlay = overlay.child(
        canvas(
            move |_, _, _| hint_resource.borrow_mut().maintain_height_hints(),
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
            let distance = -event.delta.pixel_delta(px(20.)).y;
            let interaction = resource.borrow().interaction.clone();
            queue_scroll_delta(&interaction, point(px(0.), distance));
            start_list_animation(resource.clone(), window);
            cx.stop_propagation();
        });
    }

    if show_scrollbar {
        let handle = ListFoundationHandle::new(resource.clone(), metrics);
        overlay = overlay.child(foundation_scrollbar(&handle, 0, metrics, id));
    }

    overlay
}

fn scroll_delta(event: &ScrollWheelEvent, window: &Window, axis: u32) -> Point<Pixels> {
    let delta = event.delta.pixel_delta(window.line_height());
    match axis {
        1 => point(if delta.x == px(0.) { delta.y } else { delta.x }, px(0.)),
        2 => delta,
        _ => point(px(0.), if delta.y == px(0.) { delta.x } else { delta.y }),
    }
}

fn queue_scroll_delta(interaction: &ScrollInteraction, delta: Point<Pixels>) {
    let current = interaction.remaining.get();
    interaction.remaining.set(point(
        coalesce_axis(current.x, delta.x),
        coalesce_axis(current.y, delta.y),
    ));
}

fn coalesce_axis(current: Pixels, delta: Pixels) -> Pixels {
    if delta == px(0.) {
        current
    } else if current == px(0.) || current.signum() == delta.signum() {
        current + delta
    } else {
        delta
    }
}

fn start_scroll_animation(resource: Rc<ManagedScrollResource>, window: &mut Window) {
    if resource.interaction.animating.replace(true) {
        return;
    }
    schedule_scroll_frame(resource, window);
}

fn schedule_scroll_frame(resource: Rc<ManagedScrollResource>, window: &mut Window) {
    window.on_next_frame(move |window, _| {
        let remaining = resource.interaction.remaining.get();
        let step = eased_step(remaining);
        let before = resource.handle.offset();
        apply_scroll_delta(&resource, step);
        let after = resource.handle.offset();
        let consumed = point(after.x - before.x, after.y - before.y);
        let next = remaining_after_step(remaining, step, consumed);
        resource.interaction.remaining.set(next);
        window.refresh();

        if is_finished(next) {
            resource.interaction.remaining.set(Point::default());
            resource.interaction.animating.set(false);
        } else {
            schedule_scroll_frame(resource.clone(), window);
        }
    });
    // Input callbacks run outside GPUI's render stack, so request_animation_frame() cannot be
    // used here (it looks up the currently rendering view). A full window refresh safely drives
    // the next-frame callback and still keeps the animation entirely native.
    window.refresh();
}

fn start_list_animation(
    resource: Rc<std::cell::RefCell<ManagedListResource>>,
    window: &mut Window,
) {
    let interaction = resource.borrow().interaction.clone();
    if interaction.animating.replace(true) {
        return;
    }
    schedule_list_frame(resource, window);
}

fn schedule_list_frame(resource: Rc<std::cell::RefCell<ManagedListResource>>, window: &mut Window) {
    window.on_next_frame(move |window, _| {
        let borrowed = resource.borrow();
        let interaction = borrowed.interaction.clone();
        let remaining = interaction.remaining.get();
        let step = eased_step(remaining).y;
        let before = borrowed.state.scroll_px_offset_for_scrollbar().y;
        borrowed.state.scroll_by(step);
        let after = borrowed.state.scroll_px_offset_for_scrollbar().y;
        drop(borrowed);

        let consumed = before - after;
        let next_y = if consumed == px(0.) && step != px(0.) {
            px(0.)
        } else {
            remaining.y - consumed
        };
        let next = point(px(0.), next_y);
        interaction.remaining.set(next);
        window.refresh();

        if is_finished(next) {
            interaction.remaining.set(Point::default());
            interaction.animating.set(false);
        } else {
            schedule_list_frame(resource.clone(), window);
        }
    });
    window.refresh();
}

fn eased_step(remaining: Point<Pixels>) -> Point<Pixels> {
    point(eased_axis(remaining.x), eased_axis(remaining.y))
}

fn eased_axis(remaining: Pixels) -> Pixels {
    if remaining.abs() <= FINISH_THRESHOLD {
        remaining
    } else {
        remaining * SMOOTHING
    }
}

fn remaining_after_step(
    remaining: Point<Pixels>,
    step: Point<Pixels>,
    consumed: Point<Pixels>,
) -> Point<Pixels> {
    point(
        if consumed.x == px(0.) && step.x != px(0.) {
            px(0.)
        } else {
            remaining.x - consumed.x
        },
        if consumed.y == px(0.) && step.y != px(0.) {
            px(0.)
        } else {
            remaining.y - consumed.y
        },
    )
}

fn is_finished(remaining: Point<Pixels>) -> bool {
    remaining.x.abs() <= FINISH_THRESHOLD && remaining.y.abs() <= FINISH_THRESHOLD
}

fn apply_scroll_delta(resource: &ManagedScrollResource, delta: Point<Pixels>) {
    let current = resource.handle.offset();
    let max = resource.handle.max_offset();
    resource.handle.set_offset(point(
        (current.x + delta.x).max(-max.x).min(px(0.)),
        (current.y + delta.y).max(-max.y).min(px(0.)),
    ));
}

#[derive(Clone)]
struct ScrollFoundationHandle {
    resource: Rc<ManagedScrollResource>,
    metrics: ScrollbarMetrics,
}

impl ScrollFoundationHandle {
    fn new(resource: Rc<ManagedScrollResource>, metrics: ScrollbarMetrics) -> Self {
        Self { resource, metrics }
    }
}

impl FoundationScrollbarHandle for ScrollFoundationHandle {
    fn viewport_bounds(&self) -> Bounds<Pixels> {
        adjusted_bounds(self.resource.handle.bounds(), self.metrics)
    }

    fn offset(&self) -> Point<Pixels> {
        self.resource.handle.offset()
    }

    fn set_offset(&self, offset: Point<Pixels>) {
        // Direct scrollbar interaction supersedes queued wheel easing so the thumb never fights
        // a pending animation after a track click or drag begins.
        self.resource.interaction.remaining.set(Point::default());
        let max = self.resource.handle.max_offset();
        self.resource.handle.set_offset(point(
            offset.x.max(-max.x).min(px(0.)),
            offset.y.max(-max.y).min(px(0.)),
        ));
    }

    fn content_size(&self) -> Size<Pixels> {
        let bounds = self.resource.handle.bounds();
        let max = self.resource.handle.max_offset();
        adjusted_size(bounds.size + max.into(), self.metrics)
    }

    fn start_drag(&self) {
        self.resource.interaction.remaining.set(Point::default());
    }
}

#[derive(Clone)]
struct ListFoundationHandle {
    resource: Rc<std::cell::RefCell<ManagedListResource>>,
    metrics: ScrollbarMetrics,
}

impl ListFoundationHandle {
    fn new(
        resource: Rc<std::cell::RefCell<ManagedListResource>>,
        metrics: ScrollbarMetrics,
    ) -> Self {
        Self { resource, metrics }
    }
}

impl FoundationScrollbarHandle for ListFoundationHandle {
    fn viewport_bounds(&self) -> Bounds<Pixels> {
        adjusted_bounds(self.resource.borrow().state.viewport_bounds(), self.metrics)
    }

    fn offset(&self) -> Point<Pixels> {
        self.resource
            .borrow()
            .state
            .scroll_px_offset_for_scrollbar()
    }

    fn set_offset(&self, offset: Point<Pixels>) {
        let resource = self.resource.borrow();
        resource.interaction.remaining.set(Point::default());
        resource.state.set_offset_from_scrollbar(offset);
    }

    fn content_size(&self) -> Size<Pixels> {
        let resource = self.resource.borrow();
        let viewport = adjusted_bounds(resource.state.viewport_bounds(), self.metrics);
        viewport.size + resource.state.max_offset_for_scrollbar().into()
    }

    fn start_drag(&self) {
        let resource = self.resource.borrow();
        resource.interaction.remaining.set(Point::default());
        resource.state.scrollbar_drag_started();
    }

    fn end_drag(&self) {
        self.resource.borrow().state.scrollbar_drag_ended();
    }
}

fn adjusted_bounds(bounds: Bounds<Pixels>, metrics: ScrollbarMetrics) -> Bounds<Pixels> {
    Bounds::new(
        bounds.origin + point(BAR_MARGIN, BAR_MARGIN),
        adjusted_size(bounds.size, metrics),
    )
}

fn adjusted_size(value: Size<Pixels>, metrics: ScrollbarMetrics) -> Size<Pixels> {
    size(
        (value.width - BAR_MARGIN * 2.0 + metrics.gutter).max(px(0.)),
        (value.height - BAR_MARGIN * 2.0).max(px(0.)),
    )
}

fn foundation_scrollbar<H>(
    handle: &H,
    axis: u32,
    metrics: ScrollbarMetrics,
    id: ElementId,
) -> Scrollbar
where
    H: FoundationScrollbarHandle + Clone,
{
    let axis = match axis {
        1 => ScrollbarAxis::Horizontal,
        2 => ScrollbarAxis::Both,
        _ => ScrollbarAxis::Vertical,
    };
    let inset = (metrics.hit - metrics.paint) / 2.0;
    let min_length = MIN_THUMB + inset * 2.0;

    Scrollbar::new(handle)
        .id(id)
        .axis(axis)
        .mode(ScrollbarMode::Always)
        .styles(|styles| {
            styles
                .track(|style| style.width(metrics.hit))
                .track_hover(|style| style.width(metrics.hit))
                .track_active(|style| style.width(metrics.hit))
                .thumb(|style| {
                    style
                        .width(metrics.paint)
                        .inset(inset)
                        .radius(metrics.paint / 2.0)
                        .min_length(min_length)
                })
                .thumb_hover(|style| {
                    style
                        .width(metrics.paint)
                        .inset(inset)
                        .radius(metrics.paint / 2.0)
                        .min_length(min_length)
                })
                .thumb_active(|style| {
                    style
                        .width(metrics.paint)
                        .inset(inset)
                        .radius(metrics.paint / 2.0)
                        .min_length(min_length)
                })
        })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn coalescing_accumulates_in_one_direction_and_reverses_immediately() {
        assert_eq!(coalesce_axis(px(-20.), px(-10.)), px(-30.));
        assert_eq!(coalesce_axis(px(-20.), px(10.)), px(10.));
        assert_eq!(coalesce_axis(px(12.), px(0.)), px(12.));
    }

    #[test]
    fn adjusted_viewport_preserves_scroll_range_and_reserves_the_gutter() {
        let viewport = Bounds::new(point(px(10.), px(20.)), size(px(200.), px(100.)));
        let content = size(px(500.), px(1000.));
        let metrics = ScrollbarMetrics::new(px(8.), true);
        let adjusted_viewport = adjusted_bounds(viewport, metrics);
        let adjusted_content = adjusted_size(content, metrics);

        assert_eq!(adjusted_viewport.origin, point(px(12.), px(22.)));
        assert_eq!(adjusted_viewport.size, size(px(216.), px(96.)));
        assert_eq!(adjusted_content, size(px(516.), px(996.)));
        assert_eq!(
            adjusted_content.width - adjusted_viewport.size.width,
            px(300.)
        );
        assert_eq!(
            adjusted_content.height - adjusted_viewport.size.height,
            px(900.)
        );
    }

    #[test]
    fn configured_width_scales_paint_hit_and_gutter() {
        let metrics = ScrollbarMetrics::new(px(12.), true);
        assert_eq!(metrics.paint, px(12.));
        assert_eq!(metrics.hit, px(20.));
        assert_eq!(metrics.gutter, px(24.));
    }

    #[test]
    fn blocked_axis_drops_only_its_unconsumed_motion() {
        let remaining = point(px(-40.), px(-60.));
        let step = point(px(-10.), px(-15.));
        let consumed = point(px(0.), px(-15.));

        assert_eq!(
            remaining_after_step(remaining, step, consumed),
            point(px(0.), px(-45.))
        );
    }
}
