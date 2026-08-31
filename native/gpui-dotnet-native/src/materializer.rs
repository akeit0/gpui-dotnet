use std::path::PathBuf;

use gpui::{
    AnyElement, ClickEvent, Context, ElementId, Entity, FocusHandle, InteractiveElement,
    IntoElement, KeyDownEvent, ObjectFit, ParentElement, SharedString, StatefulInteractiveElement,
    Styled, StyledImage, WeakFocusHandle, Window, WindowControlArea, anchored, deferred, div, img,
    list, point, px, relative, rgba,
};

use crate::{
    abi::{ManagedCallbacks, NativeClickEvent},
    app_host::ManagedView,
    components,
    context_menu::{ContextMenuConfiguration, context_menu},
    resources::{ResourceStore, input_configuration, list_configuration, resource_key},
    scrolling::{list_overlay, scroll_overlay},
    semantic::{
        CAPABILITY_INTERACTIVE, NativeAdapter, OP_BACKGROUND_RGBA, OP_BORDER_RGBA,
        OP_BORDER_WIDTH_PX, OP_CHECKED, OP_CONTEXT_MENU_MARGIN_PX, OP_CONTEXT_MENU_PRIORITY,
        OP_FLEX, OP_FLEX_GROW, OP_FONT_SIZE_PX, OP_GAP_PX, OP_HEIGHT_PERCENT, OP_HEIGHT_PX,
        OP_HOVER_BACKGROUND_RGBA, OP_IMAGE_GRAYSCALE, OP_IMAGE_OBJECT_FIT, OP_ITEMS_CENTER,
        OP_JUSTIFY_BETWEEN, OP_JUSTIFY_CENTER, OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE,
        OP_LIST_ESTIMATED_ITEM_HEIGHT_PX, OP_LIST_ITEM_COUNT, OP_LIST_OVERDRAW_PX,
        OP_LIST_RENDERER, OP_ON_CLICK, OP_OVERLAY_BACKDROP_RGBA, OP_OVERLAY_DISMISS_ON_BACKDROP,
        OP_OVERLAY_DISMISS_ON_ESCAPE, OP_OVERLAY_MARGIN_PX, OP_OVERLAY_MODAL,
        OP_OVERLAY_ON_DISMISS, OP_OVERLAY_PLACEMENT, OP_OVERLAY_PRIORITY, OP_PADDING_PX,
        OP_RADIUS_PX, OP_RESOURCE_OWNER, OP_SCROLL_AXIS, OP_SHOW_SCROLLBAR, OP_SMOOTH_SCROLL,
        OP_TEXT_RGBA, OP_TOOLTIP_ALIGNMENT, OP_TOOLTIP_GAP_PX, OP_TOOLTIP_HIDE_DELAY_MS,
        OP_TOOLTIP_MARGIN_PX, OP_TOOLTIP_PLACEMENT, OP_TOOLTIP_SHOW_DELAY_MS, OP_V_STACK,
        OP_WIDTH_PERCENT, OP_WIDTH_PX, OP_WINDOW_CONTROL_AREA, component_metadata,
    },
    snapshot::{SnapshotNode, ValidatedSnapshot},
    tooltip::{TooltipConfiguration, tooltip},
};

impl ManagedView {
    pub(crate) fn materialize_node(
        &self,
        node_id: u32,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let node = &snapshot.nodes[node_id as usize];
        let metadata = component_metadata(node.component)
            .expect("validated snapshots only contain registered components");

        match metadata.adapter {
            NativeAdapter::Scroll => self.materialize_scroll(node_id, node, snapshot, window, cx),
            NativeAdapter::List => self.materialize_list(node, snapshot),
            NativeAdapter::Image => materialize_image(node, snapshot),
            NativeAdapter::Input => self.materialize_input(node, snapshot, window, cx),
            NativeAdapter::Overlay => self.materialize_overlay(node, snapshot, window, cx),
            NativeAdapter::Tooltip => self.materialize_tooltip(node, snapshot, window, cx),
            NativeAdapter::ContextMenu => self.materialize_context_menu(node, snapshot, window, cx),
            _ => self.materialize_regular(node_id, node, snapshot, window, cx),
        }
    }

    fn materialize_regular(
        &self,
        node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let metadata = component_metadata(node.component).unwrap();
        let mut element = components::apply_defaults(metadata.adapter, div(), node, snapshot);

        if metadata.adapter == NativeAdapter::Text {
            element = element.child(node.data.clone());
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, window, cx));
        }
        element = apply_styles(element, node, snapshot);
        if last_op(snapshot, node, OP_FLEX_GROW).is_some() {
            // A growing flex item must be allowed to shrink below its content's intrinsic height;
            // otherwise a descendant scroll viewport expands to its full content height.
            element = element.min_h_0();
        }
        element = apply_window_control_area(element, node, snapshot);

        if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
            let event_binding = last_op(snapshot, node, OP_ON_CLICK);
            let event_token = event_binding.map_or(0, |op| op.a);
            let event_payload = event_binding.map_or(0, |op| op.b);
            let element_id = if node.data.is_empty() {
                format!("managed-control-{node_id}").into()
            } else {
                node.data.clone()
            };
            let element = element.id(element_id).cursor_pointer();
            let element = if let Some(op) = last_op(snapshot, node, OP_HOVER_BACKGROUND_RGBA) {
                let color = rgba(op.a as u32);
                element.hover(move |style| style.bg(color))
            } else {
                element.hover(|style| style.opacity(0.88))
            };
            return element
                .active(|style| style.opacity(0.72))
                .on_click(cx.listener(move |this, event: &ClickEvent, _, cx| {
                    if event_token == 0 {
                        return;
                    }
                    let status = invoke_click(
                        this.callbacks,
                        this.view_id,
                        event_token,
                        event_payload,
                        event,
                    );
                    this.after_click(status, cx);
                }))
                .into_any_element();
        }

        element.into_any_element()
    }

    fn materialize_scroll(
        &self,
        _node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("Scroll resource is missing its owner/key.")
                .into_any_element();
        };
        let resource = self.resources.scroll_resource(&key);
        let handle = resource.handle.clone();
        // Keep the viewport and its content as separate flex boxes. Without a non-shrinking
        // content box, Taffy can size the scroll child to the viewport and GPUI then computes a
        // maximum offset that ends before the actual final child.
        let mut content = div().flex().flex_col().flex_shrink_0();
        for child in snapshot.children(node) {
            content = content.child(self.materialize_node(*child, snapshot, window, cx));
        }
        let element_id = format!("managed-scroll-{}-{}", key.owner_view, key.key);
        let stateful = div()
            .flex()
            .flex_col()
            .flex_grow()
            .min_h_0()
            .min_w_0()
            .child(content)
            .id(SharedString::from(element_id));
        let axis = last_op(snapshot, node, OP_SCROLL_AXIS).map_or(0, |op| op.a as u32);
        let smooth = last_op(snapshot, node, OP_SMOOTH_SCROLL).is_none_or(|op| op.a != 0);
        let show_scrollbar = last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let scrollable = match axis {
            1 => stateful
                .overflow_x_scroll()
                .track_scroll(&handle)
                .into_any_element(),
            2 => stateful
                .overflow_scroll()
                .track_scroll(&handle)
                .into_any_element(),
            _ => stateful
                .overflow_y_scroll()
                .track_scroll(&handle)
                .into_any_element(),
        };
        let overlay = scroll_overlay(resource, axis, smooth, show_scrollbar);
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        element.child(scrollable).child(overlay).into_any_element()
    }

    fn materialize_list(&self, node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("List resource is missing its owner/key.")
                .into_any_element();
        };
        let Some(configuration) = list_configuration(snapshot, node) else {
            return div()
                .child("List resource is missing virtualization metadata.")
                .into_any_element();
        };
        let resource = self
            .resources
            .list_resource(&key, &configuration, self.snapshot_revision);
        let state = resource.borrow().state.clone();
        let resources = self.resources.clone();
        let row_scope = key.clone();
        let row_resource = resource.clone();
        let list_element = list(state, move |index, _window, _cx| {
            row_resource
                .borrow_mut()
                .render_item(index, &resources, &row_scope)
        })
        .flex_grow()
        .min_h_0()
        .min_w_0();
        let smooth = last_op(snapshot, node, OP_SMOOTH_SCROLL).is_none_or(|op| op.a != 0);
        let show_scrollbar = last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let overlay = list_overlay(
            resource,
            configuration.estimated_item_height,
            smooth,
            show_scrollbar,
        );
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        element
            .child(list_element)
            .child(overlay)
            .into_any_element()
    }

    fn materialize_input(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(configuration) = input_configuration(snapshot, node) else {
            return div()
                .child("Input resource is missing its owner or configuration.")
                .into_any_element();
        };
        let disabled = configuration.disabled;
        let input = self.resources.input_resource(&configuration, window, cx);
        let mut element = div()
            .w(px(280.))
            .h(px(38.))
            .min_w_0()
            .px(px(10.))
            .flex()
            .items_center()
            .overflow_hidden()
            .rounded(px(6.))
            .border(px(1.))
            .border_color(rgba(0xCBD5E1FF))
            .bg(rgba(0xFFFFFFFF))
            .child(input);
        if disabled {
            element = element.opacity(0.55);
        }
        apply_styles(element, node, snapshot).into_any_element()
    }

    fn materialize_overlay(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let child_id = snapshot.children(node)[0];
        let child = self.materialize_node(child_id, snapshot, window, cx);
        let placement = last_op(snapshot, node, OP_OVERLAY_PLACEMENT).map_or(0, |op| op.a);
        let priority = last_op(snapshot, node, OP_OVERLAY_PRIORITY).map_or(10, |op| op.a);
        let margin = last_op(snapshot, node, OP_OVERLAY_MARGIN_PX)
            .map_or(16.0, |op| f32::from_bits(op.a as u32));
        let modal = last_op(snapshot, node, OP_OVERLAY_MODAL).is_none_or(|op| op.a != 0);
        let backdrop_color =
            last_op(snapshot, node, OP_OVERLAY_BACKDROP_RGBA).map_or(0x00000060, |op| op.a as u32);
        let dismiss_on_backdrop =
            last_op(snapshot, node, OP_OVERLAY_DISMISS_ON_BACKDROP).is_none_or(|op| op.a != 0);
        let dismiss_on_escape =
            last_op(snapshot, node, OP_OVERLAY_DISMISS_ON_ESCAPE).is_none_or(|op| op.a != 0);
        let dismiss = last_op(snapshot, node, OP_OVERLAY_ON_DISMISS);
        let dismiss_token = dismiss.map_or(0, |op| op.a);
        let dismiss_payload = dismiss.map_or(0, |op| op.b);

        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("Overlay is missing its owner/key.")
                .into_any_element();
        };
        let overlay_id: ElementId =
            SharedString::from(format!("managed-overlay-{}-{}", key.owner_view, key.key)).into();
        let previous_focus = window.focused(cx).map(|focus| focus.downgrade());
        let focus_state =
            window.use_keyed_state(overlay_id.clone(), cx, move |_, cx| OverlayFocusState {
                focus: cx.focus_handle().tab_stop(false),
                end_focus: cx.focus_handle().tab_stop(false),
                previous_focus,
                focus_pending: modal,
            });
        let focus = focus_state.read(cx).focus.clone();
        let end_focus = focus_state.read(cx).end_focus.clone();
        if focus_state.read(cx).focus_pending {
            focus_state.update(cx, |state, _| state.focus_pending = false);
            let deferred_focus = focus.clone();
            window.defer(cx, move |window, _| deferred_focus.focus(window));
        }

        let content_id = (overlay_id.clone(), "content");
        let content = place_overlay_content(
            div().id(content_id).occlude().child(child),
            placement as u32,
        );

        let viewport = window.viewport_size();
        let mut host = div()
            .relative()
            .flex()
            .w(viewport.width)
            .h(viewport.height)
            .p(px(margin))
            .track_focus(&focus);
        host = place_overlay(host, placement as u32);

        if modal {
            let callbacks = self.callbacks;
            let session_id = self.view_id;
            let backdrop_state = focus_state.clone();
            let mut backdrop = div()
                .absolute()
                .inset_0()
                .bg(rgba(backdrop_color))
                .id((overlay_id.clone(), "backdrop"))
                .occlude();
            if dismiss_on_backdrop && dismiss_token != 0 {
                backdrop =
                    backdrop.on_click(cx.listener(move |this, event: &ClickEvent, window, cx| {
                        restore_overlay_focus(&backdrop_state, window, cx);
                        let status = invoke_click(
                            callbacks,
                            session_id,
                            dismiss_token,
                            dismiss_payload,
                            event,
                        );
                        this.after_click(status, cx);
                    }));
            }
            host = host.child(backdrop);

            let trap_state = focus_state.clone();
            host = host.on_key_down(move |event: &KeyDownEvent, window, cx| {
                let modifiers = event.keystroke.modifiers;
                if event.keystroke.key != "tab"
                    || modifiers.control
                    || modifiers.alt
                    || modifiers.platform
                    || modifiers.function
                {
                    return;
                }

                cx.stop_propagation();
                cycle_overlay_focus(&trap_state, modifiers.shift, window, cx);
            });
        }

        if dismiss_on_escape && dismiss_token != 0 {
            let callbacks = self.callbacks;
            let session_id = self.view_id;
            let escape_state = focus_state.clone();
            host = host.on_key_down(cx.listener(move |this, event: &KeyDownEvent, window, cx| {
                if event.keystroke.key != "escape" {
                    return;
                }
                cx.stop_propagation();
                restore_overlay_focus(&escape_state, window, cx);
                let position = window.mouse_position();
                let status = invoke_native_click(
                    callbacks,
                    session_id,
                    dismiss_token,
                    dismiss_payload,
                    NativeClickEvent {
                        x: position.x.into(),
                        y: position.y.into(),
                        buttons: 0,
                        modifiers: 0,
                    },
                );
                this.after_click(status, cx);
            }));
        }

        host = host.child(content);
        if modal {
            host = host.child(div().w(px(0.)).h(px(0.)).track_focus(&end_focus));
        }
        let anchored = anchored().position(point(px(0.), px(0.))).child(host);
        deferred(anchored)
            .with_priority(priority as usize)
            .into_any_element()
    }

    fn materialize_tooltip(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("Tooltip is missing its owner/key.")
                .into_any_element();
        };
        let children = snapshot.children(node);
        let trigger = self.materialize_node(children[0], snapshot, window, cx);
        let content = self.materialize_node(children[1], snapshot, window, cx);
        let configuration = TooltipConfiguration {
            placement: last_op(snapshot, node, OP_TOOLTIP_PLACEMENT).map_or(0, |op| op.a as u32),
            alignment: last_op(snapshot, node, OP_TOOLTIP_ALIGNMENT).map_or(1, |op| op.a as u32),
            show_delay_ms: last_op(snapshot, node, OP_TOOLTIP_SHOW_DELAY_MS).map_or(500, |op| op.a),
            hide_delay_ms: last_op(snapshot, node, OP_TOOLTIP_HIDE_DELAY_MS).map_or(300, |op| op.a),
            gap: last_op(snapshot, node, OP_TOOLTIP_GAP_PX)
                .map_or(8.0, |op| f32::from_bits(op.a as u32)),
            margin: last_op(snapshot, node, OP_TOOLTIP_MARGIN_PX)
                .map_or(8.0, |op| f32::from_bits(op.a as u32)),
        };
        tooltip(key, trigger, content, configuration, window, cx)
    }

    fn materialize_context_menu(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("ContextMenu is missing its owner/key.")
                .into_any_element();
        };
        let children = snapshot.children(node);
        let trigger = self.materialize_node(children[0], snapshot, window, cx);
        let content = self.materialize_node(children[1], snapshot, window, cx);
        let configuration = ContextMenuConfiguration {
            priority: last_op(snapshot, node, OP_CONTEXT_MENU_PRIORITY)
                .map_or(300, |op| op.a as u32),
            margin: last_op(snapshot, node, OP_CONTEXT_MENU_MARGIN_PX)
                .map_or(8.0, |op| f32::from_bits(op.a as u32)),
        };
        let host = apply_styles(div(), node, snapshot);
        context_menu(key, host, trigger, content, configuration, window, cx)
    }
}

struct OverlayFocusState {
    focus: FocusHandle,
    end_focus: FocusHandle,
    previous_focus: Option<WeakFocusHandle>,
    focus_pending: bool,
}

fn place_overlay(element: gpui::Div, placement: u32) -> gpui::Div {
    match placement {
        1 => element.items_start().justify_center(),
        2 => element.items_start().justify_end(),
        3 => element.items_center().justify_end(),
        4 => element.items_end().justify_end(),
        5 => element.items_end().justify_center(),
        6 => element.items_end().justify_start(),
        7 => element.items_center().justify_start(),
        8 => element.items_start().justify_start(),
        _ => element.items_center().justify_center(),
    }
}

fn place_overlay_content<T: Styled>(element: T, placement: u32) -> T {
    match placement {
        1 | 5 => element.w_full(),
        3 | 7 => element.h_full(),
        _ => element,
    }
}

fn restore_overlay_focus(
    state: &Entity<OverlayFocusState>,
    window: &mut Window,
    cx: &mut gpui::App,
) {
    if let Some(previous) = state
        .read(cx)
        .previous_focus
        .as_ref()
        .and_then(WeakFocusHandle::upgrade)
    {
        previous.focus(window);
    } else {
        window.blur();
    }
}

fn cycle_overlay_focus(
    state: &Entity<OverlayFocusState>,
    backwards: bool,
    window: &mut Window,
    cx: &mut gpui::App,
) {
    let state = state.read(cx);
    if backwards {
        window.focus_prev();
        if !state.focus.contains_focused(window, cx) {
            state.end_focus.focus(window);
            window.focus_prev();
        }
    } else {
        window.focus_next();
        if !state.focus.contains_focused(window, cx) {
            state.focus.focus(window);
            window.focus_next();
        }
    }

    if !state.focus.contains_focused(window, cx) {
        state.focus.focus(window);
    }
}

/// Materialization used by native list batch caches. It deliberately has no ManagedView Context,
/// so row clicks dispatch directly and row trees cannot create another virtualized List recursively.
pub(crate) fn materialize_snapshot_node_detached(
    node_id: u32,
    snapshot: &ValidatedSnapshot,
    session_id: u64,
    callbacks: ManagedCallbacks,
    _resources: &ResourceStore,
    list_key: &crate::resources::ResourceKey,
    item_index: usize,
) -> AnyElement {
    let node = &snapshot.nodes[node_id as usize];
    let metadata = component_metadata(node.component).unwrap();
    if matches!(
        metadata.adapter,
        NativeAdapter::Scroll
            | NativeAdapter::List
            | NativeAdapter::Input
            | NativeAdapter::Overlay
            | NativeAdapter::Tooltip
            | NativeAdapter::ContextMenu
    ) {
        return div()
            .child(
                "Retained Scroll/List/Input resources and deferred layers inside a virtualized list row are not supported.",
            )
            .into_any_element();
    }
    if metadata.adapter == NativeAdapter::Image {
        return materialize_image(node, snapshot);
    }

    let mut element = components::apply_defaults(metadata.adapter, div(), node, snapshot);
    if metadata.adapter == NativeAdapter::Text {
        element = element.child(node.data.clone());
    }
    for child in snapshot.children(node) {
        element = element.child(materialize_snapshot_node_detached(
            *child, snapshot, session_id, callbacks, _resources, list_key, item_index,
        ));
    }
    element = apply_styles(element, node, snapshot);
    element = apply_window_control_area(element, node, snapshot);
    if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
        let event_binding = last_op(snapshot, node, OP_ON_CLICK);
        let event_token = event_binding.map_or(0, |op| op.a);
        let event_payload = event_binding.map_or(0, |op| op.b);
        if event_token != 0 {
            // Node data is only a user-local element id. Virtualized rows repeat the same
            // rendered shape, so scope every stateful GPUI id by list identity + item index.
            let local_id = if node.data.is_empty() {
                node_id.to_string()
            } else {
                node.data.to_string()
            };
            let element_id = format!(
                "managed-list-{}-{}-{item_index}-{local_id}",
                list_key.owner_view, list_key.key
            );
            let element = element.id(SharedString::from(element_id)).cursor_pointer();
            let element = if let Some(op) = last_op(snapshot, node, OP_HOVER_BACKGROUND_RGBA) {
                let color = rgba(op.a as u32);
                element.hover(move |style| style.bg(color))
            } else {
                element.hover(|style| style.opacity(0.88))
            };
            return element
                .on_click(move |event: &ClickEvent, _, _| {
                    let _ = invoke_click(callbacks, session_id, event_token, event_payload, event);
                })
                .into_any_element();
        }
    }
    element.into_any_element()
}

fn invoke_click(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    event_payload: u64,
    event: &ClickEvent,
) -> i32 {
    let position = event.position();
    let modifiers = event.modifiers();
    let native_event = NativeClickEvent {
        x: position.x.into(),
        y: position.y.into(),
        buttons: if event.is_right_click() { 2 } else { 1 },
        modifiers: u32::from(modifiers.control)
            | (u32::from(modifiers.alt) << 1)
            | (u32::from(modifiers.shift) << 2)
            | (u32::from(modifiers.platform) << 3)
            | (u32::from(modifiers.function) << 4),
    };
    invoke_native_click(
        callbacks,
        session_id,
        event_token,
        event_payload,
        native_event,
    )
}

fn invoke_native_click(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    event_payload: u64,
    native_event: NativeClickEvent,
) -> i32 {
    unsafe {
        callbacks
            .click
            .expect("callbacks were validated before application startup")(
            session_id,
            event_token,
            event_payload,
            &native_event,
        )
    }
}

fn apply_styles<T: Styled>(mut element: T, node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> T {
    for op in snapshot.ops(node) {
        let value = f32::from_bits(op.a as u32);
        element = match op.code {
            OP_FLEX => element.flex().flex_row(),
            OP_V_STACK => element.flex().flex_col(),
            OP_ITEMS_CENTER => element.items_center(),
            OP_JUSTIFY_CENTER => element.justify_center(),
            OP_JUSTIFY_BETWEEN => element.justify_between(),
            OP_FLEX_GROW => element.flex_grow(),
            OP_GAP_PX => element.gap(px(value)),
            OP_PADDING_PX => element.p(px(value)),
            OP_WIDTH_PX => element.w(px(value)),
            OP_WIDTH_PERCENT => element.w(relative(value / 100.0)),
            OP_BACKGROUND_RGBA => element.bg(rgba(op.a as u32)),
            OP_HEIGHT_PX => element.h(px(value)),
            OP_HEIGHT_PERCENT => element.h(relative(value / 100.0)),
            OP_BORDER_RGBA => element.border_color(rgba(op.a as u32)),
            OP_BORDER_WIDTH_PX => element.border(px(value)),
            OP_RADIUS_PX => element.rounded(px(value)),
            OP_TEXT_RGBA => element.text_color(rgba(op.a as u32)),
            OP_FONT_SIZE_PX => element.text_size(px(value)),
            OP_HOVER_BACKGROUND_RGBA => element,
            OP_CHECKED
            | OP_ON_CLICK
            | OP_RESOURCE_OWNER
            | OP_SCROLL_AXIS
            | OP_SMOOTH_SCROLL
            | OP_SHOW_SCROLLBAR
            | OP_LIST_ITEM_COUNT
            | OP_LIST_RENDERER
            | OP_LIST_BATCH_SIZE
            | OP_LIST_OVERDRAW_PX
            | OP_LIST_ALIGNMENT
            | OP_LIST_ESTIMATED_ITEM_HEIGHT_PX
            | OP_IMAGE_OBJECT_FIT
            | OP_IMAGE_GRAYSCALE
            | OP_OVERLAY_PLACEMENT
            | OP_OVERLAY_PRIORITY
            | OP_OVERLAY_MARGIN_PX
            | OP_OVERLAY_MODAL
            | OP_OVERLAY_BACKDROP_RGBA
            | OP_OVERLAY_DISMISS_ON_BACKDROP
            | OP_OVERLAY_DISMISS_ON_ESCAPE
            | OP_OVERLAY_ON_DISMISS => element,
            _ => element,
        };
    }
    element
}

fn apply_window_control_area(
    element: gpui::Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> gpui::Div {
    let Some(operation) = last_op(snapshot, node, OP_WINDOW_CONTROL_AREA) else {
        return element;
    };
    let area = match operation.a {
        0 => WindowControlArea::Drag,
        1 => WindowControlArea::Min,
        2 => WindowControlArea::Max,
        3 => WindowControlArea::Close,
        _ => unreachable!("window control areas are validated before materialization"),
    };
    #[cfg(target_os = "windows")]
    let element = if area == WindowControlArea::Drag {
        element
    } else {
        element.font_family(windows_caption_font())
    };
    element.window_control_area(area)
}

#[cfg(target_os = "windows")]
fn windows_caption_font() -> &'static str {
    use windows::Wdk::System::SystemServices::RtlGetVersion;

    let mut version = unsafe { std::mem::zeroed() };
    let status = unsafe { RtlGetVersion(&mut version) };
    if status.is_ok() && version.dwBuildNumber >= 22000 {
        "Segoe Fluent Icons"
    } else {
        "Segoe MDL2 Assets"
    }
}

fn materialize_image(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> AnyElement {
    let mut element = img(PathBuf::from(node.data.as_ref()));
    let fit = match last_op(snapshot, node, OP_IMAGE_OBJECT_FIT).map(|op| op.a as u32) {
        Some(0) => ObjectFit::Fill,
        Some(2) => ObjectFit::Cover,
        Some(3) => ObjectFit::ScaleDown,
        Some(4) => ObjectFit::None,
        _ => ObjectFit::Contain,
    };
    let grayscale = last_op(snapshot, node, OP_IMAGE_GRAYSCALE).is_some_and(|op| op.a != 0);
    element = element
        .object_fit(fit)
        .grayscale(grayscale)
        .overflow_hidden()
        .with_fallback(|| {
            div()
                .size_full()
                .flex()
                .items_center()
                .justify_center()
                .bg(rgba(0xE2E8F0FF))
                .text_color(rgba(0x64748BFF))
                .child("Image unavailable")
                .into_any_element()
        });
    apply_styles(element, node, snapshot).into_any_element()
}

fn last_op<'a>(
    snapshot: &'a ValidatedSnapshot,
    node: &SnapshotNode,
    code: u16,
) -> Option<&'a crate::abi::OpRecord> {
    snapshot.ops(node).iter().rev().find(|op| op.code == code)
}
