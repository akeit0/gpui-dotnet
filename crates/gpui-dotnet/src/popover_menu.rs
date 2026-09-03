use std::{
    cell::{Cell, RefCell},
    rc::Rc,
};

use gpui::{
    AnyElement, App, Bounds, Context, ElementId, Entity, Focusable, InteractiveElement,
    IntoElement, KeyDownEvent, MouseButton, MouseDownEvent, ParentElement,
    StatefulInteractiveElement, Styled, WeakEntity, Window, canvas, div, px,
};
use gpui_base::{Align, Placement, PopoverState, Popup};

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

#[derive(Default)]
pub(crate) struct PopoverMenuGroup {
    active: RefCell<Option<WeakEntity<PopoverState>>>,
}

impl PopoverMenuGroup {
    fn active(&self) -> Option<Entity<PopoverState>> {
        self.active.borrow().as_ref().and_then(WeakEntity::upgrade)
    }

    fn has_open_menu(&self, cx: &App) -> bool {
        self.active().is_some_and(|state| state.read(cx).is_open())
    }

    fn set_active(&self, state: &Entity<PopoverState>) {
        self.active.replace(Some(state.downgrade()));
    }

    fn clear_if_active(&self, state: &Entity<PopoverState>) -> bool {
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
    let state = window.use_keyed_state(menu_id.clone(), cx, |_, cx| PopoverState::new(false, cx));
    let trigger_bounds = Rc::new(Cell::new(Bounds::default()));
    let measured_bounds = trigger_bounds.clone();
    let measurement = canvas(
        move |bounds, _, _| measured_bounds.set(bounds),
        |_, _, _, _| {},
    )
    .absolute()
    .inset_0();

    let open_state = state.clone();
    let hover_state = state.clone();
    let hover_group = group.clone();
    let open_group = group.clone();
    let host = host
        .relative()
        .flex()
        .flex_none()
        .id((menu_id.clone(), "trigger"))
        .on_hover(move |hovered, window, cx| {
            if *hovered && !hover_state.read(cx).is_open() && hover_group.has_open_menu(cx) {
                open_popover_menu(&hover_state, &hover_group, window, cx);
            }
        })
        .on_mouse_down(MouseButton::Left, move |_: &MouseDownEvent, window, cx| {
            cx.stop_propagation();
            window.prevent_default();
            if open_state.read(cx).is_open() {
                close_popover_menu(&open_state, &open_group, window, cx);
                return;
            }

            open_popover_menu(&open_state, &open_group, window, cx);
        })
        .child(trigger)
        .child(measurement);
    let mut popup = Popup::new(menu_id.clone(), host)
        .placement(Placement::Bottom)
        .align(Align::Start)
        .margin(px(configuration.margin))
        .priority(configuration.priority as usize);

    if !state.read(cx).is_open() {
        return popup.into_any_element();
    }

    let focus = state.read(cx).focus_handle(cx);
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
    popup = popup.content(content);
    popup.into_any_element()
}

fn open_popover_menu(
    state: &Entity<PopoverState>,
    group: &PopoverMenuGroup,
    window: &mut Window,
    cx: &mut App,
) {
    if let Some(active) = group.active().filter(|active| active != state) {
        active.update(cx, |active, cx| active.dismiss(window, cx));
    }
    state.update(cx, |state, cx| {
        state.show(window, cx);
    });
    group.set_active(state);
    window.refresh();
}

fn close_popover_menu(
    state: &Entity<PopoverState>,
    group: &PopoverMenuGroup,
    window: &mut Window,
    cx: &mut App,
) {
    if !state.read(cx).is_open() {
        return;
    }
    state.update(cx, |state, cx| state.dismiss(window, cx));
    if !group.clear_if_active(state) {
        window.refresh();
        return;
    }
    window.refresh();
}
