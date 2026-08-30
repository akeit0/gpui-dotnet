use gpui::{
    AnyElement, ClickEvent, Context, InteractiveElement, IntoElement, ParentElement,
    StatefulInteractiveElement, Styled, div, px, relative, rgba,
};

use crate::{
    abi::NativeClickEvent,
    app_host::ManagedView,
    components,
    semantic::{
        CAPABILITY_INTERACTIVE, NativeAdapter, OP_BACKGROUND_RGBA, OP_BORDER_RGBA,
        OP_BORDER_WIDTH_PX, OP_CHECKED, OP_FLEX, OP_FLEX_GROW, OP_FONT_SIZE_PX, OP_GAP_PX,
        OP_HEIGHT_PERCENT, OP_HEIGHT_PX, OP_ITEMS_CENTER, OP_JUSTIFY_BETWEEN, OP_JUSTIFY_CENTER,
        OP_ON_CLICK, OP_PADDING_PX, OP_RADIUS_PX, OP_TEXT_RGBA, OP_V_STACK, OP_WIDTH_PERCENT,
        OP_WIDTH_PX, component_metadata,
    },
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
        let mut element = components::apply_defaults(metadata.adapter, div(), node, snapshot);

        if metadata.adapter == NativeAdapter::Text {
            element = element.child(node.data.clone());
        }

        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, cx));
        }

        element = apply_styles(element, node, snapshot);

        if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
            let event_token = snapshot
                .ops(node)
                .iter()
                .rev()
                .find(|op| op.code == OP_ON_CLICK)
                .map_or(0, |op| op.a);
            let element_id = if node.data.is_empty() {
                format!("managed-control-{node_id}").into()
            } else {
                node.data.clone()
            };

            return element
                .id(element_id)
                .cursor_pointer()
                .hover(|style| style.opacity(0.88))
                .active(|style| style.opacity(0.72))
                .on_click(cx.listener(move |this, event: &ClickEvent, _, cx| {
                    if event_token == 0 {
                        return;
                    }

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
                    let status = unsafe {
                        this.callbacks
                            .click
                            .expect("callbacks were validated before application startup")(
                            this.view_id,
                            event_token,
                            &native_event,
                        )
                    };
                    this.after_click(status, cx);
                }))
                .into_any_element();
        }

        element.into_any_element()
    }
}

fn apply_styles(
    mut element: gpui::Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> gpui::Div {
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
            OP_CHECKED | OP_ON_CLICK => element,
            _ => element,
        };
    }

    element
}
