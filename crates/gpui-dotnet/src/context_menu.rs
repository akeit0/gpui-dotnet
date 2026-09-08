use std::rc::Rc;

use gpui::{
    Anchor, AnyElement, App, ElementId, Entity, Focusable, InteractiveElement, IntoElement,
    KeyDownEvent, MouseButton, MouseDownEvent, ParentElement, Point, Styled, Window, anchored,
    deferred, div, point, px,
};
use gpui_base::{PopoverState, Positioner};

use crate::{
    overlay::{OverlayStack, OverlayToken},
    resources::ResourceKey,
};

#[derive(Clone, Copy)]
pub(crate) struct ContextMenuConfiguration {
    pub(crate) priority: u32,
    pub(crate) margin: f32,
}

#[allow(clippy::too_many_arguments)]
pub(crate) fn context_menu(
    key: ResourceKey,
    host: gpui::Div,
    trigger: AnyElement,
    content: AnyElement,
    configuration: ContextMenuConfiguration,
    overlay_stack: Rc<OverlayStack>,
    overlay_token: OverlayToken,
    item_anchor: Option<(Rc<crate::item_menu::ItemMenus>, u64)>,
    window: &mut Window,
    cx: &mut App,
) -> AnyElement {
    let menu_id: ElementId = gpui::SharedString::from(format!(
        "managed-context-menu-{}-{}",
        key.owner_view, key.key
    ))
    .into();
    let item_binding = if let Some((menus, id)) = &item_anchor {
        let Some(binding) = menus.bind(*id, key.owner_view, window, cx) else {
            return div().into_any_element();
        };
        Some(binding)
    } else {
        None
    };
    let state = item_binding
        .as_ref()
        .map(|(state, _)| state.clone())
        .unwrap_or_else(|| {
            window.use_keyed_state((menu_id.clone(), "popover"), cx, |_, cx| {
                PopoverState::new(false, cx)
            })
        });
    let position =
        window.use_keyed_state((menu_id.clone(), "position"), cx, |_, _| Point::default());

    let open_state = state.clone();
    let open_position = position.clone();
    let host = if item_anchor.is_some() {
        host.absolute()
    } else {
        host
    };
    let host = host.id((menu_id.clone(), "trigger"));
    let mut host = if item_anchor.is_some() {
        host
    } else {
        host.on_mouse_down(
            MouseButton::Right,
            move |event: &MouseDownEvent, window, cx| {
                cx.stop_propagation();
                window.prevent_default();

                open_position.update(cx, |position, _| {
                    *position = event.position;
                });
                open_state.update(cx, |state, cx| state.show(window, cx));
                window.refresh();
            },
        )
        .child(trigger)
    };

    if !state.read(cx).is_open() {
        return host.into_any_element();
    }

    let focus = state.read(cx).focus_handle(cx);
    let position = item_binding.map_or_else(|| *position.read(cx), |(_, position)| position);
    overlay_stack.set_captures_input(&overlay_token, true);

    let selected_state = state.clone();
    let selected_stack = overlay_stack.clone();
    let selected_token = overlay_token.clone();
    let right_pressed_state = state.clone();
    let right_pressed_stack = overlay_stack.clone();
    let right_pressed_token = overlay_token.clone();
    let scroll_dismiss = {
        let state = state.clone();
        let stack = overlay_stack.clone();
        let token = overlay_token.clone();
        move |_: &gpui::ScrollWheelEvent, window: &mut Window, cx: &mut App| {
            if stack.is_topmost(&token) {
                cx.stop_propagation();
                close_context_menu(&state, window, cx);
            }
        }
    };
    let content = div()
        .id((menu_id.clone(), "content"))
        .occlude()
        .on_mouse_up(MouseButton::Left, move |_, window, cx| {
            if !selected_stack.is_topmost(&selected_token) {
                return;
            }
            close_context_menu(&selected_state, window, cx);
        })
        .on_mouse_down(MouseButton::Right, move |_, window, cx| {
            if !right_pressed_stack.is_topmost(&right_pressed_token) {
                return;
            }
            cx.stop_propagation();
            window.prevent_default();
            close_context_menu(&right_pressed_state, window, cx);
        })
        .child(content);
    let content = if item_anchor.is_some() {
        content.on_scroll_wheel(scroll_dismiss.clone())
    } else {
        content
    };
    let menu = Positioner::corner(Anchor::TopLeft, position)
        .margin(px(configuration.margin))
        .occlude()
        .child(content);

    let left_backdrop_state = state.clone();
    let left_backdrop_stack = overlay_stack.clone();
    let left_backdrop_token = overlay_token.clone();
    let right_backdrop_state = state.clone();
    let right_backdrop_stack = overlay_stack.clone();
    let right_backdrop_token = overlay_token.clone();
    let backdrop = div()
        .absolute()
        .inset_0()
        .id((menu_id, "backdrop"))
        .occlude()
        .on_mouse_down(MouseButton::Left, move |_, window, cx| {
            if !left_backdrop_stack.is_topmost(&left_backdrop_token) {
                return;
            }
            cx.stop_propagation();
            window.prevent_default();
            close_context_menu(&left_backdrop_state, window, cx);
        })
        .on_mouse_down(MouseButton::Right, move |_, window, cx| {
            if !right_backdrop_stack.is_topmost(&right_backdrop_token) {
                return;
            }
            cx.stop_propagation();
            window.prevent_default();
            close_context_menu(&right_backdrop_state, window, cx);
        });

    let backdrop = if item_anchor.is_some() {
        backdrop.on_scroll_wheel(scroll_dismiss)
    } else {
        backdrop
    };

    let escape_state = state;
    let escape_stack = overlay_stack.clone();
    let escape_token = overlay_token.clone();
    let viewport = window.viewport_size();
    let layer = div()
        .relative()
        .w(viewport.width)
        .h(viewport.height)
        .track_focus(&focus)
        .on_key_down(move |event: &KeyDownEvent, window, cx| {
            if event.keystroke.key != "escape" {
                return;
            }
            if !escape_stack.is_topmost(&escape_token) {
                return;
            }
            cx.stop_propagation();
            close_context_menu(&escape_state, window, cx);
        })
        .child(backdrop)
        .child(menu);
    let layer = anchored().position(point(px(0.), px(0.))).child(layer);
    let layer = if let Some((menus, id)) = item_anchor {
        crate::item_menu::Guard {
            child: layer.into_any_element(),
            menus,
            id,
            stack: overlay_stack,
            token: overlay_token,
        }
        .into_any_element()
    } else {
        layer.into_any_element()
    };
    host = host.child(deferred(layer).with_priority(configuration.priority as usize));
    host.into_any_element()
}

fn close_context_menu(state: &Entity<PopoverState>, window: &mut Window, cx: &mut App) {
    if !state.read(cx).is_open() {
        return;
    }
    state.update(cx, |state, cx| state.dismiss(window, cx));
    window.refresh();
}
