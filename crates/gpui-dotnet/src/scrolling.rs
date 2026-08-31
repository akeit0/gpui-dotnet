use std::rc::Rc;

use gpui::{
    Bounds, InteractiveElement, ListOffset, MouseButton, MouseDownEvent, MouseMoveEvent,
    MouseUpEvent, ParentElement, Pixels, Point, ScrollWheelEvent, Styled, Window, canvas, div,
    point, px, quad, rgba, size, transparent_black,
};

use crate::resources::{
    ManagedListResource, ManagedScrollResource, ScrollInteraction, ScrollbarDrag,
};

const SMOOTHING: f32 = 0.24;
const FINISH_THRESHOLD: Pixels = px(0.5);
const BAR_MARGIN: Pixels = px(2.0);
const BAR_HIT_WIDTH: Pixels = px(16.0);
const BAR_PAINT_WIDTH: Pixels = px(8.0);
const MIN_THUMB: Pixels = px(48.0);

#[derive(Clone, Copy)]
struct AxisGeometry {
    track: Bounds<Pixels>,
    thumb: Bounds<Pixels>,
}

#[derive(Clone, Copy, Default)]
struct ScrollbarGeometry {
    vertical: Option<AxisGeometry>,
    horizontal: Option<AxisGeometry>,
}

pub(crate) fn scroll_overlay(
    resource: Rc<ManagedScrollResource>,
    axis: u32,
    smooth: bool,
    show_scrollbar: bool,
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
        let paint_resource = resource.clone();
        overlay = overlay.child(
            canvas(
                move |_, _, _| scroll_geometry(&paint_resource, axis),
                move |_, geometry, window, _| paint_scrollbar(geometry, window),
            )
            .absolute()
            .inset_0(),
        );

        let down_resource = resource.clone();
        overlay = overlay.on_mouse_down(MouseButton::Left, move |event, window, cx| {
            let geometry = scroll_geometry(&down_resource, axis);
            if begin_drag(event, geometry, &down_resource.interaction) {
                update_scroll_drag(&down_resource, event.position, geometry);
                window.refresh();
                cx.stop_propagation();
            }
        });

        let move_resource = resource.clone();
        overlay = overlay.on_mouse_move(move |event: &MouseMoveEvent, window, cx| {
            if move_resource.interaction.drag.get().is_none() {
                return;
            }
            if !event.dragging() {
                move_resource.interaction.drag.set(None);
                return;
            }
            let geometry = scroll_geometry(&move_resource, axis);
            update_scroll_drag(&move_resource, event.position, geometry);
            window.refresh();
            cx.stop_propagation();
        });

        let up_interaction = resource.interaction.clone();
        overlay = overlay.on_mouse_up(MouseButton::Left, move |_: &MouseUpEvent, _, cx| {
            if up_interaction.drag.replace(None).is_some() {
                cx.stop_propagation();
            }
        });
    }

    overlay
}

pub(crate) fn list_overlay(
    resource: Rc<std::cell::RefCell<ManagedListResource>>,
    estimated_item_height: Pixels,
    smooth: bool,
    show_scrollbar: bool,
) -> gpui::Div {
    let mut overlay = div().absolute().inset_0();

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
        let paint_resource = resource.clone();
        overlay = overlay.child(
            canvas(
                move |_, _, _| list_geometry(&paint_resource.borrow(), estimated_item_height),
                move |_, geometry, window, _| paint_scrollbar(geometry, window),
            )
            .absolute()
            .inset_0(),
        );

        let down_resource = resource.clone();
        overlay = overlay.on_mouse_down(MouseButton::Left, move |event, window, cx| {
            let borrowed = down_resource.borrow();
            let geometry = list_geometry(&borrowed, estimated_item_height);
            let interaction = borrowed.interaction.clone();
            drop(borrowed);
            if begin_drag(event, geometry, &interaction) {
                update_list_drag(
                    &down_resource.borrow(),
                    event.position,
                    geometry,
                    estimated_item_height,
                );
                window.refresh();
                cx.stop_propagation();
            }
        });

        let move_resource = resource.clone();
        overlay = overlay.on_mouse_move(move |event: &MouseMoveEvent, window, cx| {
            let interaction = move_resource.borrow().interaction.clone();
            if interaction.drag.get().is_none() {
                return;
            }
            if !event.dragging() {
                interaction.drag.set(None);
                return;
            }
            let borrowed = move_resource.borrow();
            let geometry = list_geometry(&borrowed, estimated_item_height);
            update_list_drag(&borrowed, event.position, geometry, estimated_item_height);
            drop(borrowed);
            window.refresh();
            cx.stop_propagation();
        });

        let up_interaction = resource.borrow().interaction.clone();
        overlay = overlay.on_mouse_up(MouseButton::Left, move |_: &MouseUpEvent, _, cx| {
            if up_interaction.drag.replace(None).is_some() {
                cx.stop_propagation();
            }
        });
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
        (current.x + delta.x).max(-max.width).min(px(0.)),
        (current.y + delta.y).max(-max.height).min(px(0.)),
    ));
}

fn scroll_geometry(resource: &ManagedScrollResource, axis: u32) -> ScrollbarGeometry {
    let bounds = resource.handle.bounds();
    let offset = resource.handle.offset();
    let max = resource.handle.max_offset();
    ScrollbarGeometry {
        vertical: (axis != 1)
            .then(|| vertical_geometry(bounds, offset.y, max.height))
            .flatten(),
        horizontal: (axis != 0)
            .then(|| horizontal_geometry(bounds, offset.x, max.width))
            .flatten(),
    }
}

fn list_geometry(
    resource: &ManagedListResource,
    estimated_item_height: Pixels,
) -> ScrollbarGeometry {
    let bounds = resource.state.viewport_bounds();
    let visible_items = if estimated_item_height > px(0.) {
        bounds.size.height / estimated_item_height
    } else {
        0.0
    };
    let scrollable_items = (resource.item_count as f32 - visible_items).max(0.0);
    let logical = resource.state.logical_scroll_top();
    let item_position =
        logical.item_ix as f32 + (logical.offset_in_item / estimated_item_height).max(0.0);
    let max = estimated_item_height * scrollable_items;
    let offset = -(estimated_item_height * item_position.min(scrollable_items));
    ScrollbarGeometry {
        vertical: vertical_geometry(bounds, offset, max),
        horizontal: None,
    }
}

fn vertical_geometry(
    viewport: Bounds<Pixels>,
    offset: Pixels,
    max: Pixels,
) -> Option<AxisGeometry> {
    if max <= px(0.) || viewport.size.height <= BAR_MARGIN * 2.0 {
        return None;
    }
    let track = Bounds::new(
        point(
            viewport.right() - BAR_HIT_WIDTH - BAR_MARGIN,
            viewport.top() + BAR_MARGIN,
        ),
        size(BAR_HIT_WIDTH, viewport.size.height - BAR_MARGIN * 2.0),
    );
    let thumb_extent = (track.size.height * (viewport.size.height / (viewport.size.height + max)))
        .max(MIN_THUMB)
        .min(track.size.height);
    let progress = (-offset / max).clamp(0.0, 1.0);
    let thumb = Bounds::new(
        point(
            track.left(),
            track.top() + (track.size.height - thumb_extent) * progress,
        ),
        size(BAR_HIT_WIDTH, thumb_extent),
    );
    Some(AxisGeometry { track, thumb })
}

fn horizontal_geometry(
    viewport: Bounds<Pixels>,
    offset: Pixels,
    max: Pixels,
) -> Option<AxisGeometry> {
    if max <= px(0.) || viewport.size.width <= BAR_MARGIN * 2.0 {
        return None;
    }
    let track = Bounds::new(
        point(
            viewport.left() + BAR_MARGIN,
            viewport.bottom() - BAR_HIT_WIDTH - BAR_MARGIN,
        ),
        size(viewport.size.width - BAR_MARGIN * 2.0, BAR_HIT_WIDTH),
    );
    let thumb_extent = (track.size.width * (viewport.size.width / (viewport.size.width + max)))
        .max(MIN_THUMB)
        .min(track.size.width);
    let progress = (-offset / max).clamp(0.0, 1.0);
    let thumb = Bounds::new(
        point(
            track.left() + (track.size.width - thumb_extent) * progress,
            track.top(),
        ),
        size(thumb_extent, BAR_HIT_WIDTH),
    );
    Some(AxisGeometry { track, thumb })
}

fn paint_scrollbar(geometry: ScrollbarGeometry, window: &mut Window) {
    if let Some(vertical) = geometry.vertical {
        paint_axis(vertical, true, window);
    }
    if let Some(horizontal) = geometry.horizontal {
        paint_axis(horizontal, false, window);
    }
}

fn paint_axis(axis: AxisGeometry, vertical: bool, window: &mut Window) {
    let visual_track = visual_bounds(axis.track, vertical);
    let visual_thumb = visual_bounds(axis.thumb, vertical);
    window.paint_quad(quad(
        visual_track,
        BAR_PAINT_WIDTH / 2.0,
        rgba(0x0f172a18),
        px(0.),
        transparent_black(),
        Default::default(),
    ));
    window.paint_quad(quad(
        visual_thumb,
        BAR_PAINT_WIDTH / 2.0,
        rgba(0x64748bb8),
        px(0.),
        transparent_black(),
        Default::default(),
    ));
}

fn visual_bounds(bounds: Bounds<Pixels>, vertical: bool) -> Bounds<Pixels> {
    let inset = (BAR_HIT_WIDTH - BAR_PAINT_WIDTH) / 2.0;
    if vertical {
        Bounds::new(
            point(bounds.left() + inset, bounds.top()),
            size(BAR_PAINT_WIDTH, bounds.size.height),
        )
    } else {
        Bounds::new(
            point(bounds.left(), bounds.top() + inset),
            size(bounds.size.width, BAR_PAINT_WIDTH),
        )
    }
}

fn begin_drag(
    event: &MouseDownEvent,
    geometry: ScrollbarGeometry,
    interaction: &ScrollInteraction,
) -> bool {
    if let Some(axis) = geometry.vertical
        && axis.track.contains(&event.position)
    {
        let pointer_offset = if axis.thumb.contains(&event.position) {
            event.position.y - axis.thumb.top()
        } else {
            axis.thumb.size.height / 2.0
        };
        interaction
            .drag
            .set(Some(ScrollbarDrag::Vertical { pointer_offset }));
        interaction.remaining.set(Point::default());
        return true;
    }
    if let Some(axis) = geometry.horizontal
        && axis.track.contains(&event.position)
    {
        let pointer_offset = if axis.thumb.contains(&event.position) {
            event.position.x - axis.thumb.left()
        } else {
            axis.thumb.size.width / 2.0
        };
        interaction
            .drag
            .set(Some(ScrollbarDrag::Horizontal { pointer_offset }));
        interaction.remaining.set(Point::default());
        return true;
    }
    false
}

fn update_scroll_drag(
    resource: &ManagedScrollResource,
    pointer: Point<Pixels>,
    geometry: ScrollbarGeometry,
) {
    let max = resource.handle.max_offset();
    let current = resource.handle.offset();
    match resource.interaction.drag.get() {
        Some(ScrollbarDrag::Vertical { pointer_offset }) => {
            let Some(axis) = geometry.vertical else {
                return;
            };
            let travel = axis.track.size.height - axis.thumb.size.height;
            let progress = if travel <= px(0.) {
                0.0
            } else {
                ((pointer.y - axis.track.top() - pointer_offset) / travel).clamp(0.0, 1.0)
            };
            resource
                .handle
                .set_offset(point(current.x, -(max.height * progress)));
        }
        Some(ScrollbarDrag::Horizontal { pointer_offset }) => {
            let Some(axis) = geometry.horizontal else {
                return;
            };
            let travel = axis.track.size.width - axis.thumb.size.width;
            let progress = if travel <= px(0.) {
                0.0
            } else {
                ((pointer.x - axis.track.left() - pointer_offset) / travel).clamp(0.0, 1.0)
            };
            resource
                .handle
                .set_offset(point(-(max.width * progress), current.y));
        }
        None => {}
    }
}

fn update_list_drag(
    resource: &ManagedListResource,
    pointer: Point<Pixels>,
    geometry: ScrollbarGeometry,
    estimated_item_height: Pixels,
) {
    let Some(ScrollbarDrag::Vertical { pointer_offset }) = resource.interaction.drag.get() else {
        return;
    };
    let Some(axis) = geometry.vertical else {
        return;
    };
    let travel = axis.track.size.height - axis.thumb.size.height;
    let progress = if travel <= px(0.) {
        0.0
    } else {
        ((pointer.y - axis.track.top() - pointer_offset) / travel).clamp(0.0, 1.0)
    };
    let visible_items = resource.state.viewport_bounds().size.height / estimated_item_height;
    let scrollable_items = (resource.item_count as f32 - visible_items).max(0.0);
    let item_position = scrollable_items * progress;
    let item_ix = item_position.floor() as usize;
    resource.state.scroll_to(ListOffset {
        item_ix,
        offset_in_item: estimated_item_height * (item_position - item_ix as f32),
    });
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
    fn vertical_thumb_tracks_the_full_scroll_range() {
        let viewport = Bounds::new(point(px(10.), px(20.)), size(px(200.), px(100.)));
        let top = vertical_geometry(viewport, px(0.), px(900.)).unwrap();
        let bottom = vertical_geometry(viewport, px(-900.), px(900.)).unwrap();

        assert_eq!(top.thumb.top(), top.track.top());
        assert_eq!(bottom.thumb.bottom(), bottom.track.bottom());
        assert_eq!(top.thumb.size.height, MIN_THUMB);
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
