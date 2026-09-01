use std::{
    cell::{Cell, RefCell},
    rc::Rc,
};

use gpui::{
    AnyElement, App, Bounds, Context, Display, Element, ElementId, Entity, FocusHandle,
    GlobalElementId, InspectorElementId, InteractiveElement, IntoElement, KeyDownEvent, LayoutId,
    MouseButton, MouseDownEvent, ParentElement, Pixels, Position, Size, StatefulInteractiveElement,
    Style, Styled, WeakEntity, WeakFocusHandle, Window, canvas, deferred, div, point, px,
};

use crate::{
    app_host::ManagedView,
    overlay::{OverlayStack, OverlayToken},
    resources::ResourceKey,
};

#[derive(Clone, Copy)]
pub(crate) struct PopoverMenuConfiguration {
    pub(crate) priority: u32,
    pub(crate) margin: f32,
}

struct PopoverMenuState {
    visible: bool,
    focus: FocusHandle,
    previous_focus: Option<WeakFocusHandle>,
}

#[derive(Default)]
pub(crate) struct PopoverMenuGroup {
    active: RefCell<Option<WeakEntity<PopoverMenuState>>>,
}

impl PopoverMenuGroup {
    fn active(&self) -> Option<Entity<PopoverMenuState>> {
        self.active.borrow().as_ref().and_then(WeakEntity::upgrade)
    }

    fn has_open_menu(&self, cx: &App) -> bool {
        self.active().is_some_and(|state| state.read(cx).visible)
    }

    fn set_active(&self, state: &Entity<PopoverMenuState>) {
        self.active.replace(Some(state.downgrade()));
    }

    fn clear_if_active(&self, state: &Entity<PopoverMenuState>) -> bool {
        let is_active = self
            .active
            .borrow()
            .as_ref()
            .is_some_and(|active| active == state);
        if is_active {
            self.active.replace(None);
        }
        is_active
    }
}

#[allow(clippy::too_many_arguments)]
pub(crate) fn popover_menu(
    key: ResourceKey,
    host: gpui::Div,
    trigger: AnyElement,
    content: AnyElement,
    configuration: PopoverMenuConfiguration,
    group: Rc<PopoverMenuGroup>,
    overlay_stack: Rc<OverlayStack>,
    overlay_token: OverlayToken,
    window: &mut Window,
    cx: &mut Context<ManagedView>,
) -> AnyElement {
    let menu_id: ElementId = gpui::SharedString::from(format!(
        "managed-popover-menu-{}-{}",
        key.owner_view, key.key
    ))
    .into();
    let state = window.use_keyed_state(menu_id.clone(), cx, |_, cx| PopoverMenuState {
        visible: false,
        focus: cx.focus_handle().tab_stop(false),
        previous_focus: None,
    });
    let trigger_bounds = Rc::new(Cell::new(Bounds::default()));
    let measured_bounds = trigger_bounds.clone();
    let measurement = canvas(
        move |bounds, _, _| measured_bounds.set(bounds),
        |_, _, _, _| {},
    )
    .absolute()
    .inset_0();

    let open_state = state.clone();
    let open_focus = state.read(cx).focus.clone();
    let hover_state = state.clone();
    let hover_focus = open_focus.clone();
    let hover_group = group.clone();
    let open_group = group.clone();
    let mut host = host
        .relative()
        .flex()
        .flex_none()
        .id((menu_id.clone(), "trigger"))
        .on_hover(move |hovered, window, cx| {
            if *hovered && !hover_state.read(cx).visible && hover_group.has_open_menu(cx) {
                open_popover_menu(&hover_state, &hover_focus, &hover_group, window, cx);
            }
        })
        .on_mouse_down(MouseButton::Left, move |_: &MouseDownEvent, window, cx| {
            cx.stop_propagation();
            window.prevent_default();
            if open_state.read(cx).visible {
                close_popover_menu(&open_state, &open_group, window, cx);
                return;
            }

            open_popover_menu(&open_state, &open_focus, &open_group, window, cx);
        })
        .child(trigger)
        .child(measurement);

    if !state.read(cx).visible {
        return host.into_any_element();
    }

    let focus = state.read(cx).focus.clone();
    overlay_stack.set_captures_input(&overlay_token, true);
    let outside_state = state.clone();
    let outside_stack = overlay_stack.clone();
    let outside_token = overlay_token.clone();
    let outside_group = group.clone();
    let outside_trigger_bounds = trigger_bounds.clone();
    let selected_state = state.clone();
    let selected_stack = overlay_stack.clone();
    let selected_token = overlay_token.clone();
    let selected_group = group.clone();
    let escape_state = state.clone();
    let escape_stack = overlay_stack;
    let escape_token = overlay_token;
    let escape_group = group;
    let content = div()
        .id((menu_id, "content"))
        .occlude()
        .track_focus(&focus)
        .on_mouse_down_out(move |event: &MouseDownEvent, window, cx| {
            if !outside_stack.is_topmost(&outside_token) {
                return;
            }
            let trigger_pressed = outside_trigger_bounds.get().contains(&event.position);
            close_popover_menu(&outside_state, &outside_group, window, cx);
            if trigger_pressed {
                cx.stop_propagation();
                window.prevent_default();
            }
        })
        .on_mouse_up(MouseButton::Left, move |_, window, cx| {
            if !selected_stack.is_topmost(&selected_token) {
                return;
            }
            close_popover_menu(&selected_state, &selected_group, window, cx);
        })
        .on_key_down(move |event: &KeyDownEvent, window, cx| {
            if event.keystroke.key != "escape" {
                return;
            }
            if !escape_stack.is_topmost(&escape_token) {
                return;
            }
            cx.stop_propagation();
            close_popover_menu(&escape_state, &escape_group, window, cx);
        })
        .child(content);
    let menu =
        PopoverMenuPositioner::new(content.into_any_element(), trigger_bounds, configuration);
    host = host.child(deferred(menu).with_priority(configuration.priority as usize));
    host.into_any_element()
}

fn open_popover_menu(
    state: &Entity<PopoverMenuState>,
    focus: &FocusHandle,
    group: &PopoverMenuGroup,
    window: &mut Window,
    cx: &mut App,
) {
    let active = group.active();
    let transferring = active.as_ref().is_some_and(|active| active != state);
    let previous_focus = if let Some(active) = active.filter(|active| active != state) {
        active.update(cx, |active, cx| {
            active.visible = false;
            cx.notify();
            active.previous_focus.take()
        })
    } else {
        None
    };
    let previous_focus = if transferring {
        previous_focus
    } else {
        window.focused(cx).map(|focus| focus.downgrade())
    };

    state.update(cx, |state, cx| {
        state.visible = true;
        state.previous_focus = previous_focus;
        cx.notify();
    });
    group.set_active(state);
    focus.focus(window);
    window.refresh();
}

fn close_popover_menu(
    state: &Entity<PopoverMenuState>,
    group: &PopoverMenuGroup,
    window: &mut Window,
    cx: &mut App,
) {
    let (changed, previous_focus) = state.update(cx, |state, cx| {
        if !state.visible {
            return (false, None);
        }
        state.visible = false;
        cx.notify();
        (true, state.previous_focus.take())
    });
    if !changed {
        return;
    }
    if !group.clear_if_active(state) {
        window.refresh();
        return;
    }
    if let Some(previous_focus) = previous_focus.and_then(|focus| focus.upgrade()) {
        previous_focus.focus(window);
    } else {
        window.blur();
    }
    window.refresh();
}

struct PopoverMenuPositioner {
    child: AnyElement,
    trigger_bounds: Rc<Cell<Bounds<Pixels>>>,
    configuration: PopoverMenuConfiguration,
}

impl PopoverMenuPositioner {
    fn new(
        child: AnyElement,
        trigger_bounds: Rc<Cell<Bounds<Pixels>>>,
        configuration: PopoverMenuConfiguration,
    ) -> Self {
        Self {
            child,
            trigger_bounds,
            configuration,
        }
    }
}

impl Element for PopoverMenuPositioner {
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
        let desired = popover_menu_origin(
            self.trigger_bounds.get(),
            child_size,
            window.viewport_size(),
            px(self.configuration.margin),
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

impl IntoElement for PopoverMenuPositioner {
    type Element = Self;

    fn into_element(self) -> Self::Element {
        self
    }
}

fn popover_menu_origin(
    trigger: Bounds<Pixels>,
    content: Size<Pixels>,
    viewport: Size<Pixels>,
    margin: Pixels,
) -> gpui::Point<Pixels> {
    point(
        clamp_coordinate(trigger.left(), content.width, viewport.width, margin),
        clamp_coordinate(trigger.bottom(), content.height, viewport.height, margin),
    )
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

    #[test]
    fn menu_attaches_below_trigger_leading_edge() {
        let origin = popover_menu_origin(
            Bounds::new(point(px(180.), px(0.)), size(px(40.), px(38.))),
            size(px(224.), px(120.)),
            size(px(1040.), px(700.)),
            px(8.),
        );

        assert_eq!(origin, point(px(180.), px(38.)));
    }

    #[test]
    fn menu_snaps_inside_viewport_margin() {
        let origin = popover_menu_origin(
            Bounds::new(point(px(990.), px(670.)), size(px(40.), px(30.))),
            size(px(224.), px(120.)),
            size(px(1040.), px(700.)),
            px(8.),
        );

        assert_eq!(origin, point(px(808.), px(572.)));
    }
}
