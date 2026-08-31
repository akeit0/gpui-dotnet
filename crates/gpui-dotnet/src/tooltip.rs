use std::{cell::Cell, rc::Rc, time::Duration};

use gpui::{
    AnyElement, App, Bounds, Context, Display, Element, ElementId, GlobalElementId,
    InspectorElementId, InteractiveElement, IntoElement, LayoutId, MouseButton, ParentElement,
    Pixels, Point, Position, Size, StatefulInteractiveElement, Style, Styled, Window, canvas,
    deferred, div, point, px,
};

use crate::{app_host::ManagedView, resources::ResourceKey};

const TOOLTIP_PRIORITY: usize = 200;

#[derive(Clone, Copy)]
pub(crate) struct TooltipConfiguration {
    pub(crate) placement: u32,
    pub(crate) alignment: u32,
    pub(crate) show_delay_ms: u64,
    pub(crate) hide_delay_ms: u64,
    pub(crate) gap: f32,
    pub(crate) margin: f32,
}

#[derive(Default)]
struct TooltipState {
    visible: bool,
    trigger_hovered: bool,
    content_hovered: bool,
    generation: u64,
}

#[derive(Clone, Copy)]
enum HoverTarget {
    Trigger,
    Content,
}

#[derive(Clone, Copy)]
enum Transition {
    Show,
    Hide,
}

pub(crate) fn tooltip(
    key: ResourceKey,
    trigger: AnyElement,
    content: AnyElement,
    configuration: TooltipConfiguration,
    window: &mut Window,
    cx: &mut Context<ManagedView>,
) -> AnyElement {
    let tooltip_id: ElementId =
        gpui::SharedString::from(format!("managed-tooltip-{}-{}", key.owner_view, key.key)).into();
    let state = window.use_keyed_state(tooltip_id.clone(), cx, |_, _| TooltipState::default());
    let trigger_bounds = Rc::new(Cell::new(Bounds::default()));

    let measured_bounds = trigger_bounds.clone();
    let measurement = canvas(
        move |bounds, _, _| measured_bounds.set(bounds),
        |_, _, _, _| {},
    )
    .absolute()
    .inset_0();

    let trigger_state = state.clone();
    let trigger_config = configuration;
    let pressed_state = state.clone();
    let mut host = div()
        .relative()
        .flex()
        .flex_none()
        .id((tooltip_id.clone(), "trigger"))
        .on_hover(move |hovered, window, cx| {
            update_hover(
                &trigger_state,
                HoverTarget::Trigger,
                *hovered,
                trigger_config,
                window,
                cx,
            );
        })
        .on_mouse_down(MouseButton::Left, move |_, window, cx| {
            hide_immediately(&pressed_state, window, cx);
        })
        .child(trigger)
        .child(measurement);

    if state.read(cx).visible {
        let content_state = state.clone();
        let content_config = configuration;
        let pressed_state = state.clone();
        let content = div()
            .id((tooltip_id, "content"))
            .occlude()
            .on_hover(move |hovered, window, cx| {
                update_hover(
                    &content_state,
                    HoverTarget::Content,
                    *hovered,
                    content_config,
                    window,
                    cx,
                );
            })
            .on_mouse_down(MouseButton::Left, move |_, window, cx| {
                hide_immediately(&pressed_state, window, cx);
            })
            .child(content)
            .into_any_element();
        host = host.child(
            deferred(TooltipPositioner::new(
                content,
                trigger_bounds,
                configuration,
            ))
            .with_priority(TOOLTIP_PRIORITY),
        );
    }

    host.into_any_element()
}

fn update_hover(
    state: &gpui::Entity<TooltipState>,
    target: HoverTarget,
    hovered: bool,
    configuration: TooltipConfiguration,
    window: &mut Window,
    cx: &mut App,
) {
    let scheduled = state.update(cx, |state, _| {
        match target {
            HoverTarget::Trigger => state.trigger_hovered = hovered,
            HoverTarget::Content => state.content_hovered = hovered,
        }
        state.generation = state.generation.wrapping_add(1);
        let generation = state.generation;
        let any_hovered = state.trigger_hovered || state.content_hovered;
        if any_hovered && !state.visible && state.trigger_hovered {
            Some((Transition::Show, generation, configuration.show_delay_ms))
        } else if !any_hovered && state.visible {
            Some((Transition::Hide, generation, configuration.hide_delay_ms))
        } else {
            None
        }
    });

    if let Some((transition, generation, delay_ms)) = scheduled {
        schedule_transition(state.clone(), transition, generation, delay_ms, window, cx);
    }
}

fn schedule_transition(
    state: gpui::Entity<TooltipState>,
    transition: Transition,
    generation: u64,
    delay_ms: u64,
    window: &mut Window,
    cx: &mut App,
) {
    if delay_ms == 0 {
        apply_transition(&state, transition, generation, window, cx);
        return;
    }

    window
        .spawn(cx, async move |cx| {
            cx.background_executor()
                .timer(Duration::from_millis(delay_ms))
                .await;
            cx.update(|window, cx| {
                apply_transition(&state, transition, generation, window, cx);
            })
            .ok();
        })
        .detach();
}

fn apply_transition(
    state: &gpui::Entity<TooltipState>,
    transition: Transition,
    generation: u64,
    window: &mut Window,
    cx: &mut App,
) {
    let changed = state.update(cx, |state, cx| {
        if state.generation != generation {
            return false;
        }
        let should_show = state.trigger_hovered || state.content_hovered;
        let visible = match transition {
            Transition::Show => should_show,
            Transition::Hide => should_show,
        };
        if state.visible == visible {
            return false;
        }
        state.visible = visible;
        cx.notify();
        true
    });
    if changed {
        window.refresh();
    }
}

fn hide_immediately(state: &gpui::Entity<TooltipState>, window: &mut Window, cx: &mut App) {
    let changed = state.update(cx, |state, cx| {
        state.generation = state.generation.wrapping_add(1);
        state.trigger_hovered = false;
        state.content_hovered = false;
        if !state.visible {
            return false;
        }
        state.visible = false;
        cx.notify();
        true
    });
    if changed {
        window.refresh();
    }
}

struct TooltipPositioner {
    child: AnyElement,
    trigger_bounds: Rc<Cell<Bounds<Pixels>>>,
    configuration: TooltipConfiguration,
}

impl TooltipPositioner {
    fn new(
        child: AnyElement,
        trigger_bounds: Rc<Cell<Bounds<Pixels>>>,
        configuration: TooltipConfiguration,
    ) -> Self {
        Self {
            child,
            trigger_bounds,
            configuration,
        }
    }
}

impl Element for TooltipPositioner {
    type RequestLayoutState = LayoutId;
    type PrepaintState = ();

    fn id(&self) -> Option<ElementId> {
        None
    }

    fn source_location(&self) -> Option<&'static core::panic::Location<'static>> {
        None
    }

    fn request_layout(
        &mut self,
        _id: Option<&GlobalElementId>,
        _inspector_id: Option<&InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (LayoutId, Self::RequestLayoutState) {
        let child_layout = self.child.request_layout(window, cx);
        let layout = window.request_layout(
            Style {
                position: Position::Absolute,
                display: Display::Flex,
                ..Style::default()
            },
            [child_layout],
            cx,
        );
        (layout, child_layout)
    }

    fn prepaint(
        &mut self,
        _id: Option<&GlobalElementId>,
        _inspector_id: Option<&InspectorElementId>,
        bounds: Bounds<Pixels>,
        child_layout: &mut Self::RequestLayoutState,
        window: &mut Window,
        cx: &mut App,
    ) {
        let child_size = window.layout_bounds(*child_layout).size;
        let desired = tooltip_origin(
            self.trigger_bounds.get(),
            child_size,
            window.viewport_size(),
            self.configuration,
        );
        let offset = point(
            (desired.x - bounds.origin.x).round(),
            (desired.y - bounds.origin.y).round(),
        );
        window.with_element_offset(offset, |window| self.child.prepaint(window, cx));
    }

    fn paint(
        &mut self,
        _id: Option<&GlobalElementId>,
        _inspector_id: Option<&InspectorElementId>,
        _bounds: Bounds<Pixels>,
        _request_layout: &mut Self::RequestLayoutState,
        _prepaint: &mut Self::PrepaintState,
        window: &mut Window,
        cx: &mut App,
    ) {
        self.child.paint(window, cx);
    }
}

impl IntoElement for TooltipPositioner {
    type Element = Self;

    fn into_element(self) -> Self::Element {
        self
    }
}

fn tooltip_origin(
    trigger: Bounds<Pixels>,
    content: Size<Pixels>,
    viewport: Size<Pixels>,
    configuration: TooltipConfiguration,
) -> Point<Pixels> {
    let gap = px(configuration.gap);
    let margin = px(configuration.margin);
    let placements: &[u32] = match configuration.placement {
        1 => &[1, 3, 2, 4],
        2 => &[2, 4, 3, 1],
        3 => &[3, 1, 2, 4],
        4 => &[4, 2, 3, 1],
        _ => &[3, 1, 2, 4],
    };

    for placement in placements {
        let mut origin = origin_for(trigger, content, *placement, configuration.alignment, gap);
        clamp_cross_axis(&mut origin, content, viewport, *placement, margin);
        if fits_primary_axis(origin, content, viewport, *placement, margin) {
            return origin;
        }
    }

    let mut origin = origin_for(
        trigger,
        content,
        placements[0],
        configuration.alignment,
        gap,
    );
    origin.x = clamp_coordinate(origin.x, content.width, viewport.width, margin);
    origin.y = clamp_coordinate(origin.y, content.height, viewport.height, margin);
    origin
}

fn origin_for(
    trigger: Bounds<Pixels>,
    content: Size<Pixels>,
    placement: u32,
    alignment: u32,
    gap: Pixels,
) -> Point<Pixels> {
    let horizontal = || match alignment {
        0 => trigger.left(),
        2 => trigger.right() - content.width,
        _ => trigger.left() + (trigger.size.width - content.width) / 2.,
    };
    let vertical = || match alignment {
        0 => trigger.top(),
        2 => trigger.bottom() - content.height,
        _ => trigger.top() + (trigger.size.height - content.height) / 2.,
    };
    match placement {
        1 => point(horizontal(), trigger.top() - gap - content.height),
        2 => point(trigger.right() + gap, vertical()),
        4 => point(trigger.left() - gap - content.width, vertical()),
        _ => point(horizontal(), trigger.bottom() + gap),
    }
}

fn clamp_cross_axis(
    origin: &mut Point<Pixels>,
    content: Size<Pixels>,
    viewport: Size<Pixels>,
    placement: u32,
    margin: Pixels,
) {
    if matches!(placement, 1 | 3) {
        origin.x = clamp_coordinate(origin.x, content.width, viewport.width, margin);
    } else {
        origin.y = clamp_coordinate(origin.y, content.height, viewport.height, margin);
    }
}

fn fits_primary_axis(
    origin: Point<Pixels>,
    content: Size<Pixels>,
    viewport: Size<Pixels>,
    placement: u32,
    margin: Pixels,
) -> bool {
    if matches!(placement, 1 | 3) {
        origin.y >= margin && origin.y + content.height <= viewport.height - margin
    } else {
        origin.x >= margin && origin.x + content.width <= viewport.width - margin
    }
}

fn clamp_coordinate(
    coordinate: Pixels,
    extent: Pixels,
    viewport_extent: Pixels,
    margin: Pixels,
) -> Pixels {
    let maximum = viewport_extent - margin - extent;
    if maximum < margin {
        margin
    } else {
        coordinate.max(margin).min(maximum)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::size;

    fn configuration(placement: u32) -> TooltipConfiguration {
        TooltipConfiguration {
            placement,
            alignment: 1,
            show_delay_ms: 500,
            hide_delay_ms: 300,
            gap: 8.,
            margin: 8.,
        }
    }

    #[test]
    fn auto_prefers_bottom_and_centers_on_trigger() {
        let origin = tooltip_origin(
            Bounds::new(point(px(100.), px(100.)), size(px(40.), px(20.))),
            size(px(80.), px(30.)),
            size(px(400.), px(300.)),
            configuration(0),
        );

        assert_eq!(origin, point(px(80.), px(128.)));
    }

    #[test]
    fn requested_bottom_flips_above_near_viewport_edge() {
        let origin = tooltip_origin(
            Bounds::new(point(px(100.), px(270.)), size(px(40.), px(20.))),
            size(px(80.), px(30.)),
            size(px(400.), px(300.)),
            configuration(3),
        );

        assert_eq!(origin, point(px(80.), px(232.)));
    }

    #[test]
    fn cross_axis_is_clamped_to_viewport_margin() {
        let origin = tooltip_origin(
            Bounds::new(point(px(2.), px(100.)), size(px(20.), px(20.))),
            size(px(100.), px(30.)),
            size(px(400.), px(300.)),
            configuration(3),
        );

        assert_eq!(origin.x, px(8.));
    }
}
