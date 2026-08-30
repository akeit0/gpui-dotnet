use gpui::{
    AnyElement, ClickEvent, Context, InteractiveElement, IntoElement, ParentElement,
    SharedString, StatefulInteractiveElement, Styled, div, list, px, relative, rgba,
};

use crate::{
    abi::{ManagedCallbacks, NativeClickEvent},
    app_host::ManagedView,
    components,
    resources::{ResourceStore, list_configuration, resource_key},
    semantic::{
        CAPABILITY_INTERACTIVE, NativeAdapter, OP_BACKGROUND_RGBA, OP_BORDER_RGBA,
        OP_BORDER_WIDTH_PX, OP_CHECKED, OP_FLEX, OP_FLEX_GROW, OP_FONT_SIZE_PX, OP_GAP_PX,
        OP_HEIGHT_PERCENT, OP_HEIGHT_PX, OP_ITEMS_CENTER, OP_JUSTIFY_BETWEEN, OP_JUSTIFY_CENTER,
        OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE, OP_LIST_ESTIMATED_ITEM_HEIGHT_PX,
        OP_LIST_ITEM_COUNT, OP_LIST_OVERDRAW_PX, OP_LIST_RENDERER, OP_ON_CLICK,
        OP_PADDING_PX, OP_RADIUS_PX, OP_RESOURCE_OWNER, OP_SCROLL_AXIS, OP_SHOW_SCROLLBAR,
        OP_SMOOTH_SCROLL, OP_TEXT_RGBA, OP_V_STACK, OP_WIDTH_PERCENT, OP_WIDTH_PX,
        component_metadata,
    },
    scrolling::{list_overlay, scroll_overlay},
    snapshot::{SnapshotNode, ValidatedSnapshot},
};

impl ManagedView {
    pub(crate) fn materialize_node(
        &self,
        node_id: u32,
        snapshot: &ValidatedSnapshot,
        cx: &Context<Self>,
    ) -> AnyElement {
        let node = &snapshot.nodes[node_id as usize];
        let metadata = component_metadata(node.component)
            .expect("validated snapshots only contain registered components");

        match metadata.adapter {
            NativeAdapter::Scroll => self.materialize_scroll(node_id, node, snapshot, cx),
            NativeAdapter::List => self.materialize_list(node, snapshot),
            _ => self.materialize_regular(node_id, node, snapshot, cx),
        }
    }

    fn materialize_regular(
        &self,
        node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        cx: &Context<Self>,
    ) -> AnyElement {
        let metadata = component_metadata(node.component).unwrap();
        let mut element = components::apply_defaults(metadata.adapter, div(), node, snapshot);

        if metadata.adapter == NativeAdapter::Text {
            element = element.child(node.data.clone());
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, cx));
        }
        element = apply_styles(element, node, snapshot);
        if last_op(snapshot, node, OP_FLEX_GROW).is_some() {
            // A growing flex item must be allowed to shrink below its content's intrinsic height;
            // otherwise a descendant scroll viewport expands to its full content height.
            element = element.min_h_0();
        }

        if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
            let event_binding = last_op(snapshot, node, OP_ON_CLICK);
            let event_token = event_binding.map_or(0, |op| op.a);
            let event_payload = event_binding.map_or(0, |op| op.b);
            let element_id = if node.data.is_empty() {
                format!("managed-control-{node_id}").into()
            } else {
                node.data.clone()
            };
            return element
                .id(SharedString::from(element_id))
                .cursor_pointer()
                .hover(|style| style.opacity(0.88))
                .active(|style| style.opacity(0.72))
                .on_click(cx.listener(move |this, event: &ClickEvent, _, cx| {
                    if event_token == 0 {
                        return;
                    }
                    let status = invoke_click(this.callbacks, this.view_id, event_token, event_payload, event);
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
        cx: &Context<Self>,
    ) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div().child("Scroll resource is missing its owner/key.").into_any_element();
        };
        let resource = self.resources.scroll_resource(&key);
        let handle = resource.handle.clone();
        // Keep the viewport and its content as separate flex boxes. Without a non-shrinking
        // content box, Taffy can size the scroll child to the viewport and GPUI then computes a
        // maximum offset that ends before the actual final child.
        let mut content = div().flex().flex_col().flex_shrink_0();
        for child in snapshot.children(node) {
            content = content.child(self.materialize_node(*child, snapshot, cx));
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
        let show_scrollbar =
            last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let scrollable = match axis {
            1 => stateful.overflow_x_scroll().track_scroll(&handle).into_any_element(),
            2 => stateful.overflow_scroll().track_scroll(&handle).into_any_element(),
            _ => stateful.overflow_y_scroll().track_scroll(&handle).into_any_element(),
        };
        let overlay = scroll_overlay(resource, axis, smooth, show_scrollbar);
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        element.child(scrollable).child(overlay).into_any_element()
    }

    fn materialize_list(&self, node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div().child("List resource is missing its owner/key.").into_any_element();
        };
        let Some((item_count, renderer, batch_size, overdraw, alignment, estimated_item_height)) =
            list_configuration(snapshot, node)
        else {
            return div().child("List resource is missing virtualization metadata.").into_any_element();
        };
        let resource = self.resources.list_resource(
            &key,
            item_count,
            renderer,
            batch_size,
            overdraw,
            alignment,
            self.snapshot_revision,
        );
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
        let show_scrollbar =
            last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let overlay = list_overlay(
            resource,
            estimated_item_height,
            smooth,
            show_scrollbar,
        );
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        element.child(list_element).child(overlay).into_any_element()
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
    if matches!(metadata.adapter, NativeAdapter::Scroll | NativeAdapter::List) {
        return div()
            .child("Retained Scroll/List resources inside a virtualized list row are not supported.")
            .into_any_element();
    }

    let mut element = components::apply_defaults(metadata.adapter, div(), node, snapshot);
    if metadata.adapter == NativeAdapter::Text {
        element = element.child(node.data.clone());
    }
    for child in snapshot.children(node) {
        element = element.child(materialize_snapshot_node_detached(
            *child,
            snapshot,
            session_id,
            callbacks,
            _resources,
            list_key,
            item_index,
        ));
    }
    element = apply_styles(element, node, snapshot);
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
            return element
                .id(SharedString::from(element_id))
                .cursor_pointer()
                .on_click(move |event: &ClickEvent, _, _| {
                    let _ = invoke_click(
                        callbacks,
                        session_id,
                        event_token,
                        event_payload,
                        event,
                    );
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

fn apply_styles<T: Styled>(
    mut element: T,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> T {
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
            | OP_LIST_ESTIMATED_ITEM_HEIGHT_PX => element,
            _ => element,
        };
    }
    element
}

fn last_op<'a>(
    snapshot: &'a ValidatedSnapshot,
    node: &SnapshotNode,
    code: u16,
) -> Option<&'a crate::abi::OpRecord> {
    snapshot.ops(node).iter().rev().find(|op| op.code == code)
}
