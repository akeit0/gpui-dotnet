use std::rc::Rc;

use gpui::{
    AnyElement, App, Context, ElementId, Entity, FocusHandle, InteractiveElement, IntoElement,
    KeyDownEvent, MouseButton, MouseDownEvent, ParentElement, Point, Styled, WeakFocusHandle,
    Window, anchored, deferred, div, point, px,
};

use crate::{
    app_host::ManagedView,
    overlay::{OverlayStack, OverlayToken},
    resources::ResourceKey,
};

#[derive(Clone, Copy)]
pub(crate) struct ContextMenuConfiguration {
    pub(crate) priority: u32,
    pub(crate) margin: f32,
}

struct ContextMenuState {
    visible: bool,
    position: Point<gpui::Pixels>,
    focus: FocusHandle,
    previous_focus: Option<WeakFocusHandle>,
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
    window: &mut Window,
    cx: &mut Context<ManagedView>,
) -> AnyElement {
    let menu_id: ElementId = gpui::SharedString::from(format!(
        "managed-context-menu-{}-{}",
        key.owner_view, key.key
    ))
    .into();
    let state = window.use_keyed_state(menu_id.clone(), cx, |_, cx| ContextMenuState {
        visible: false,
        position: Point::default(),
        focus: cx.focus_handle().tab_stop(false),
        previous_focus: None,
    });

    let open_state = state.clone();
    let open_focus = state.read(cx).focus.clone();
    let mut host = host
        .id((menu_id.clone(), "trigger"))
        .on_mouse_down(
            MouseButton::Right,
            move |event: &MouseDownEvent, window, cx| {
                cx.stop_propagation();
                window.prevent_default();

                let previous_focus = (!open_state.read(cx).visible)
                    .then(|| window.focused(cx).map(|focus| focus.downgrade()))
                    .flatten();
                open_state.update(cx, |state, cx| {
                    state.visible = true;
                    state.position = event.position;
                    if previous_focus.is_some() {
                        state.previous_focus = previous_focus;
                    }
                    cx.notify();
                });
                open_focus.focus(window, cx);
                window.refresh();
            },
        )
        .child(trigger);

    if !state.read(cx).visible {
        return host.into_any_element();
    }

    let focus = state.read(cx).focus.clone();
    let position = state.read(cx).position;
    overlay_stack.set_captures_input(&overlay_token, true);

    let selected_state = state.clone();
    let selected_stack = overlay_stack.clone();
    let selected_token = overlay_token.clone();
    let right_pressed_state = state.clone();
    let right_pressed_stack = overlay_stack.clone();
    let right_pressed_token = overlay_token.clone();
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
    let menu = anchored()
        .position(position)
        .snap_to_window_with_margin(px(configuration.margin))
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

    let escape_state = state;
    let escape_stack = overlay_stack;
    let escape_token = overlay_token;
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
    host = host.child(deferred(layer).with_priority(configuration.priority as usize));
    host.into_any_element()
}

fn close_context_menu(state: &Entity<ContextMenuState>, window: &mut Window, cx: &mut App) {
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
    if let Some(previous_focus) = previous_focus.and_then(|focus| focus.upgrade()) {
        previous_focus.focus(window, cx);
    } else {
        window.blur(cx);
    }
    window.refresh();
}
