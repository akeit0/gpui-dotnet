use std::path::PathBuf;

use gpui::{
    AnyElement, App, BoxShadow, ClickEvent, Context, CursorStyle, DefiniteLength, ElementId,
    Entity, ExternalPaths, FillOptions, FillRule, FocusHandle, Font, FontFallbacks, FontStyle,
    FontWeight, Hsla, InteractiveElement, IntoElement, KeyDownEvent, KeyUpEvent, Length,
    ListOffset, ListState, ModifiersChangedEvent, MouseButton, MouseDownEvent, MouseMoveEvent,
    MouseUpEvent, ObjectFit, ParentElement, PathBuilder, PathStyle, Pixels, ScrollWheelEvent,
    SharedString, StatefulInteractiveElement, Styled, StyledImage, TextAlign, TextOverflow,
    WeakFocusHandle, Window, WindowControlArea, anchored, canvas, deferred, div, img, list, point,
    px, relative, rgba,
};
use gpui_base::FocusTrapElement as _;

use crate::{
    abi::{ManagedCallbacks, NativeClickEvent, NativeControlEvent},
    app_host::ManagedView,
    components,
    context_menu::{ContextMenuConfiguration, context_menu},
    dock::dock_configuration,
    extension::{
        NativeExtensionEventEmitter, NativeExtensionRequest, declaration as extension_declaration,
        provider as extension_provider,
    },
    overlay::{OverlayKind, OverlayStack, OverlayToken},
    popover_menu::{PopoverMenuConfiguration, popover_menu},
    presentation,
    resources::{
        CollectionCursor, ListRowEventKind, ListRowEvents, ManagedListResource, ResourceStore,
        ScrollInteraction, TableSpec, input_configuration, list_configuration, resource_key,
        slider_configuration, table_configuration,
    },
    scrolling::{DEFAULT_SCROLLBAR_WIDTH, ScrollbarMetrics, list_overlay, scroll_overlay},
    semantic::{
        CAPABILITY_INTERACTIVE, EVENT_FILE_DROPPED, EVENT_HOVER, EVENT_KEY_DOWN, EVENT_KEY_UP,
        EVENT_MODIFIERS_CHANGED, EVENT_MOUSE_DOWN, EVENT_MOUSE_DOWN_OUT, EVENT_MOUSE_MOVE,
        EVENT_MOUSE_UP, EVENT_MOUSE_UP_OUT, EVENT_SCROLL_WHEEL, NativeAdapter, OP_ABSOLUTE,
        OP_ACTIVE_BACKGROUND_RGBA, OP_ACTIVE_BORDER_RGBA, OP_ACTIVE_TEXT_RGBA, OP_ALIGN_CONTENT,
        OP_ASPECT_RATIO, OP_BACKGROUND_RGBA, OP_BORDER_RGBA, OP_BORDER_STYLE, OP_BORDER_WIDTH_PX,
        OP_BOTTOM_PERCENT, OP_BOTTOM_PX, OP_CHECKED, OP_COL_END, OP_COL_END_AUTO, OP_COL_SPAN,
        OP_COL_SPAN_FULL, OP_COL_START, OP_COL_START_AUTO, OP_CONTEXT_MENU_MARGIN_PX,
        OP_CONTEXT_MENU_PRIORITY, OP_CURSOR, OP_DISABLED, OP_DISPLAY_GRID, OP_DISPLAY_NONE,
        OP_DRAWING_VIEW_BOX_ORIGIN, OP_DRAWING_VIEW_BOX_SIZE, OP_ELEMENT_OWNER, OP_FLEX,
        OP_FLEX_BASIS_PERCENT, OP_FLEX_BASIS_PX, OP_FLEX_GROW, OP_FLEX_SHRINK, OP_FLEX_WRAP,
        OP_FONT_FALLBACKS, OP_FONT_FAMILY, OP_FONT_FEATURES, OP_FONT_SIZE_PX, OP_FONT_STYLE,
        OP_FONT_WEIGHT, OP_GAP_PERCENT, OP_GAP_PX, OP_GAP_X_PERCENT, OP_GAP_X_PX, OP_GAP_Y_PERCENT,
        OP_GAP_Y_PX, OP_GRID_COLS, OP_GRID_COLS_MAX_CONTENT, OP_GRID_COLS_MIN_CONTENT,
        OP_GRID_ROWS, OP_GRID_ROWS_MAX_CONTENT, OP_GRID_ROWS_MIN_CONTENT, OP_HEIGHT_PERCENT,
        OP_HEIGHT_PX, OP_HOVER_BACKGROUND_RGBA, OP_HOVER_BORDER_RGBA, OP_HOVER_TEXT_RGBA,
        OP_IMAGE_GRAYSCALE, OP_IMAGE_OBJECT_FIT, OP_INSET_PERCENT, OP_INSET_PX, OP_ITEMS_BASELINE,
        OP_ITEMS_CENTER, OP_ITEMS_END, OP_ITEMS_START, OP_ITEMS_STRETCH, OP_JUSTIFY_BETWEEN,
        OP_JUSTIFY_CENTER, OP_JUSTIFY_END, OP_JUSTIFY_START, OP_LEFT_PERCENT, OP_LEFT_PX,
        OP_LINE_CLAMP, OP_LINE_HEIGHT_PERCENT, OP_LINE_HEIGHT_PX, OP_LINE_THROUGH,
        OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE, OP_LIST_ESTIMATED_ITEM_HEIGHT_PX,
        OP_LIST_ITEM_COUNT, OP_LIST_OVERDRAW_PX, OP_LIST_RENDERER, OP_MARGIN_BOTTOM_PERCENT,
        OP_MARGIN_BOTTOM_PX, OP_MARGIN_LEFT_PERCENT, OP_MARGIN_LEFT_PX, OP_MARGIN_PERCENT,
        OP_MARGIN_PX, OP_MARGIN_RIGHT_PERCENT, OP_MARGIN_RIGHT_PX, OP_MARGIN_TOP_PERCENT,
        OP_MARGIN_TOP_PX, OP_MARGIN_X_PERCENT, OP_MARGIN_X_PX, OP_MARGIN_Y_PERCENT, OP_MARGIN_Y_PX,
        OP_MAX_HEIGHT_PERCENT, OP_MAX_HEIGHT_PX, OP_MAX_WIDTH_PERCENT, OP_MAX_WIDTH_PX,
        OP_MIN_HEIGHT_PERCENT, OP_MIN_HEIGHT_PX, OP_MIN_WIDTH_PERCENT, OP_MIN_WIDTH_PX,
        OP_ON_CLICK, OP_ON_FILE_DROP, OP_ON_HOVER, OP_ON_KEY_DOWN, OP_ON_KEY_UP,
        OP_ON_MODIFIERS_CHANGED, OP_ON_MOUSE_DOWN, OP_ON_MOUSE_DOWN_OUT, OP_ON_MOUSE_MOVE,
        OP_ON_MOUSE_UP, OP_ON_MOUSE_UP_OUT, OP_ON_SCROLL_WHEEL, OP_OPACITY, OP_OVERFLOW_HIDDEN,
        OP_OVERFLOW_X_HIDDEN, OP_OVERFLOW_Y_HIDDEN, OP_OVERLAY_BACKDROP_RGBA,
        OP_OVERLAY_DISMISS_ON_BACKDROP, OP_OVERLAY_DISMISS_ON_ESCAPE, OP_OVERLAY_MARGIN_PX,
        OP_OVERLAY_MODAL, OP_OVERLAY_ON_DISMISS, OP_OVERLAY_PLACEMENT, OP_OVERLAY_PRIORITY,
        OP_PADDING_BOTTOM_PERCENT, OP_PADDING_BOTTOM_PX, OP_PADDING_LEFT_PERCENT,
        OP_PADDING_LEFT_PX, OP_PADDING_PERCENT, OP_PADDING_PX, OP_PADDING_RIGHT_PERCENT,
        OP_PADDING_RIGHT_PX, OP_PADDING_TOP_PERCENT, OP_PADDING_TOP_PX, OP_PADDING_X_PERCENT,
        OP_PADDING_X_PX, OP_PADDING_Y_PERCENT, OP_PADDING_Y_PX, OP_PATH_ARC_FLAGS,
        OP_PATH_ARC_RADII, OP_PATH_ARC_ROTATION, OP_PATH_ARC_TO, OP_PATH_CIRCLE_CENTER,
        OP_PATH_CIRCLE_RADIUS, OP_PATH_CLOSE, OP_PATH_CUBIC_CONTROL_A, OP_PATH_CUBIC_CONTROL_B,
        OP_PATH_CUBIC_TO, OP_PATH_DASH_PX, OP_PATH_FILL_RGBA, OP_PATH_FILL_RULE, OP_PATH_LINE_TO,
        OP_PATH_MOVE_TO, OP_PATH_QUADRATIC_CONTROL, OP_PATH_QUADRATIC_TO, OP_PATH_STROKE_RGBA,
        OP_PATH_STROKE_WIDTH_PX, OP_POPOVER_MENU_MARGIN_PX, OP_POPOVER_MENU_PRIORITY,
        OP_RADIUS_BOTTOM_LEFT_PX, OP_RADIUS_BOTTOM_RIGHT_PX, OP_RADIUS_PX, OP_RADIUS_TOP_LEFT_PX,
        OP_RADIUS_TOP_RIGHT_PX, OP_RELATIVE, OP_RESOURCE_OWNER, OP_RIGHT_PERCENT, OP_RIGHT_PX,
        OP_ROW_END, OP_ROW_END_AUTO, OP_ROW_SPAN, OP_ROW_SPAN_FULL, OP_ROW_START,
        OP_ROW_START_AUTO, OP_SCROLL_AXIS, OP_SCROLLBAR_GUTTER, OP_SCROLLBAR_WIDTH,
        OP_SELF_BASELINE, OP_SELF_CENTER, OP_SELF_END, OP_SELF_FLEX_END, OP_SELF_FLEX_START,
        OP_SELF_START, OP_SELF_STRETCH, OP_SHADOW_BLUR, OP_SHADOW_COLOR, OP_SHADOW_OFFSET,
        OP_SHADOW_SPREAD, OP_SHOW_SCROLLBAR, OP_SMOOTH_SCROLL, OP_TABLE_CELL_COLUMN,
        OP_TABLE_HEADER_BACKGROUND_RGBA, OP_TABLE_HEADER_BORDER_RGBA, OP_TABLE_HEADER_TEXT_RGBA,
        OP_TABLE_SHOW_HEADER, OP_TEXT_ALIGN, OP_TEXT_BACKGROUND, OP_TEXT_DECORATION_COLOR,
        OP_TEXT_DECORATION_NONE, OP_TEXT_DECORATION_SOLID, OP_TEXT_DECORATION_WAVY,
        OP_TEXT_ELLIPSIS, OP_TEXT_RGBA, OP_TEXT_TRUNCATE, OP_TOP_PERCENT, OP_TOP_PX, OP_UNDERLINE,
        OP_V_STACK, OP_VISIBILITY, OP_WHITE_SPACE, OP_WIDTH_PERCENT, OP_WIDTH_PX,
        OP_WINDOW_CONTROL_AREA, component_metadata,
    },
    snapshot::{SnapshotNode, ValidatedSnapshot, parse_font_fallbacks, parse_font_features},
    theme::NativeTheme,
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
            NativeAdapter::List => self.materialize_list(node, snapshot, window, cx),
            NativeAdapter::Table => self.materialize_table(node, snapshot, window, cx),
            NativeAdapter::Image => materialize_image(node, snapshot, *self.theme.borrow()),
            NativeAdapter::Drawing => materialize_drawing(node_id, snapshot),
            NativeAdapter::Dynamic => self.materialize_dynamic(node, snapshot, window, cx),
            NativeAdapter::Path => div().into_any_element(),
            NativeAdapter::Input => self.materialize_input(node, snapshot, window, cx),
            NativeAdapter::Slider => self.materialize_slider(node, snapshot, cx),
            NativeAdapter::DockArea => self.materialize_dock(node, snapshot, window, cx),
            NativeAdapter::NativeExtension => {
                self.materialize_native_extension(node, snapshot, window, cx)
            }
            NativeAdapter::Overlay => self.materialize_overlay(node, snapshot, window, cx),
            NativeAdapter::Tooltip => self.materialize_tooltip(node, snapshot, window, cx),
            NativeAdapter::ContextMenu => self.materialize_context_menu(node, snapshot, window, cx),
            NativeAdapter::PopoverMenu => self.materialize_popover_menu(node, snapshot, window, cx),
            _ => self.materialize_regular(node_id, node, snapshot, window, cx),
        }
    }

    fn materialize_dock(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(configuration) = dock_configuration(snapshot, node) else {
            return div()
                .child("Dock resource is missing its owner or layout configuration.")
                .into_any_element();
        };
        let area =
            self.resources
                .dock_resource(&configuration, cx.entity().downgrade(), window, cx);
        let element = div().size_full().child(area);
        apply_styles(element, node, snapshot).into_any_element()
    }

    fn materialize_native_extension(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(declaration) = extension_declaration(node) else {
            return div()
                .child("Native extension declaration is malformed.")
                .into_any_element();
        };
        let Some(owner_view) = last_op(snapshot, node, OP_RESOURCE_OWNER)
            .map(|operation| operation.a as u32)
            .filter(|owner| *owner != 0)
        else {
            return div()
                .child("Native extension declaration is missing its owner.")
                .into_any_element();
        };
        let Some(provider) = extension_provider(declaration.extension_id.as_ref()) else {
            return div()
                .child(format!(
                    "Native extension '{}' is not installed in this host.",
                    declaration.extension_id
                ))
                .into_any_element();
        };
        let descriptor = provider.descriptor();
        if descriptor.version != declaration.version
            || descriptor.schema_hash != declaration.schema_hash
        {
            return div()
                .child(format!(
                    "Native extension '{}' has an incompatible schema.",
                    declaration.extension_id
                ))
                .into_any_element();
        }

        let children = snapshot
            .children(node)
            .iter()
            .map(|child| self.materialize_node(*child, snapshot, window, cx))
            .collect();
        let resource_key = declaration.resource_key(owner_view);
        let request = NativeExtensionRequest {
            commands: self.resources.extensions().take_commands(&resource_key),
            events: NativeExtensionEventEmitter::new(self.view_id, owner_view, self.callbacks),
            resource_key,
            configuration: declaration.configuration,
            children,
        };
        match provider.materialize(request, self.resources.extensions(), window, cx) {
            Ok(content) => {
                apply_styles(div().size_full().child(content), node, snapshot).into_any_element()
            }
            Err(error) => div().child(error).into_any_element(),
        }
    }

    fn materialize_dynamic(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        self.materialize_node(snapshot.children(node)[0], snapshot, window, cx)
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
        match metadata.adapter {
            NativeAdapter::Button => {
                return self.materialize_button(node_id, node, snapshot, window, cx);
            }
            NativeAdapter::Checkbox => {
                return self.materialize_checkbox(node_id, node, snapshot, window, cx);
            }
            NativeAdapter::Radio => {
                return self.materialize_radio(node_id, node, snapshot, window, cx);
            }
            _ => {}
        }

        let theme = *self.theme.borrow();
        let mut element = components::apply_defaults(metadata.adapter, div(), theme);

        if metadata.adapter == NativeAdapter::Text {
            element = element.child(node.data.clone());
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, window, cx));
        }
        if last_op(snapshot, node, OP_FLEX_GROW).is_some() {
            // A growing flex item must be allowed to shrink below its content's intrinsic
            // size; otherwise a descendant scroll viewport expands to its full content
            // height, or a growing row item forces the row wider than its parent when a
            // long text child reports a wide max-content width.
            element = element.min_h_0().min_w_0();
        }
        // Explicit pixel or percentage minima override these intrinsic-size defaults.
        element = apply_styles(element, node, snapshot);
        element = apply_window_control_area(element, node, snapshot);

        if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
            let event_binding = last_op(snapshot, node, OP_ON_CLICK);
            let event_token = event_binding.map_or(0, |op| op.a);
            let event_payload = event_binding.map_or(0, |op| op.b);
            let element_id = interactive_element_id(node_id, node, snapshot);
            let element = element.id(element_id);
            let element = if use_default_cursor(node, snapshot) {
                element.cursor_pointer()
            } else {
                element
            };
            let element = apply_interaction_styles(element, node, snapshot, theme);
            let bindings = key_mouse_bindings(node, snapshot);
            let element = attach_key_mouse(element, &bindings, cx);
            let element = attach_hover(element, &bindings, cx);
            return element
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

        let bindings = key_mouse_bindings(node, snapshot);
        let element = attach_key_mouse(element, &bindings, cx);
        if let Some(key) = crate::resources::focus_target_key(snapshot, node) {
            let tab_stop = last_op(snapshot, node, crate::semantic::OP_FOCUS_TAB_STOP)
                .is_some_and(|op| op.a != 0);
            let focus = self.resources.focus_target(&key, tab_stop, window, cx);
            let element = element.id(&focus).track_focus(&focus);
            let element = attach_hover(element, &bindings, cx);
            return presentation::focus_ring(element, theme.border_focused).into_any_element();
        }
        if bindings.hover != 0 {
            // Hover tracking needs stable element state, which plain Divs lack:
            // wrap with the deterministic node id for the stateful listener only.
            let element = attach_hover(
                element.id(interactive_element_id(node_id, node, snapshot)),
                &bindings,
                cx,
            );
            return element.into_any_element();
        }
        element.into_any_element()
    }

    fn materialize_button(
        &self,
        node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let theme = *self.theme.borrow();
        let disabled = components::has_u32_flag(node, snapshot, OP_DISABLED);
        let mut element = components::button(
            interactive_element_id(node_id, node, snapshot),
            disabled,
            theme,
        );
        element = crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
        if let Some(label) = accessibility_label(node, snapshot) {
            element = element.accessibility_label(label);
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, window, cx));
        }
        element = apply_styles(element, node, snapshot);
        element = apply_window_control_area(element, node, snapshot);
        if !disabled {
            // An explicit Cursor operation takes precedence over the pointing-hand default.
            element = if use_default_cursor(node, snapshot) {
                element.cursor_pointer()
            } else {
                element
            };
            element = apply_interaction_styles(element, node, snapshot, theme);
        }

        let element = presentation::disabled(element, disabled);
        let bindings = key_mouse_bindings(node, snapshot);
        let element = attach_key_mouse(element, &bindings, cx);
        let element = attach_hover(element, &bindings, cx);
        let Some((event_token, event_payload)) = click_binding(node, snapshot) else {
            return element.into_any_element();
        };
        element
            .on_click(cx.listener(move |this, event: &ClickEvent, _, cx| {
                let status = invoke_click(
                    this.callbacks,
                    this.view_id,
                    event_token,
                    event_payload,
                    event,
                );
                this.after_click(status, cx);
            }))
            .into_any_element()
    }

    fn materialize_checkbox(
        &self,
        node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let theme = *self.theme.borrow();
        let checked = components::has_u32_flag(node, snapshot, OP_CHECKED);
        let disabled = components::has_u32_flag(node, snapshot, OP_DISABLED);
        let mut element = components::checkbox(
            interactive_element_id(node_id, node, snapshot),
            checked,
            disabled,
            theme,
        );
        element = crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
        if let Some(label) = accessibility_label(node, snapshot) {
            element = element.accessibility_label(label);
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, window, cx));
        }
        element = apply_styles(element, node, snapshot);
        if !disabled {
            // An explicit Cursor operation takes precedence over the pointing-hand default.
            element = if use_default_cursor(node, snapshot) {
                element.cursor_pointer()
            } else {
                element
            };
            element = apply_interaction_styles(element, node, snapshot, theme);
        }

        let element = presentation::disabled(element, disabled);
        let bindings = key_mouse_bindings(node, snapshot);
        let element = attach_key_mouse(element, &bindings, cx);
        let element = attach_hover(element, &bindings, cx);
        let Some((event_token, event_payload)) = click_binding(node, snapshot) else {
            return element.into_any_element();
        };
        let listener = cx.listener(move |this, event: &ClickEvent, _, cx| {
            let status = invoke_click(
                this.callbacks,
                this.view_id,
                event_token,
                event_payload,
                event,
            );
            this.after_click(status, cx);
        });
        element
            .on_change(move |_, event, window, cx| listener(event, window, cx))
            .into_any_element()
    }

    fn materialize_radio(
        &self,
        node_id: u32,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let theme = *self.theme.borrow();
        let checked = components::has_u32_flag(node, snapshot, OP_CHECKED);
        let disabled = components::has_u32_flag(node, snapshot, OP_DISABLED);
        let mut element = components::radio(
            interactive_element_id(node_id, node, snapshot),
            checked,
            disabled,
            theme,
        );
        element = crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
        if let Some(label) = accessibility_label(node, snapshot) {
            element = element.accessibility_label(label);
        }
        for child in snapshot.children(node) {
            element = element.child(self.materialize_node(*child, snapshot, window, cx));
        }
        element = apply_styles(element, node, snapshot);
        if !disabled {
            // An explicit Cursor operation takes precedence over the pointing-hand default.
            element = if use_default_cursor(node, snapshot) {
                element.cursor_pointer()
            } else {
                element
            };
            element = apply_interaction_styles(element, node, snapshot, theme);
        }

        let element = presentation::disabled(element, disabled);
        let bindings = key_mouse_bindings(node, snapshot);
        let element = attach_key_mouse(element, &bindings, cx);
        let element = attach_hover(element, &bindings, cx);
        let Some((event_token, event_payload)) = click_binding(node, snapshot) else {
            return element.into_any_element();
        };
        let listener = cx.listener(move |this, event: &ClickEvent, _, cx| {
            let status = invoke_click(
                this.callbacks,
                this.view_id,
                event_token,
                event_payload,
                event,
            );
            this.after_click(status, cx);
        });
        element
            .on_change(move |_, event, window, cx| listener(event, window, cx))
            .into_any_element()
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
            .flex_grow(1.0)
            .min_h_0()
            .min_w_0()
            .child(content)
            .id(SharedString::from(element_id));
        let axis = last_op(snapshot, node, OP_SCROLL_AXIS).map_or(0, |op| op.a as u32);
        let smooth = last_op(snapshot, node, OP_SMOOTH_SCROLL).is_none_or(|op| op.a != 0);
        let show_scrollbar = last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let metrics = scrollbar_metrics(snapshot, node);
        // Gutter mode reserves the bar's width inside the scrollport: content lays out within
        // the right padding while the track paints over the reserved padding area.
        let stateful = if metrics.gutter > px(0.) {
            stateful.pr(metrics.gutter)
        } else {
            stateful
        };
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
        let overlay = scroll_overlay(
            resource,
            axis,
            smooth,
            show_scrollbar,
            metrics,
            collection_focus_id("managed-scroll-scrollbar", &key),
        );
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        element.child(scrollable).child(overlay).into_any_element()
    }

    fn materialize_list(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
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
        resource.borrow_mut().begin_frame();
        let state = resource.borrow().state.clone();
        let focus_state = window.use_keyed_state(
            collection_focus_id("managed-list-focus", &key),
            cx,
            |_, cx| CollectionFocusState {
                focus: cx.focus_handle().tab_stop(true),
            },
        );
        let focus = focus_state.read(cx).focus.clone();
        let keyboard_cursor = resource.borrow().cursor.clone();
        let keyboard_epoch = keyboard_cursor.epoch();
        let keyboard_resource = resource.clone();
        let keyboard_focus = focus.clone();
        let row_cursor = keyboard_cursor.clone();
        let row_focus = focus.clone();
        let keyboard_list = state.clone();
        let keyboard_interaction = resource.borrow().interaction.clone();
        let item_count = configuration.item_count;
        let resources = self.resources.clone();
        let row_scope = key.clone();
        let row_resource = resource.clone();
        let menu_token = last_op(
            snapshot,
            node,
            crate::semantic::OP_LIST_ON_CONTEXT_MENU_REQUESTED,
        )
        .map_or(0, |op| op.a);
        let list_element = list(state, move |index, _window, _cx| {
            let element = row_resource
                .borrow_mut()
                .render_item(index, &resources, &row_scope);
            CollectionRow::new(element, row_cursor.clone(), row_focus.clone(), index)
                .with_row_events(row_resource.borrow().cached_row_events(index))
                .with_context_menu(
                    menu_token,
                    resources.row_menus.clone(),
                    row_resource.clone(),
                )
                .into_any_element()
        })
        .flex_grow(1.0)
        .min_h_0()
        .min_w_0();
        let smooth = last_op(snapshot, node, OP_SMOOTH_SCROLL).is_none_or(|op| op.a != 0);
        let show_scrollbar = last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let overlay = list_overlay(
            resource,
            smooth,
            show_scrollbar,
            configuration.scrollbar,
            collection_focus_id("managed-list-scrollbar", &key),
        );
        let focus_color = self.theme.borrow().border_focused;
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        let mut host =
            presentation::focus_ring(element.id(&focus).track_focus(&focus), focus_color)
                .key_context("GpuiDotnetList")
                .on_key_down(move |event, window, cx| {
                    if keyboard_cursor.epoch() != keyboard_epoch {
                        return;
                    }
                    if handle_collection_row_event_key(
                        event,
                        window,
                        cx,
                        &keyboard_focus,
                        &keyboard_resource,
                    ) {
                        return;
                    }
                    handle_collection_key_down(
                        event,
                        window,
                        cx,
                        &keyboard_cursor,
                        &keyboard_list,
                        &keyboard_interaction,
                        item_count,
                    );
                });
        if configuration.scrollbar.gutter > px(0.) {
            // Reserve the bar's width: the virtualized content excludes the gutter, so rows
            // never extend under the scrollbar.
            host = host.child(
                div()
                    .flex()
                    .flex_row()
                    .flex_grow(1.0)
                    .min_h_0()
                    .min_w_0()
                    .child(list_element)
                    .child(gutter_spacer(configuration.scrollbar.gutter)),
            );
        } else {
            host = host.child(list_element);
        }
        host.child(overlay).into_any_element()
    }

    /// A table is a virtualized list whose rows are reconciled against declared columns. The
    /// row engine, its commands, and its batch cache are exactly the list machinery; columns
    /// are declarative IR that only changes how the header strip and row cells are laid out.
    fn materialize_table(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some((key, spec)) = table_configuration(snapshot, node) else {
            return div()
                .child("Table resource is missing its owner/key or declares malformed columns.")
                .into_any_element();
        };
        let Some(configuration) = list_configuration(snapshot, node) else {
            return div()
                .child("Table resource is missing virtualization metadata.")
                .into_any_element();
        };
        let resource = self
            .resources
            .list_resource(&key, &configuration, self.snapshot_revision);
        self.resources
            .bind_table_spec(&key, spec.clone(), &resource);
        resource.borrow_mut().begin_frame();
        let state = resource.borrow().state.clone();
        let focus_state = window.use_keyed_state(
            collection_focus_id("managed-table-focus", &key),
            cx,
            |_, cx| CollectionFocusState {
                focus: cx.focus_handle().tab_stop(true),
            },
        );
        let focus = focus_state.read(cx).focus.clone();
        let keyboard_cursor = resource.borrow().cursor.clone();
        let keyboard_epoch = keyboard_cursor.epoch();
        let keyboard_resource = resource.clone();
        let keyboard_focus = focus.clone();
        let row_cursor = keyboard_cursor.clone();
        let row_focus = focus.clone();
        let keyboard_list = state.clone();
        let keyboard_interaction = resource.borrow().interaction.clone();
        let item_count = configuration.item_count;
        let resources = self.resources.clone();
        let row_scope = key.clone();
        let row_resource = resource.clone();
        let menu_token = last_op(
            snapshot,
            node,
            crate::semantic::OP_LIST_ON_CONTEXT_MENU_REQUESTED,
        )
        .map_or(0, |op| op.a);
        let list_element = list(state, move |index, _window, _cx| {
            let element = row_resource
                .borrow_mut()
                .render_item(index, &resources, &row_scope);
            CollectionRow::new(element, row_cursor.clone(), row_focus.clone(), index)
                .with_row_events(row_resource.borrow().cached_row_events(index))
                .with_context_menu(
                    menu_token,
                    resources.row_menus.clone(),
                    row_resource.clone(),
                )
                .into_any_element()
        })
        .flex_grow(1.0)
        .min_h_0()
        .min_w_0();
        let smooth = last_op(snapshot, node, OP_SMOOTH_SCROLL).is_none_or(|op| op.a != 0);
        let show_scrollbar = last_op(snapshot, node, OP_SHOW_SCROLLBAR).is_none_or(|op| op.a != 0);
        let overlay = list_overlay(
            resource,
            smooth,
            show_scrollbar,
            configuration.scrollbar,
            collection_focus_id("managed-table-scrollbar", &key),
        );
        let theme = *self.theme.borrow();
        let mut element = apply_styles(div().relative().flex().flex_col(), node, snapshot);
        element = element.min_h_0().min_w_0();
        let mut host =
            presentation::focus_ring(element.id(&focus).track_focus(&focus), theme.border_focused)
                .key_context("GpuiDotnetTable")
                .on_key_down(move |event, window, cx| {
                    if keyboard_cursor.epoch() != keyboard_epoch
                        || !keyboard_focus.is_focused(window)
                    {
                        return;
                    }
                    if handle_collection_row_event_key(
                        event,
                        window,
                        cx,
                        &keyboard_focus,
                        &keyboard_resource,
                    ) {
                        return;
                    }
                    handle_collection_key_down(
                        event,
                        window,
                        cx,
                        &keyboard_cursor,
                        &keyboard_list,
                        &keyboard_interaction,
                        item_count,
                    );
                });
        let show_header = last_op(snapshot, node, OP_TABLE_SHOW_HEADER).is_none_or(|op| op.a != 0);
        if show_header {
            let header =
                table_header_strip(&spec, theme, configuration.scrollbar.gutter, |index| {
                    snapshot
                        .children(node)
                        .get(index)
                        .map(|&child| self.materialize_node(child, snapshot, window, cx))
                });
            host = host.child(apply_table_header_styles(header, node, snapshot));
        }
        if configuration.scrollbar.gutter > px(0.) {
            host = host.child(
                div()
                    .flex()
                    .flex_row()
                    .flex_grow(1.0)
                    .min_h_0()
                    .min_w_0()
                    .child(list_element)
                    .child(gutter_spacer(configuration.scrollbar.gutter)),
            );
        } else {
            host = host.child(list_element);
        }
        host.child(overlay).into_any_element()
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
        let theme = *self.theme.borrow();
        let input = self.resources.input_resource(&configuration, window, cx);
        let element = div()
            .w(px(280.))
            .h(px(38.))
            .min_w_0()
            .px(px(10.))
            .flex()
            .items_center()
            .overflow_hidden()
            .rounded(px(6.))
            .border(px(1.))
            .border_color(rgba(theme.border))
            .bg(rgba(theme.element_background))
            .child(input);
        presentation::disabled(apply_styles(element, node, snapshot), disabled).into_any_element()
    }

    fn materialize_slider(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(configuration) = slider_configuration(snapshot, node) else {
            return div()
                .child("Slider resource is missing its owner or configuration.")
                .into_any_element();
        };
        let disabled = configuration.disabled;
        let slider = self.resources.slider_resource(&configuration, cx);
        let element = div().w_full().h(px(24.)).min_w_0().child(slider);
        presentation::disabled(apply_styles(element, node, snapshot), disabled).into_any_element()
    }

    fn materialize_overlay(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
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
                previous_focus,
                focus_pending: modal,
            });
        let focus = focus_state.read(cx).focus.clone();
        let overlay_stack = self.overlay_stack.clone();
        let overlay_token = overlay_stack.register(
            key,
            OverlayKind::Overlay,
            priority as u32,
            modal || (dismiss_token != 0 && dismiss_on_escape),
        );
        if focus_state.read(cx).focus_pending {
            let deferred_focus = focus.clone();
            let deferred_focus_state = overlay_stack.clone();
            let deferred_focus_token = overlay_token.clone();
            let pending_focus_state = focus_state.clone();
            window.defer(cx, move |window, cx| {
                if !pending_focus_state.update(cx, |state, _| {
                    state.take_pending_focus(&deferred_focus_state, &deferred_focus_token)
                }) {
                    return;
                }
                deferred_focus.focus(window, cx);
                window.focus_next(cx);
                if !deferred_focus.contains_focused(window, cx) {
                    deferred_focus.focus(window, cx);
                }
            });
        }

        let child_id = snapshot.children(node)[0];
        let child = self.materialize_node(child_id, snapshot, window, cx);
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
            let backdrop_stack = overlay_stack.clone();
            let backdrop_token = overlay_token.clone();
            let mut backdrop = div()
                .absolute()
                .inset_0()
                .bg(rgba(backdrop_color))
                .id((overlay_id.clone(), "backdrop"))
                .occlude();
            if dismiss_on_backdrop && dismiss_token != 0 {
                backdrop =
                    backdrop.on_click(cx.listener(move |this, event: &ClickEvent, window, cx| {
                        if !backdrop_stack.is_topmost(&backdrop_token) {
                            return;
                        }
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
        }

        if dismiss_on_escape && dismiss_token != 0 {
            let callbacks = self.callbacks;
            let session_id = self.view_id;
            let escape_state = focus_state.clone();
            let escape_stack = overlay_stack;
            let escape_token = overlay_token;
            host = host.on_key_down(cx.listener(move |this, event: &KeyDownEvent, window, cx| {
                if event.keystroke.key != "escape" {
                    return;
                }
                if !escape_stack.is_topmost(&escape_token) {
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

        let mut bindings = key_mouse_bindings(node, snapshot);
        bindings.isolate_shortcuts |= modal;
        host = attach_key_mouse(host, &bindings, cx).child(content);
        let host = if modal {
            host.focus_trap((overlay_id, "focus-trap"), &focus)
                .into_any_element()
        } else {
            host.into_any_element()
        };
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
        let _tooltip_token =
            self.overlay_stack
                .register(key.clone(), OverlayKind::Tooltip, 200, false);
        let children = snapshot.children(node);
        let trigger = self.materialize_node(children[0], snapshot, window, cx);
        let content = self.materialize_node(children[1], snapshot, window, cx);
        if let Some(anchor) = last_op(snapshot, node, crate::semantic::OP_TOOLTIP_ROW_ANCHOR) {
            return crate::row_tooltip::tooltip(
                self.resources.row_tooltips.clone(),
                anchor.a,
                key.owner_view,
                content,
                window,
                cx,
            );
        }
        tooltip(
            key,
            trigger,
            content,
            TooltipConfiguration::from_snapshot(snapshot, node),
            window,
            cx,
        )
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
        let overlay_token = self.overlay_stack.register(
            key.clone(),
            OverlayKind::ContextMenu,
            last_op(snapshot, node, OP_CONTEXT_MENU_PRIORITY).map_or(300, |op| op.a as u32),
            false,
        );
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
        context_menu(
            key,
            host,
            trigger,
            content,
            configuration,
            self.overlay_stack.clone(),
            overlay_token,
            last_op(snapshot, node, crate::semantic::OP_CONTEXT_MENU_ROW_ANCHOR)
                .map(|op| (self.resources.row_menus.clone(), op.a)),
            window,
            cx,
        )
    }

    fn materialize_popover_menu(
        &self,
        node: &SnapshotNode,
        snapshot: &ValidatedSnapshot,
        window: &mut Window,
        cx: &mut Context<Self>,
    ) -> AnyElement {
        let Some(key) = resource_key(snapshot, node) else {
            return div()
                .child("PopoverMenu is missing its owner/key.")
                .into_any_element();
        };
        let overlay_token = self.overlay_stack.register(
            key.clone(),
            OverlayKind::PopoverMenu,
            last_op(snapshot, node, OP_POPOVER_MENU_PRIORITY).map_or(300, |op| op.a as u32),
            false,
        );
        let children = snapshot.children(node);
        let trigger = self.materialize_node(children[0], snapshot, window, cx);
        let content = self.materialize_node(children[1], snapshot, window, cx);
        let configuration = PopoverMenuConfiguration {
            priority: last_op(snapshot, node, OP_POPOVER_MENU_PRIORITY)
                .map_or(300, |op| op.a as u32),
            margin: last_op(snapshot, node, OP_POPOVER_MENU_MARGIN_PX)
                .map_or(8.0, |op| f32::from_bits(op.a as u32)),
        };
        let host = apply_styles(div(), node, snapshot);
        popover_menu(
            key,
            host,
            trigger,
            content,
            configuration,
            self.popover_menus.clone(),
            self.overlay_stack.clone(),
            overlay_token,
            window,
            cx,
        )
    }
}

struct OverlayFocusState {
    focus: FocusHandle,
    previous_focus: Option<WeakFocusHandle>,
    focus_pending: bool,
}

impl OverlayFocusState {
    fn take_pending_focus(&mut self, stack: &OverlayStack, token: &OverlayToken) -> bool {
        if !self.focus_pending || !stack.is_topmost(token) {
            return false;
        }
        // A superseded frame leaves the request pending for its successor.
        self.focus_pending = false;
        true
    }
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
        previous.focus(window, cx);
    } else {
        window.blur(cx);
    }
}

/// Materialization used by native list batch caches. It deliberately has no ManagedView Context,
/// so row clicks dispatch directly and row trees cannot create another virtualized List recursively.
/// `item_id` is the stable model identity declared on the row root via OP_LIST_ITEM_ID; it keeps
/// stateful element identity stable across splices and supplies the default event payload for
/// rows that do not bake an explicit payload. A missing ID falls back to the positional index.
pub(crate) fn materialize_snapshot_node_detached(
    node_id: u32,
    snapshot: &ValidatedSnapshot,
    session_id: u64,
    callbacks: ManagedCallbacks,
    resources: &ResourceStore,
    list_key: &crate::resources::ResourceKey,
    item_index: usize,
    item_id: Option<u64>,
) -> AnyElement {
    let child = materialize_row_node(
        node_id, snapshot, session_id, callbacks, resources, list_key, item_index, item_id,
    );
    let node = &snapshot.nodes[node_id as usize];
    if item_id.is_some()
        && last_op(snapshot, node, crate::semantic::OP_ROW_TOOLTIP_TARGET)
            .is_some_and(|op| op.a != 0)
    {
        resources.row_tooltip_target(list_key, item_index, node_id, child)
    } else {
        child
    }
}

fn materialize_row_node(
    node_id: u32,
    snapshot: &ValidatedSnapshot,
    session_id: u64,
    callbacks: ManagedCallbacks,
    resources: &ResourceStore,
    list_key: &crate::resources::ResourceKey,
    item_index: usize,
    item_id: Option<u64>,
) -> AnyElement {
    let node = &snapshot.nodes[node_id as usize];
    let metadata = component_metadata(node.component).unwrap();
    if matches!(
        metadata.adapter,
        NativeAdapter::Scroll
            | NativeAdapter::List
            | NativeAdapter::Table
            | NativeAdapter::Input
            | NativeAdapter::Slider
            | NativeAdapter::Overlay
            | NativeAdapter::Tooltip
            | NativeAdapter::ContextMenu
            | NativeAdapter::PopoverMenu
            | NativeAdapter::DockArea
            | NativeAdapter::DockSplit
            | NativeAdapter::DockTabs
            | NativeAdapter::DockPanel
            | NativeAdapter::DockRegion
            | NativeAdapter::NativeExtension
    ) {
        return div()
            .child(
                "Retained resources, Dock declarations, and deferred layers inside a virtualized list row are not supported.",
            )
            .into_any_element();
    }
    if metadata.adapter == NativeAdapter::Image {
        return materialize_image(node, snapshot, resources.theme());
    }
    if metadata.adapter == NativeAdapter::Drawing {
        return materialize_drawing(node_id, snapshot);
    }
    // Observer key/mouse/modifier/hover/move/wheel/drop bindings need focus and bubbling
    // through a mounted View; virtual rows are element-only snapshots without View lifetime.
    if last_op(snapshot, node, OP_ON_KEY_DOWN).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, crate::semantic::OP_FOCUS_TARGET).is_some()
        || last_op(snapshot, node, crate::semantic::OP_ON_SHORTCUT).is_some()
        || last_op(snapshot, node, crate::semantic::OP_ISOLATE_SHORTCUTS)
            .is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_KEY_UP).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MOUSE_DOWN).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MOUSE_UP).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MODIFIERS_CHANGED).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_HOVER).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MOUSE_DOWN_OUT).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MOUSE_UP_OUT).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_MOUSE_MOVE).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_SCROLL_WHEEL).is_some_and(|op| op.a != 0)
        || last_op(snapshot, node, OP_ON_FILE_DROP).is_some_and(|op| op.a != 0)
    {
        return div()
            .child("Focus targets, shortcuts, and key/mouse observer events inside a virtualized list row are not supported.")
            .into_any_element();
    }

    let theme = resources.theme();
    if let Some(control) = materialize_detached_foundation_control(
        metadata.adapter,
        node,
        snapshot,
        session_id,
        callbacks,
        resources,
        list_key,
        item_index,
        item_id,
        theme,
    ) {
        return control;
    }
    let mut element = components::apply_defaults(metadata.adapter, div(), theme);
    if metadata.adapter == NativeAdapter::Text {
        element = element.child(node.data.clone());
    }
    for child in snapshot.children(node) {
        element = element.child(materialize_snapshot_node_detached(
            *child, snapshot, session_id, callbacks, resources, list_key, item_index, item_id,
        ));
    }
    element = apply_styles(element, node, snapshot);
    element = apply_window_control_area(element, node, snapshot);
    element = apply_table_cell_layout(element, node, snapshot, resources, list_key);
    if metadata.capabilities & CAPABILITY_INTERACTIVE != 0 {
        let event_binding = last_op(snapshot, node, OP_ON_CLICK);
        let event_token = event_binding.map_or(0, |op| op.a);
        let event_payload = event_binding.map_or(0, |op| op.b);
        // Rows without an explicit payload deliver the stable model ID so handlers survive
        // splices; payload 0 means "unset" in the ON_CLICK encoding.
        let event_payload = match (event_payload, item_id) {
            (0, Some(id)) => id,
            (payload, _) => payload,
        };
        if event_token != 0 {
            // GPUI element ids are path-scoped state keys. Interactive row nodes use a
            // structured NamedInteger id — a fixed namespace plus a deterministic hash of the
            // list identity, row identity, and node identity — so the virtualized-row hot path
            // performs no string formatting or allocation. The stable model ID is preferred so
            // state survives splices; without one, the positional index keeps prior behavior.
            let state_id = row_state_id(list_key, item_index, item_id, &node.data);
            let element = element.id(("managed-list-row", state_id));
            let element = if use_default_cursor(node, snapshot) {
                element.cursor_pointer()
            } else {
                element
            };
            let element = apply_interaction_styles(element, node, snapshot, theme);
            return element
                .on_click(move |event: &ClickEvent, _, _| {
                    crate::app_host::after_detached_callback(
                        session_id,
                        invoke_click(callbacks, session_id, event_token, event_payload, event),
                    );
                })
                .into_any_element();
        }
    }
    element.into_any_element()
}

#[allow(clippy::too_many_arguments)]
fn materialize_detached_foundation_control(
    adapter: NativeAdapter,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    session_id: u64,
    callbacks: ManagedCallbacks,
    resources: &ResourceStore,
    list_key: &crate::resources::ResourceKey,
    item_index: usize,
    item_id: Option<u64>,
    theme: NativeTheme,
) -> Option<AnyElement> {
    if !matches!(
        adapter,
        NativeAdapter::Button | NativeAdapter::Checkbox | NativeAdapter::Radio
    ) {
        return None;
    }

    let children = snapshot
        .children(node)
        .iter()
        .map(|child| {
            materialize_snapshot_node_detached(
                *child, snapshot, session_id, callbacks, resources, list_key, item_index, item_id,
            )
        })
        .collect::<Vec<_>>();
    let state_id = row_state_id(list_key, item_index, item_id, &node.data);
    let element_id: ElementId = ("managed-list-row", state_id).into();
    let disabled = components::has_u32_flag(node, snapshot, OP_DISABLED);
    let label = accessibility_label(node, snapshot);
    let binding = click_binding(node, snapshot).map(|(token, payload)| {
        let payload = if payload == 0 {
            item_id.unwrap_or(0)
        } else {
            payload
        };
        (token, payload)
    });

    Some(match adapter {
        NativeAdapter::Button => {
            let mut element = components::button(element_id, disabled, theme);
            element =
                crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
            if let Some(label) = label {
                element = element.accessibility_label(label);
            }
            element = element.children(children);
            element = apply_styles(element, node, snapshot);
            element = apply_window_control_area(element, node, snapshot);
            if !disabled {
                element = if use_default_cursor(node, snapshot) {
                    element.cursor_pointer()
                } else {
                    element
                };
                element = apply_interaction_styles(element, node, snapshot, theme);
            }
            if let Some((event_token, event_payload)) = binding {
                element = element.on_click(move |event: &ClickEvent, _, _| {
                    crate::app_host::after_detached_callback(
                        session_id,
                        invoke_click(callbacks, session_id, event_token, event_payload, event),
                    );
                });
            }
            element.into_any_element()
        }
        NativeAdapter::Checkbox => {
            let checked = components::has_u32_flag(node, snapshot, OP_CHECKED);
            let mut element = components::checkbox(element_id, checked, disabled, theme);
            element =
                crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
            if let Some(label) = label {
                element = element.accessibility_label(label);
            }
            element = element.children(children);
            element = apply_styles(element, node, snapshot);
            if !disabled {
                element = if use_default_cursor(node, snapshot) {
                    element.cursor_pointer()
                } else {
                    element
                };
                element = apply_interaction_styles(element, node, snapshot, theme);
            }
            if let Some((event_token, event_payload)) = binding {
                element = element.on_change(move |_, event, _, _| {
                    crate::app_host::after_detached_callback(
                        session_id,
                        invoke_click(callbacks, session_id, event_token, event_payload, event),
                    );
                });
            }
            element.into_any_element()
        }
        NativeAdapter::Radio => {
            let checked = components::has_u32_flag(node, snapshot, OP_CHECKED);
            let mut element = components::radio(element_id, checked, disabled, theme);
            element =
                crate::accessibility::Accessibility::from_snapshot(node, snapshot).apply(element);
            if let Some(label) = label {
                element = element.accessibility_label(label);
            }
            element = element.children(children);
            element = apply_styles(element, node, snapshot);
            if !disabled {
                element = if use_default_cursor(node, snapshot) {
                    element.cursor_pointer()
                } else {
                    element
                };
                element = apply_interaction_styles(element, node, snapshot, theme);
            }
            if let Some((event_token, event_payload)) = binding {
                element = element.on_change(move |_, event, _, _| {
                    crate::app_host::after_detached_callback(
                        session_id,
                        invoke_click(callbacks, session_id, event_token, event_payload, event),
                    );
                });
            }
            element.into_any_element()
        }
        _ => unreachable!(),
    })
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

fn click_binding(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> Option<(u64, u64)> {
    let binding = last_op(snapshot, node, OP_ON_CLICK)?;
    (binding.a != 0).then_some((binding.a, binding.b))
}

/// Interactive elements default to the pointing-hand cursor; an explicit Cursor
/// operation takes precedence over that default.
fn use_default_cursor(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> bool {
    last_op(snapshot, node, OP_CURSOR).is_none()
}

fn accessibility_label(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> Option<SharedString> {
    if let Some(name) = snapshot.last_data_op(node, crate::semantic::OP_ACCESSIBLE_NAME) {
        return Some(name);
    }
    let mut label = String::new();
    append_accessibility_text(node, snapshot, &mut label);
    (!label.is_empty()).then(|| SharedString::from(label))
}

fn append_accessibility_text(
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    label: &mut String,
) {
    for child_id in snapshot.children(node) {
        let child = &snapshot.nodes[*child_id as usize];
        if component_metadata(child.component).is_some_and(|metadata| {
            metadata.adapter == NativeAdapter::Text && !child.data.is_empty()
        }) {
            if !label.is_empty() {
                label.push(' ');
            }
            label.push_str(&child.data);
        }
        append_accessibility_text(child, snapshot, label);
    }
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

/// Key/mouse observers and shortcut declarations on one semantic node. Tokens are render-bound;
/// shortcut descriptors use payload word B, while observer payload words remain zero.
#[derive(Clone, Default)]
struct KeyMouseBindings {
    shortcuts: Vec<crate::shortcuts::Binding>,
    isolate_shortcuts: bool,
    key_down: u64,
    key_up: u64,
    mouse_down: u64,
    mouse_up: u64,
    modifiers_changed: u64,
    hover: u64,
    mouse_down_out: u64,
    mouse_up_out: u64,
    mouse_move: u64,
    scroll_wheel: u64,
    file_drop: u64,
}

fn key_mouse_bindings(node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> KeyMouseBindings {
    KeyMouseBindings {
        shortcuts: snapshot
            .ops(node)
            .iter()
            .filter(|op| op.code == crate::semantic::OP_ON_SHORTCUT)
            .map(|op| crate::shortcuts::Binding {
                token: op.a,
                descriptor: op.b,
            })
            .collect(),
        isolate_shortcuts: last_op(snapshot, node, crate::semantic::OP_ISOLATE_SHORTCUTS)
            .is_some_and(|op| op.a != 0),
        key_down: last_op(snapshot, node, OP_ON_KEY_DOWN).map_or(0, |op| op.a),
        key_up: last_op(snapshot, node, OP_ON_KEY_UP).map_or(0, |op| op.a),
        mouse_down: last_op(snapshot, node, OP_ON_MOUSE_DOWN).map_or(0, |op| op.a),
        mouse_up: last_op(snapshot, node, OP_ON_MOUSE_UP).map_or(0, |op| op.a),
        modifiers_changed: last_op(snapshot, node, OP_ON_MODIFIERS_CHANGED).map_or(0, |op| op.a),
        hover: last_op(snapshot, node, OP_ON_HOVER).map_or(0, |op| op.a),
        mouse_down_out: last_op(snapshot, node, OP_ON_MOUSE_DOWN_OUT).map_or(0, |op| op.a),
        mouse_up_out: last_op(snapshot, node, OP_ON_MOUSE_UP_OUT).map_or(0, |op| op.a),
        mouse_move: last_op(snapshot, node, OP_ON_MOUSE_MOVE).map_or(0, |op| op.a),
        scroll_wheel: last_op(snapshot, node, OP_ON_SCROLL_WHEEL).map_or(0, |op| op.a),
        file_drop: last_op(snapshot, node, OP_ON_FILE_DROP).map_or(0, |op| op.a),
    }
}

fn modifiers_flags(modifiers: &gpui::Modifiers) -> u16 {
    u16::from(modifiers.control)
        | (u16::from(modifiers.alt) << 1)
        | (u16::from(modifiers.shift) << 2)
        | (u16::from(modifiers.platform) << 3)
        | (u16::from(modifiers.function) << 4)
}

fn mouse_button_code(button: &MouseButton) -> u32 {
    match button {
        MouseButton::Left => 0,
        MouseButton::Right => 1,
        MouseButton::Middle => 2,
        MouseButton::Navigate(direction) => match direction {
            gpui::NavigationDirection::Back => 3,
            gpui::NavigationDirection::Forward => 4,
        },
    }
}

fn invoke_key_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    kind: u16,
    key: &str,
    modifiers: u16,
    is_held: bool,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let flags = modifiers | (u16::from(is_held) << 5);
    let bytes = key.as_bytes();
    let event = NativeControlEvent {
        kind,
        flags,
        reserved: 0,
        revision: 0,
        data: bytes.as_ptr(),
        data_length: bytes.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_mouse_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    kind: u16,
    x: f32,
    y: f32,
    button: u32,
    click_count: u32,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 16];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&button.to_le_bytes());
    payload[12..16].copy_from_slice(&click_count.to_le_bytes());
    let event = NativeControlEvent {
        kind,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_modifiers_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let event = NativeControlEvent {
        kind: EVENT_MODIFIERS_CHANGED,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: std::ptr::null(),
        data_length: 0,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_hover_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    modifiers: u16,
    is_hovering: bool,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let event = NativeControlEvent {
        kind: EVENT_HOVER,
        flags: modifiers | (u16::from(is_hovering) << 5),
        reserved: 0,
        revision: 0,
        data: std::ptr::null(),
        data_length: 0,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_mouse_move_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    pressed_button: Option<u32>,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 12];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&pressed_button.unwrap_or(u32::MAX).to_le_bytes());
    let event = NativeControlEvent {
        kind: EVENT_MOUSE_MOVE,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_scroll_wheel_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    delta_x: f32,
    delta_y: f32,
    units: u32,
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    let mut payload = [0u8; 20];
    payload[..4].copy_from_slice(&x.to_bits().to_le_bytes());
    payload[4..8].copy_from_slice(&y.to_bits().to_le_bytes());
    payload[8..12].copy_from_slice(&delta_x.to_bits().to_le_bytes());
    payload[12..16].copy_from_slice(&delta_y.to_bits().to_le_bytes());
    payload[16..20].copy_from_slice(&units.to_le_bytes());
    let event = NativeControlEvent {
        kind: EVENT_SCROLL_WHEEL,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

fn invoke_file_drop_control_event(
    callbacks: ManagedCallbacks,
    session_id: u64,
    event_token: u64,
    x: f32,
    y: f32,
    paths: &[PathBuf],
    modifiers: u16,
) -> i32 {
    let Some(callback) = callbacks.control_event else {
        return -86;
    };
    // 8-byte LE header (x, y) followed by NUL-separated lossy UTF-8 paths.
    // Paths never contain NUL, so the join is unambiguous.
    let mut payload = Vec::with_capacity(8 + paths.len() * 64);
    payload.extend_from_slice(&x.to_bits().to_le_bytes());
    payload.extend_from_slice(&y.to_bits().to_le_bytes());
    for (index, path) in paths.iter().enumerate() {
        if index != 0 {
            payload.push(0);
        }
        payload.extend_from_slice(path.to_string_lossy().as_bytes());
    }
    let event = NativeControlEvent {
        kind: EVENT_FILE_DROPPED,
        flags: modifiers,
        reserved: 0,
        revision: 0,
        data: payload.as_ptr(),
        data_length: payload.len() as i32,
        reserved2: 0,
    };
    unsafe { callback(session_id, event_token, &event) }
}

/// Shortcuts apply their native consumption policy; observer listeners remain non-consuming.
/// Both operate on the focus ancestry after descendant controls have handled the event.
fn attach_key_mouse<T>(
    mut element: T,
    bindings: &KeyMouseBindings,
    cx: &mut Context<ManagedView>,
) -> T
where
    T: InteractiveElement,
{
    if !bindings.shortcuts.is_empty() || bindings.isolate_shortcuts {
        let shortcuts = bindings.shortcuts.clone();
        let isolated = bindings.isolate_shortcuts;
        element = element.on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
            let matched = this
                .resources
                .shortcuts
                .resolve(&shortcuts, isolated, event);
            if matched.consume {
                cx.stop_propagation();
            }
            if let Some(token) = matched.token {
                let event = NativeControlEvent {
                    kind: crate::semantic::EVENT_SHORTCUT_INVOKED,
                    flags: 0,
                    revision: 0,
                    data: std::ptr::null(),
                    data_length: 0,
                    reserved: 0,
                    reserved2: 0,
                };
                let status = unsafe {
                    this.callbacks.control_event.expect("validated callbacks")(
                        this.view_id,
                        token,
                        &event,
                    )
                };
                this.after_click(status, cx);
            }
        }));
    }
    if bindings.key_down != 0 {
        let token = bindings.key_down;
        element = element.on_key_down(cx.listener(move |this, event: &KeyDownEvent, _, cx| {
            let status = invoke_key_control_event(
                this.callbacks,
                this.view_id,
                token,
                EVENT_KEY_DOWN,
                event.keystroke.key.as_str(),
                modifiers_flags(&event.keystroke.modifiers),
                event.is_held,
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.key_up != 0 {
        let token = bindings.key_up;
        element = element.on_key_up(cx.listener(move |this, event: &KeyUpEvent, _, cx| {
            let status = invoke_key_control_event(
                this.callbacks,
                this.view_id,
                token,
                EVENT_KEY_UP,
                event.keystroke.key.as_str(),
                modifiers_flags(&event.keystroke.modifiers),
                false,
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.mouse_down != 0 {
        let token = bindings.mouse_down;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_down(
                button,
                cx.listener(move |this, event: &MouseDownEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_DOWN,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.mouse_up != 0 {
        let token = bindings.mouse_up;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_up(
                button,
                cx.listener(move |this, event: &MouseUpEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_UP,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.modifiers_changed != 0 {
        let token = bindings.modifiers_changed;
        element = element.on_modifiers_changed(cx.listener(
            move |this, event: &ModifiersChangedEvent, _, cx| {
                let status = invoke_modifiers_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            },
        ));
    }
    if bindings.mouse_down_out != 0 {
        let token = bindings.mouse_down_out;
        element =
            element.on_mouse_down_out(cx.listener(move |this, event: &MouseDownEvent, _, cx| {
                let status = invoke_mouse_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    EVENT_MOUSE_DOWN_OUT,
                    event.position.x.into(),
                    event.position.y.into(),
                    mouse_button_code(&event.button),
                    event.click_count.min(255) as u32,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            }));
    }
    if bindings.mouse_up_out != 0 {
        let token = bindings.mouse_up_out;
        for button in MouseButton::all() {
            let button_code = mouse_button_code(&button);
            element = element.on_mouse_up_out(
                button,
                cx.listener(move |this, event: &MouseUpEvent, _, cx| {
                    let status = invoke_mouse_control_event(
                        this.callbacks,
                        this.view_id,
                        token,
                        EVENT_MOUSE_UP_OUT,
                        event.position.x.into(),
                        event.position.y.into(),
                        button_code,
                        event.click_count.min(255) as u32,
                        modifiers_flags(&event.modifiers),
                    );
                    this.after_click(status, cx);
                }),
            );
        }
    }
    if bindings.mouse_move != 0 {
        let token = bindings.mouse_move;
        // Opt-in only: without a binding no listener is installed and no
        // per-move crossing occurs. Handlers must stay cheap.
        element = element.on_mouse_move(cx.listener(move |this, event: &MouseMoveEvent, _, cx| {
            let status = invoke_mouse_move_control_event(
                this.callbacks,
                this.view_id,
                token,
                event.position.x.into(),
                event.position.y.into(),
                event.pressed_button.as_ref().map(mouse_button_code),
                modifiers_flags(&event.modifiers),
            );
            this.after_click(status, cx);
        }));
    }
    if bindings.scroll_wheel != 0 {
        let token = bindings.scroll_wheel;
        // Opt-in only: retained Scroll resources keep owning wheel deltas
        // natively; this observes without consuming.
        element =
            element.on_scroll_wheel(cx.listener(move |this, event: &ScrollWheelEvent, _, cx| {
                let (delta_x, delta_y, units) = match &event.delta {
                    gpui::ScrollDelta::Pixels(point) => (point.x.into(), point.y.into(), 0),
                    gpui::ScrollDelta::Lines(point) => (point.x, point.y, 1),
                };
                let status = invoke_scroll_wheel_control_event(
                    this.callbacks,
                    this.view_id,
                    token,
                    event.position.x.into(),
                    event.position.y.into(),
                    delta_x,
                    delta_y,
                    units,
                    modifiers_flags(&event.modifiers),
                );
                this.after_click(status, cx);
            }));
    }
    if bindings.file_drop != 0 {
        let token = bindings.file_drop;
        // GPUI translates platform file drops into its internal drag system, so
        // on_drop fires on the element under the cursor. Opt-in only.
        element = element.on_drop(cx.listener(move |this, paths: &ExternalPaths, window, cx| {
            let position = window.mouse_position();
            let status = invoke_file_drop_control_event(
                this.callbacks,
                this.view_id,
                token,
                position.x.into(),
                position.y.into(),
                paths.paths(),
                modifiers_flags(&window.modifiers()),
            );
            this.after_click(status, cx);
        }));
    }
    element
}

/// Attaches the hover observer. Hover tracking needs stable element state, so this
/// requires the stateful bound; plain Divs are wrapped with their deterministic node
/// id at the call site. Fires on enter/exit transitions only, never per mouse move.
fn attach_hover<T>(mut element: T, bindings: &KeyMouseBindings, cx: &mut Context<ManagedView>) -> T
where
    T: StatefulInteractiveElement,
{
    if bindings.hover != 0 {
        let token = bindings.hover;
        element = element.on_hover(cx.listener(move |this, is_hovering: &bool, _, cx| {
            let status =
                invoke_hover_control_event(this.callbacks, this.view_id, token, 0, *is_hovering);
            this.after_click(status, cx);
        }));
    }
    element
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
            OP_FLEX_GROW => element.flex_grow(value),
            OP_ASPECT_RATIO => element.aspect_ratio(value),
            OP_GAP_PX => element.gap(px(value)),
            OP_PADDING_PX => element.p(px(value)),
            OP_MARGIN_PX => element.m(px(value)),
            OP_MARGIN_X_PX => element.mx(px(value)),
            OP_MARGIN_Y_PX => element.my(px(value)),
            OP_MARGIN_TOP_PX => element.mt(px(value)),
            OP_MARGIN_BOTTOM_PX => element.mb(px(value)),
            OP_MARGIN_LEFT_PX => element.ml(px(value)),
            OP_MARGIN_RIGHT_PX => element.mr(px(value)),
            OP_MARGIN_PERCENT => element.m(relative(value / 100.0)),
            OP_MARGIN_X_PERCENT => element.mx(relative(value / 100.0)),
            OP_MARGIN_Y_PERCENT => element.my(relative(value / 100.0)),
            OP_MARGIN_TOP_PERCENT => element.mt(relative(value / 100.0)),
            OP_MARGIN_BOTTOM_PERCENT => element.mb(relative(value / 100.0)),
            OP_MARGIN_LEFT_PERCENT => element.ml(relative(value / 100.0)),
            OP_MARGIN_RIGHT_PERCENT => element.mr(relative(value / 100.0)),
            OP_PADDING_X_PX => element.px(px(value)),
            OP_PADDING_Y_PX => element.py(px(value)),
            OP_PADDING_TOP_PX => element.pt(px(value)),
            OP_PADDING_BOTTOM_PX => element.pb(px(value)),
            OP_PADDING_LEFT_PX => element.pl(px(value)),
            OP_PADDING_RIGHT_PX => element.pr(px(value)),
            OP_PADDING_PERCENT => element.p(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_X_PERCENT => element.px(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_Y_PERCENT => element.py(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_TOP_PERCENT => element.pt(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_BOTTOM_PERCENT => element.pb(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_LEFT_PERCENT => element.pl(DefiniteLength::Fraction(value / 100.0)),
            OP_PADDING_RIGHT_PERCENT => element.pr(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_X_PX => element.gap_x(px(value)),
            OP_GAP_Y_PX => element.gap_y(px(value)),
            OP_GAP_PERCENT => element.gap(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_X_PERCENT => element.gap_x(DefiniteLength::Fraction(value / 100.0)),
            OP_GAP_Y_PERCENT => element.gap_y(DefiniteLength::Fraction(value / 100.0)),
            OP_MIN_WIDTH_PX => element.min_w(px(value)),
            OP_MIN_HEIGHT_PX => element.min_h(px(value)),
            OP_MAX_WIDTH_PX => element.max_w(px(value)),
            OP_MAX_HEIGHT_PX => element.max_h(px(value)),
            OP_MIN_WIDTH_PERCENT => element.min_w(relative(value / 100.0)),
            OP_MIN_HEIGHT_PERCENT => element.min_h(relative(value / 100.0)),
            OP_MAX_WIDTH_PERCENT => element.max_w(relative(value / 100.0)),
            OP_MAX_HEIGHT_PERCENT => element.max_h(relative(value / 100.0)),
            OP_FLEX_BASIS_PX => element.flex_basis(px(value)),
            OP_FLEX_BASIS_PERCENT => element.flex_basis(relative(value / 100.0)),
            OP_FLEX_SHRINK => element.flex_shrink(value),
            OP_FLEX_WRAP => match op.a as u32 {
                0 => element.flex_nowrap(),
                1 => element.flex_wrap(),
                2 => element.flex_wrap_reverse(),
                _ => element,
            },
            OP_ITEMS_START => element.items_start(),
            OP_ITEMS_END => element.items_end(),
            OP_ITEMS_BASELINE => element.items_baseline(),
            OP_ITEMS_STRETCH => element.items_stretch(),
            OP_JUSTIFY_START => element.justify_start(),
            OP_JUSTIFY_END => element.justify_end(),
            OP_DISPLAY_GRID => element.grid(),
            OP_GRID_COLS => element.grid_cols(op.a as u16),
            OP_GRID_COLS_MIN_CONTENT => element.grid_cols_min_content(op.a as u16),
            OP_GRID_COLS_MAX_CONTENT => element.grid_cols_max_content(op.a as u16),
            OP_GRID_ROWS => element.grid_rows(op.a as u16),
            OP_GRID_ROWS_MIN_CONTENT => element.grid_rows_min_content(op.a as u16),
            OP_GRID_ROWS_MAX_CONTENT => element.grid_rows_max_content(op.a as u16),
            OP_COL_SPAN => element.col_span(op.a as u16),
            OP_ROW_SPAN => element.row_span(op.a as u16),
            OP_COL_START => element.col_start(op.a as u16 as i16),
            OP_COL_END => element.col_end(op.a as u16 as i16),
            OP_ROW_START => element.row_start(op.a as u16 as i16),
            OP_ROW_END => element.row_end(op.a as u16 as i16),
            OP_COL_START_AUTO => element.col_start_auto(),
            OP_COL_END_AUTO => element.col_end_auto(),
            OP_ROW_START_AUTO => element.row_start_auto(),
            OP_ROW_END_AUTO => element.row_end_auto(),
            OP_COL_SPAN_FULL => element.col_span_full(),
            OP_ROW_SPAN_FULL => element.row_span_full(),
            OP_DISPLAY_NONE => element.hidden(),
            OP_ALIGN_CONTENT => match op.a as u32 {
                0 => element.content_normal(),
                1 => element.content_start(),
                2 => element.content_end(),
                3 => element.content_center(),
                4 => element.content_stretch(),
                5 => element.content_between(),
                6 => element.content_evenly(),
                7 => element.content_around(),
                _ => element,
            },
            OP_SELF_START => element.self_start(),
            OP_SELF_END => element.self_end(),
            OP_SELF_FLEX_START => element.self_flex_start(),
            OP_SELF_FLEX_END => element.self_flex_end(),
            OP_SELF_CENTER => element.self_center(),
            OP_SELF_BASELINE => element.self_baseline(),
            OP_SELF_STRETCH => element.self_stretch(),
            OP_RELATIVE => element.relative(),
            OP_ABSOLUTE => element.absolute(),
            OP_TOP_PX => element.top(px(value)),
            OP_LEFT_PX => element.left(px(value)),
            OP_RIGHT_PX => element.right(px(value)),
            OP_BOTTOM_PX => element.bottom(px(value)),
            OP_INSET_PX => element.inset(px(value)),
            OP_TOP_PERCENT => {
                element.top(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_LEFT_PERCENT => {
                element.left(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_RIGHT_PERCENT => {
                element.right(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_BOTTOM_PERCENT => {
                element.bottom(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_INSET_PERCENT => {
                element.inset(Length::Definite(DefiniteLength::Fraction(value / 100.0)))
            }
            OP_OVERFLOW_HIDDEN => element.overflow_hidden(),
            OP_OVERFLOW_X_HIDDEN => element.overflow_x_hidden(),
            OP_OVERFLOW_Y_HIDDEN => element.overflow_y_hidden(),
            OP_OPACITY => element.opacity(value),
            OP_TEXT_ALIGN => match op.a as u32 {
                0 => element.text_align(TextAlign::Left),
                1 => element.text_align(TextAlign::Center),
                2 => element.text_align(TextAlign::Right),
                _ => element,
            },
            OP_WHITE_SPACE => match op.a as u32 {
                0 => element.whitespace_normal(),
                1 => element.whitespace_nowrap(),
                _ => element,
            },
            OP_VISIBILITY => match op.a as u32 {
                0 => element.visible(),
                1 => element.invisible(),
                _ => element,
            },
            OP_LINE_CLAMP => element.line_clamp(op.a as usize),
            OP_WIDTH_PX => element.w(px(value)),
            OP_WIDTH_PERCENT => element.w(relative(value / 100.0)),
            OP_BACKGROUND_RGBA => element.bg(rgba(op.a as u32)),
            OP_HEIGHT_PX => element.h(px(value)),
            OP_HEIGHT_PERCENT => element.h(relative(value / 100.0)),
            OP_BORDER_RGBA => element.border_color(rgba(op.a as u32)),
            OP_BORDER_WIDTH_PX => element.border(px(value)),
            OP_BORDER_STYLE => match op.a as u32 {
                // Solid is the GPUI default, so it applies no change.
                1 => element.border_dashed(),
                _ => element,
            },
            OP_RADIUS_PX => element.rounded(px(value)),
            OP_RADIUS_TOP_LEFT_PX => element.rounded_tl(px(value)),
            OP_RADIUS_TOP_RIGHT_PX => element.rounded_tr(px(value)),
            OP_RADIUS_BOTTOM_LEFT_PX => element.rounded_bl(px(value)),
            OP_RADIUS_BOTTOM_RIGHT_PX => element.rounded_br(px(value)),
            OP_TEXT_RGBA => element.text_color(rgba(op.a as u32)),
            OP_TEXT_BACKGROUND => element.text_bg(rgba(op.a as u32)),
            OP_FONT_SIZE_PX => element.text_size(px(value)),
            OP_FONT_WEIGHT => element.font_weight(FontWeight::from(value)),
            OP_FONT_FAMILY => match snapshot.last_data_op(node, OP_FONT_FAMILY) {
                Some(family) => element.font_family(family),
                None => element,
            },
            OP_FONT_FEATURES => match snapshot
                .last_data_op(node, OP_FONT_FEATURES)
                .as_deref()
                .and_then(parse_font_features)
            {
                Some(features) => element.font_features(features),
                None => element,
            },
            OP_FONT_FALLBACKS => match snapshot
                .last_data_op(node, OP_FONT_FALLBACKS)
                .as_deref()
                .and_then(parse_font_fallbacks)
            {
                Some(fallbacks) => element.font(apply_font_fallbacks(snapshot, node, fallbacks)),
                None => element,
            },
            OP_FONT_STYLE => match op.a as u32 {
                // GPUI exposes no oblique setter, so the schema enum covers normal and italic only.
                1 => element.italic(),
                _ => element.not_italic(),
            },
            OP_TEXT_ELLIPSIS => element.text_ellipsis(),
            OP_LINE_HEIGHT_PX => element.line_height(px(value)),
            OP_LINE_HEIGHT_PERCENT => element.line_height(DefiniteLength::Fraction(value / 100.0)),
            OP_UNDERLINE => element.underline(),
            OP_LINE_THROUGH => element.line_through(),
            OP_TEXT_DECORATION_NONE => element.text_decoration_none(),
            OP_TEXT_DECORATION_COLOR => {
                element.text_decoration_color(Hsla::from(rgba(op.a as u32)))
            }
            OP_TEXT_DECORATION_WAVY => element.text_decoration_wavy(),
            OP_TEXT_DECORATION_SOLID => element.text_decoration_solid(),
            OP_TEXT_TRUNCATE => match snapshot.last_data_op(node, OP_TEXT_TRUNCATE) {
                Some(truncation) => element.text_overflow(TextOverflow::Truncate(truncation)),
                None => element,
            },
            OP_SHADOW_COLOR | OP_SHADOW_OFFSET | OP_SHADOW_BLUR | OP_SHADOW_SPREAD => {
                apply_box_shadow(element, node, snapshot)
            }
            OP_CURSOR => match op.a as u32 {
                0 => element.cursor(CursorStyle::Arrow),
                1 => element.cursor(CursorStyle::IBeam),
                2 => element.cursor(CursorStyle::Crosshair),
                3 => element.cursor(CursorStyle::ClosedHand),
                4 => element.cursor(CursorStyle::OpenHand),
                5 => element.cursor(CursorStyle::PointingHand),
                6 => element.cursor(CursorStyle::ResizeLeft),
                7 => element.cursor(CursorStyle::ResizeRight),
                8 => element.cursor(CursorStyle::ResizeLeftRight),
                9 => element.cursor(CursorStyle::ResizeUp),
                10 => element.cursor(CursorStyle::ResizeDown),
                11 => element.cursor(CursorStyle::ResizeUpDown),
                12 => element.cursor(CursorStyle::ResizeUpLeftDownRight),
                13 => element.cursor(CursorStyle::ResizeUpRightDownLeft),
                14 => element.cursor(CursorStyle::ResizeColumn),
                15 => element.cursor(CursorStyle::ResizeRow),
                16 => element.cursor(CursorStyle::IBeamCursorForVerticalLayout),
                17 => element.cursor(CursorStyle::OperationNotAllowed),
                18 => element.cursor(CursorStyle::DragLink),
                19 => element.cursor(CursorStyle::DragCopy),
                20 => element.cursor(CursorStyle::ContextualMenu),
                _ => element,
            },
            OP_HOVER_BACKGROUND_RGBA
            | OP_HOVER_TEXT_RGBA
            | OP_HOVER_BORDER_RGBA
            | OP_ACTIVE_BACKGROUND_RGBA
            | OP_ACTIVE_TEXT_RGBA
            | OP_ACTIVE_BORDER_RGBA => element,
            OP_CHECKED
            | OP_DISABLED
            | OP_ELEMENT_OWNER
            | OP_ON_CLICK
            | OP_ON_FILE_DROP
            | OP_ON_HOVER
            | OP_ON_KEY_DOWN
            | OP_ON_KEY_UP
            | OP_ON_MODIFIERS_CHANGED
            | OP_ON_MOUSE_DOWN
            | OP_ON_MOUSE_DOWN_OUT
            | OP_ON_MOUSE_MOVE
            | OP_ON_MOUSE_UP
            | OP_ON_MOUSE_UP_OUT
            | OP_ON_SCROLL_WHEEL
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

/// Builds the full font description for a fallbacks declaration. Sibling font operations
/// supply the remaining parts so per-field last-wins ordering holds; anything unset falls back
/// to the GPUI font defaults.
fn apply_font_fallbacks(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
    fallbacks: FontFallbacks,
) -> Font {
    let mut font = gpui::font(
        snapshot
            .last_data_op(node, OP_FONT_FAMILY)
            .unwrap_or_else(|| ".SystemUIFont".into()),
    );
    if let Some(operation) = last_op(snapshot, node, OP_FONT_WEIGHT) {
        font.weight = FontWeight::from(f32::from_bits(operation.a as u32));
    }
    if last_op(snapshot, node, OP_FONT_STYLE).is_some_and(|operation| operation.a == 1) {
        font.style = FontStyle::Italic;
    }
    if let Some(features) = snapshot
        .last_data_op(node, OP_FONT_FEATURES)
        .as_deref()
        .and_then(parse_font_features)
    {
        font.features = features;
    }
    font.fallbacks = Some(fallbacks);
    font
}

/// Composes the single-layer box shadow from its scalar parts. Any shadow operation on the
/// node enables the layer; parts without an explicit operation fall back to transparent black,
/// zero offset, and zero blur/spread. Multi-layer shadow vectors are not exposed.
fn apply_box_shadow<T: Styled>(element: T, node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> T {
    let color = last_op(snapshot, node, OP_SHADOW_COLOR).map_or(0, |op| op.a as u32);
    let (x, y) = last_op(snapshot, node, OP_SHADOW_OFFSET).map_or((0.0, 0.0), op_f32x2);
    let blur =
        last_op(snapshot, node, OP_SHADOW_BLUR).map_or(0.0, |op| f32::from_bits(op.a as u32));
    let spread =
        last_op(snapshot, node, OP_SHADOW_SPREAD).map_or(0.0, |op| f32::from_bits(op.a as u32));
    element.shadow(vec![BoxShadow {
        color: Hsla::from(rgba(color)),
        offset: point(px(x), px(y)),
        blur_radius: px(blur),
        spread_radius: px(spread),
        inset: false,
    }])
}

#[derive(Clone, Copy, Default)]
struct InteractionPaint {
    background: Option<u32>,
    text: Option<u32>,
    border: Option<u32>,
}

impl InteractionPaint {
    fn is_empty(self) -> bool {
        self.background.is_none() && self.text.is_none() && self.border.is_none()
    }
}

fn interaction_paint(
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    background_code: u16,
    text_code: u16,
    border_code: u16,
) -> InteractionPaint {
    InteractionPaint {
        background: last_op(snapshot, node, background_code).map(|op| op.a as u32),
        text: last_op(snapshot, node, text_code).map(|op| op.a as u32),
        border: last_op(snapshot, node, border_code).map(|op| op.a as u32),
    }
}

fn apply_paint<T: Styled>(mut style: T, paint: InteractionPaint) -> T {
    if let Some(color) = paint.background {
        style = style.bg(rgba(color));
    }
    if let Some(color) = paint.text {
        style = style.text_color(rgba(color));
    }
    if let Some(color) = paint.border {
        style = style.border_color(rgba(color));
    }
    style
}

/// Resolves only transient interaction states in native GPUI. Application variants and durable
/// states such as selection remain managed concerns and are already flattened into base ops.
fn apply_interaction_styles<T>(
    mut element: T,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    theme: NativeTheme,
) -> T
where
    T: StatefulInteractiveElement + Styled,
{
    element = presentation::focus_ring(element, theme.border_focused);

    let hover = interaction_paint(
        node,
        snapshot,
        OP_HOVER_BACKGROUND_RGBA,
        OP_HOVER_TEXT_RGBA,
        OP_HOVER_BORDER_RGBA,
    );
    let has_hover_paint = !hover.is_empty();
    element = if !has_hover_paint {
        element.hover(move |style| style.bg(rgba(theme.element_hover)))
    } else {
        element.hover(move |style| apply_paint(style, hover))
    };

    let active = interaction_paint(
        node,
        snapshot,
        OP_ACTIVE_BACKGROUND_RGBA,
        OP_ACTIVE_TEXT_RGBA,
        OP_ACTIVE_BORDER_RGBA,
    );
    element = if active.is_empty() && has_hover_paint {
        // Preserve an explicit hover palette (notably destructive caption controls) when the
        // app has not supplied a distinct pressed palette.
        presentation::pressed_feedback(element)
    } else if active.is_empty() {
        element.active(move |style| style.bg(rgba(theme.element_active)))
    } else {
        element.active(move |style| apply_paint(style, active))
    };

    element
}

/// Reconciles a row cell's width and alignment against the owning table's declared columns.
/// Cells carry only a column index (OP_TABLE_CELL_COLUMN); the resolved width intent comes
/// from the table spec so header cells and row cells are laid out from one source of truth.
/// Applying it after apply_styles lets a cell still declare its own padding or text styles.
fn apply_table_cell_layout(
    element: gpui::Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    resources: &ResourceStore,
    list_key: &crate::resources::ResourceKey,
) -> gpui::Div {
    let Some(op) = last_op(snapshot, node, OP_TABLE_CELL_COLUMN) else {
        return element;
    };
    let Some(spec) = resources.table_spec(list_key) else {
        return element;
    };
    let Some(column) = spec.columns.get(op.a as usize) else {
        return element;
    };
    apply_column_layout(element, column)
}

fn apply_column_layout(
    mut element: gpui::Div,
    column: &crate::resources::TableColumnSpec,
) -> gpui::Div {
    element = element.flex().overflow_hidden();
    element = if column.width_is_fraction {
        element.w(relative(column.width.into()))
    } else {
        element.w(column.width)
    };
    match column.alignment {
        1 => element.justify_center(),
        2 => element.justify_end(),
        _ => element,
    }
}

/// FNV-1a over length-delimited parts, finalized with the splitmix64 avalanche. Length
/// prefixes keep concatenation unambiguous; the avalanche spreads structured inputs evenly.
fn stable_hash(parts: &[&[u8]]) -> u64 {
    const FNV_BASIS: u64 = 0xcbf2_9ce4_8422_2325;
    const FNV_PRIME: u64 = 0x1000_0000_01b3;
    let mut h = FNV_BASIS;
    for part in parts {
        h ^= (part.len() as u64).wrapping_mul(0x9e37_79b9_7f4a_7c15);
        h = h.wrapping_mul(FNV_PRIME);
        for byte in *part {
            h ^= *byte as u64;
            h = h.wrapping_mul(FNV_PRIME);
        }
    }
    let mut z = h;
    z = (z ^ (z >> 30)).wrapping_mul(0xbf58_476d_1ce4_e5b9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94d0_49bb_1331_11eb);
    z ^ (z >> 33)
}

/// Stable GPUI state identity for a managed interactive element. Local keys are reusable in
/// separate managed View instances, while the same owner/key pair survives tree reordering.
fn interactive_element_id(
    node_id: u32,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> ElementId {
    if let Some(owner) = last_op(snapshot, node, OP_ELEMENT_OWNER) {
        return (
            "managed-control",
            managed_control_state_id(owner.a as u32, &node.data),
        )
            .into();
    }

    if node.data.is_empty() {
        ("managed-control-node", node_id as u64).into()
    } else {
        node.data.clone().into()
    }
}

fn managed_control_state_id(owner_view: u32, local_key: &str) -> u64 {
    let owner = owner_view.to_le_bytes();
    stable_hash(&[&owner, local_key.as_bytes()])
}

/// Deterministic GPUI state id for an interactive node inside a virtualized row. Combines the
/// list identity, the row identity (stable model ID when declared, positional index otherwise),
/// and the node identity into one 64-bit value so `ElementId::NamedInteger` needs no per-frame
/// allocation. Hash collisions across distinct identities are 64-bit improbable.
fn row_state_id(
    list_key: &crate::resources::ResourceKey,
    item_index: usize,
    item_id: Option<u64>,
    node_data: &str,
) -> u64 {
    let identity = item_id.unwrap_or(item_index as u64).to_le_bytes();
    let owner = list_key.owner_view.to_le_bytes();
    let identity_kind = [u8::from(item_id.is_some())];
    stable_hash(&[
        &owner,
        list_key.key.as_bytes(),
        &identity_kind,
        &identity,
        node_data.as_bytes(),
    ])
}

/// Resolves scrollbar chrome from the declared visual width and gutter mode.
fn scrollbar_metrics(snapshot: &ValidatedSnapshot, node: &SnapshotNode) -> ScrollbarMetrics {
    let width = last_op(snapshot, node, OP_SCROLLBAR_WIDTH).map_or(DEFAULT_SCROLLBAR_WIDTH, |op| {
        px(f32::from_bits(op.a as u32))
    });
    let gutter_enabled = last_op(snapshot, node, OP_SCROLLBAR_GUTTER).is_some_and(|op| op.a != 0);
    ScrollbarMetrics::new(width, gutter_enabled)
}

/// The reserved spacer column that keeps virtualized content off the scrollbar track.
fn gutter_spacer(gutter: gpui::Pixels) -> gpui::Div {
    div().flex_shrink_0().w(gutter)
}

/// Header content is managed; cell geometry shares the row column policy and gutter.
fn table_header_strip(
    spec: &std::rc::Rc<TableSpec>,
    theme: NativeTheme,
    gutter: gpui::Pixels,
    mut content: impl FnMut(usize) -> Option<AnyElement>,
) -> gpui::Div {
    let strip = div()
        .flex()
        .flex_row()
        .min_h(px(32.))
        .flex_shrink_0()
        .bg(rgba(theme.element_background))
        .text_color(rgba(theme.text_muted))
        .border_b_1()
        .border_color(rgba(theme.border_variant));
    // Only cells exclude the gutter; strip paint spans the complete table width.
    let mut cells = div().flex().flex_row().flex_grow(1.).min_w_0();
    for (index, column) in spec.columns.iter().enumerate() {
        let cell = apply_column_layout(
            div()
                .items_center()
                .px(px(10.))
                .overflow_hidden()
                .whitespace_nowrap(),
            column,
        );
        let content = content(index).unwrap_or_else(|| {
            div()
                .child(column.header.clone())
                .text_size(px(12.))
                .into_any_element()
        });
        cells = cells.child(cell.child(content));
    }
    let strip = strip.child(cells);
    if gutter > px(0.) {
        strip.child(gutter_spacer(gutter))
    } else {
        strip
    }
}

fn apply_table_header_styles(
    mut header: gpui::Div,
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
) -> gpui::Div {
    if let Some(op) = last_op(snapshot, node, OP_TABLE_HEADER_BACKGROUND_RGBA) {
        header = header.bg(rgba(op.a as u32));
    }
    if let Some(op) = last_op(snapshot, node, OP_TABLE_HEADER_TEXT_RGBA) {
        header = header.text_color(rgba(op.a as u32));
    }
    if let Some(op) = last_op(snapshot, node, OP_TABLE_HEADER_BORDER_RGBA) {
        header = header.border_color(rgba(op.a as u32));
    }
    header
}

fn apply_window_control_area<T>(element: T, node: &SnapshotNode, snapshot: &ValidatedSnapshot) -> T
where
    T: Styled + InteractiveElement,
{
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
        element
            .flex()
            .items_center()
            .justify_center()
            .font_family(windows_caption_font())
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

#[derive(Clone, Copy)]
struct DrawingViewBox {
    x: f32,
    y: f32,
    width: f32,
    height: f32,
}

#[derive(Clone, Copy)]
enum DrawingPaint {
    Fill { rule: FillRule },
    Stroke { width: f32 },
}

fn materialize_drawing(node_id: u32, snapshot: &ValidatedSnapshot) -> AnyElement {
    prepare_drawing(node_id, snapshot).into_any_element()
}

fn prepare_drawing(node_id: u32, snapshot: &ValidatedSnapshot) -> impl IntoElement {
    let node = &snapshot.nodes[node_id as usize];
    let cache = snapshot.drawing_cache();
    let origin = last_op(snapshot, node, OP_DRAWING_VIEW_BOX_ORIGIN).map(op_f32x2);
    let size = last_op(snapshot, node, OP_DRAWING_VIEW_BOX_SIZE).map(op_f32x2);
    let view_box = origin
        .zip(size)
        .map(|((x, y), (width, height))| DrawingViewBox {
            x,
            y,
            width,
            height,
        });
    let commands = snapshot.drawing_commands(node);
    let path_count = snapshot.children(node).len();
    let padding = last_op(snapshot, node, OP_PADDING_PX)
        .map_or(0.0, |op| f32::from_bits(op.a as u32))
        .max(0.0);

    let element = canvas(
        move |bounds, _, _| {
            let max_padding =
                (f32::from(bounds.size.width).min(f32::from(bounds.size.height)) / 2.0).max(0.0);
            let drawing_bounds = bounds.inset(px(padding.min(max_padding)));
            cache.borrow_mut().prepare(node_id, drawing_bounds, || {
                let mut painted = Vec::with_capacity(path_count * 2);
                for operations in commands.paths() {
                    if let Some(fill) = last_op_in(operations, OP_PATH_FILL_RGBA) {
                        let rule = match last_op_in(operations, OP_PATH_FILL_RULE).map(|op| op.a) {
                            Some(1) => FillRule::EvenOdd,
                            _ => FillRule::NonZero,
                        };
                        let paint = DrawingPaint::Fill { rule };
                        if let Some(path) =
                            build_drawing_path(operations, drawing_bounds, view_box, paint)
                        {
                            painted.push((path, fill.a as u32));
                        }
                    }
                    if let Some(stroke) = last_op_in(operations, OP_PATH_STROKE_RGBA) {
                        let width = last_op_in(operations, OP_PATH_STROKE_WIDTH_PX)
                            .map_or(1.0, |op| f32::from_bits(op.a as u32));
                        let paint = DrawingPaint::Stroke { width };
                        if let Some(path) =
                            build_drawing_path(operations, drawing_bounds, view_box, paint)
                        {
                            painted.push((path, stroke.a as u32));
                        }
                    }
                }
                painted
            })
        },
        |_, painted, window, _| match std::rc::Rc::try_unwrap(painted) {
            Ok(paths) => {
                for (path, color) in paths {
                    window.paint_path(path, rgba(color));
                }
            }
            Err(paths) => {
                for (path, color) in paths.iter() {
                    window.paint_path(path.clone(), rgba(*color));
                }
            }
        },
    )
    .overflow_hidden();
    apply_styles(element, node, snapshot)
}

fn build_drawing_path(
    operations: &[crate::abi::OpRecord],
    bounds: gpui::Bounds<Pixels>,
    view_box: Option<DrawingViewBox>,
    paint: DrawingPaint,
) -> Option<gpui::Path<Pixels>> {
    let mut builder = match paint {
        DrawingPaint::Fill { rule, .. } => PathBuilder::fill()
            .with_style(PathStyle::Fill(FillOptions::default().with_fill_rule(rule))),
        DrawingPaint::Stroke { width, .. } => PathBuilder::stroke(px(width)),
    };
    if matches!(paint, DrawingPaint::Stroke { .. }) {
        let dash = operations
            .iter()
            .filter(|op| op.code == OP_PATH_DASH_PX)
            .map(|op| px(f32::from_bits(op.a as u32)))
            .collect::<Vec<_>>();
        if !dash.is_empty() {
            builder = builder.dash_array(&dash);
        }
    }

    let mut started = false;
    let mut has_segment = false;
    let mut quadratic_control = None;
    let mut cubic_control_a = None;
    let mut cubic_control_b = None;
    let mut arc_radii = None;
    let mut arc_rotation = None;
    let mut arc_flags = None;
    let mut circle_center = None;

    for op in operations {
        match op.code {
            OP_PATH_MOVE_TO => {
                builder.move_to(drawing_point(op_f32x2(op), bounds, view_box));
                started = true;
            }
            OP_PATH_LINE_TO if started => {
                builder.line_to(drawing_point(op_f32x2(op), bounds, view_box));
                has_segment = true;
            }
            OP_PATH_QUADRATIC_CONTROL => quadratic_control = Some(op_f32x2(op)),
            OP_PATH_QUADRATIC_TO if started => {
                if let Some(control) = quadratic_control.take() {
                    builder.curve_to(
                        drawing_point(op_f32x2(op), bounds, view_box),
                        drawing_point(control, bounds, view_box),
                    );
                    has_segment = true;
                }
            }
            OP_PATH_CUBIC_CONTROL_A => cubic_control_a = Some(op_f32x2(op)),
            OP_PATH_CUBIC_CONTROL_B => cubic_control_b = Some(op_f32x2(op)),
            OP_PATH_CUBIC_TO if started => {
                if let (Some(control_a), Some(control_b)) =
                    (cubic_control_a.take(), cubic_control_b.take())
                {
                    builder.cubic_bezier_to(
                        drawing_point(op_f32x2(op), bounds, view_box),
                        drawing_point(control_a, bounds, view_box),
                        drawing_point(control_b, bounds, view_box),
                    );
                    has_segment = true;
                }
            }
            OP_PATH_ARC_RADII => arc_radii = Some(op_f32x2(op)),
            OP_PATH_ARC_ROTATION => arc_rotation = Some(f32::from_bits(op.a as u32)),
            OP_PATH_ARC_FLAGS => arc_flags = Some(op.a as u32),
            OP_PATH_ARC_TO if started => {
                if let (Some(radii), Some(rotation), Some(flags)) =
                    (arc_radii.take(), arc_rotation.take(), arc_flags.take())
                {
                    builder.arc_to(
                        drawing_radii(radii, bounds, view_box),
                        px(rotation),
                        flags & 1 != 0,
                        flags & 2 != 0,
                        drawing_point(op_f32x2(op), bounds, view_box),
                    );
                    has_segment = true;
                }
            }
            OP_PATH_CIRCLE_CENTER => circle_center = Some(op_f32x2(op)),
            OP_PATH_CIRCLE_RADIUS => {
                if let Some(center) = circle_center.take() {
                    let center = drawing_point(center, bounds, view_box);
                    let radius =
                        drawing_uniform_radius(f32::from_bits(op.a as u32), bounds, view_box);
                    builder.move_to(point(center.x + radius, center.y));
                    builder.arc_to(
                        point(radius, radius),
                        px(0.0),
                        false,
                        true,
                        point(center.x - radius, center.y),
                    );
                    builder.arc_to(
                        point(radius, radius),
                        px(0.0),
                        false,
                        true,
                        point(center.x + radius, center.y),
                    );
                    builder.close();
                    started = true;
                    has_segment = true;
                }
            }
            OP_PATH_CLOSE if started => {
                builder.close();
                has_segment = true;
            }
            _ => {}
        }
    }

    has_segment.then(|| builder.build().ok()).flatten()
}

fn drawing_point(
    (x, y): (f32, f32),
    bounds: gpui::Bounds<Pixels>,
    view_box: Option<DrawingViewBox>,
) -> gpui::Point<Pixels> {
    if let Some(view_box) = view_box {
        point(
            bounds.origin.x + bounds.size.width * ((x - view_box.x) / view_box.width),
            bounds.origin.y + bounds.size.height * ((y - view_box.y) / view_box.height),
        )
    } else {
        point(bounds.origin.x + px(x), bounds.origin.y + px(y))
    }
}

fn drawing_radii(
    (x, y): (f32, f32),
    bounds: gpui::Bounds<Pixels>,
    view_box: Option<DrawingViewBox>,
) -> gpui::Point<Pixels> {
    if let Some(view_box) = view_box {
        point(
            bounds.size.width * (x / view_box.width),
            bounds.size.height * (y / view_box.height),
        )
    } else {
        point(px(x), px(y))
    }
}

fn drawing_uniform_radius(
    radius: f32,
    bounds: gpui::Bounds<Pixels>,
    view_box: Option<DrawingViewBox>,
) -> Pixels {
    if let Some(view_box) = view_box {
        let scale_x = f32::from(bounds.size.width) / view_box.width;
        let scale_y = f32::from(bounds.size.height) / view_box.height;
        px(radius * scale_x.min(scale_y))
    } else {
        px(radius)
    }
}

fn op_f32x2(op: &crate::abi::OpRecord) -> (f32, f32) {
    (
        f32::from_bits(op.a as u32),
        f32::from_bits((op.a >> 32) as u32),
    )
}

fn last_op_in(operations: &[crate::abi::OpRecord], code: u16) -> Option<&crate::abi::OpRecord> {
    operations.iter().rev().find(|op| op.code == code)
}

fn materialize_image(
    node: &SnapshotNode,
    snapshot: &ValidatedSnapshot,
    theme: NativeTheme,
) -> AnyElement {
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
        .with_fallback(move || {
            div()
                .size_full()
                .flex()
                .items_center()
                .justify_center()
                .bg(rgba(theme.element_active))
                .text_color(rgba(theme.text_muted))
                .child("Image unavailable")
                .into_any_element()
        });
    apply_styles(element, node, snapshot).into_any_element()
}

struct CollectionFocusState {
    focus: FocusHandle,
}

/// Adds native cursor hit testing without adding a layout box or changing row state IDs.
pub(crate) struct CollectionRow {
    element: AnyElement,
    cursor: std::rc::Rc<CollectionCursor>,
    focus: FocusHandle,
    index: usize,
    epoch: u64,
    row_events: Option<ListRowEvents>,
    context_menu: Option<(
        u64,
        std::rc::Rc<crate::row_menu::RowMenus>,
        std::rc::Rc<std::cell::RefCell<ManagedListResource>>,
    )>,
}

impl CollectionRow {
    pub(crate) fn new(
        element: AnyElement,
        cursor: std::rc::Rc<CollectionCursor>,
        focus: FocusHandle,
        index: usize,
    ) -> Self {
        let epoch = cursor.epoch();
        Self {
            element,
            cursor,
            focus,
            index,
            epoch,
            row_events: None,
            context_menu: None,
        }
    }

    fn with_row_events(mut self, row_events: Option<ListRowEvents>) -> Self {
        self.row_events = row_events;
        self
    }

    pub(crate) fn with_context_menu(
        mut self,
        token: u64,
        menus: std::rc::Rc<crate::row_menu::RowMenus>,
        resource: std::rc::Rc<std::cell::RefCell<ManagedListResource>>,
    ) -> Self {
        if token != 0 {
            self.context_menu = Some((token, menus, resource));
        }
        self
    }
}

impl IntoElement for CollectionRow {
    type Element = Self;
    fn into_element(self) -> Self {
        self
    }
}

impl gpui::Element for CollectionRow {
    type RequestLayoutState = ();
    type PrepaintState = gpui::Hitbox;

    fn id(&self) -> Option<ElementId> {
        None
    }
    fn source_location(&self) -> Option<&'static core::panic::Location<'static>> {
        None
    }

    fn request_layout(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (gpui::LayoutId, ()) {
        (self.element.request_layout(window, cx), ())
    }

    fn prepaint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        bounds: gpui::Bounds<Pixels>,
        _: &mut (),
        window: &mut Window,
        cx: &mut App,
    ) -> gpui::Hitbox {
        let hitbox = window.insert_hitbox(bounds, gpui::HitboxBehavior::Normal);
        if let Some((_, menus, resource)) = &self.context_menu {
            menus.observe(resource, self.index, bounds, hitbox.content_mask.bounds);
        }
        self.element.prepaint(window, cx);
        hitbox
    }

    fn paint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: gpui::Bounds<Pixels>,
        _: &mut (),
        hitbox: &mut gpui::Hitbox,
        window: &mut Window,
        cx: &mut App,
    ) {
        let hitbox = hitbox.clone();
        let cursor = self.cursor.clone();
        let focus = self.focus.clone();
        let index = self.index;
        let epoch = self.epoch;
        let row_events = self.row_events;
        let context_menu = self.context_menu.clone();
        // Capture precedes child handlers; children can still take focus or consume the event.
        window.on_mouse_event(move |event: &MouseDownEvent, phase, window, cx| {
            if phase.bubble()
                && event.button == MouseButton::Right
                && hitbox.is_hovered(window)
                && cursor.epoch() == epoch
                && let Some((token, menus, resource)) = &context_menu
                && menus.open(
                    resource,
                    index,
                    *token,
                    hitbox.bounds,
                    hitbox.content_mask.bounds,
                    event.position,
                    window,
                    cx,
                )
            {
                cx.stop_propagation();
                window.prevent_default();
            }
            if phase.capture()
                && event.button == MouseButton::Left
                && hitbox.is_hovered(window)
                && cursor.set_from_row(index, epoch)
            {
                focus.focus(window, cx);
                window.refresh();
            }
            if phase.bubble()
                && event.button == MouseButton::Left
                && event.modifiers == gpui::Modifiers::none()
                && hitbox.is_hovered(window)
                && cursor.epoch() == epoch
                && let Some(events) = row_events
            {
                let kind = match event.click_count {
                    1 if events.selection_token != 0 => Some(ListRowEventKind::Selection),
                    2 if events.activation_token != 0 => Some(ListRowEventKind::Activation),
                    _ => None,
                };
                if let Some(kind) = kind {
                    dispatch_list_row_event(Ok(Some(events)), kind, false);
                    cx.stop_propagation();
                }
            }
        });
        self.element.paint(window, cx);
    }
}

fn collection_focus_id(name: &'static str, key: &crate::resources::ResourceKey) -> ElementId {
    let owner = key.owner_view.to_le_bytes();
    ElementId::named_usize(name, stable_hash(&[&owner, key.key.as_bytes()]) as usize)
}

fn dispatch_list_row_event(
    result: Result<Option<ListRowEvents>, (u64, i32)>,
    kind: ListRowEventKind,
    keyboard: bool,
) {
    let (session, status) = match result {
        Ok(Some(events)) => (events.session_id, events.emit(kind, keyboard)),
        Ok(None) => return,
        Err(failure) => failure,
    };
    crate::app_host::after_detached_callback(session, status);
}

pub(crate) fn handle_collection_row_event_key(
    event: &KeyDownEvent,
    window: &mut Window,
    cx: &mut App,
    focus: &FocusHandle,
    resource: &std::rc::Rc<std::cell::RefCell<ManagedListResource>>,
) -> bool {
    let kind = match event.keystroke.key.as_str() {
        "enter" => ListRowEventKind::Activation,
        "space" => ListRowEventKind::Selection,
        _ => return false,
    };
    if event.prefer_character_input
        || event.keystroke.modifiers != gpui::Modifiers::none()
        || !focus.is_focused(window)
        || !resource.borrow().event_enabled(kind)
    {
        return false;
    }
    cx.stop_propagation();
    if !event.is_held {
        let result = {
            let mut resource = resource.borrow_mut();
            resource
                .cursor
                .active()
                .map(|index| resource.prepare_row_event(index, kind))
        };
        // Release the resource borrow before application handlers can issue commands.
        if let Some(result) = result {
            dispatch_list_row_event(result, kind, true);
        }
    }
    true
}

fn handle_collection_key_down(
    event: &KeyDownEvent,
    window: &mut Window,
    cx: &mut App,
    cursor: &CollectionCursor,
    list_state: &ListState,
    interaction: &ScrollInteraction,
    item_count: usize,
) {
    if item_count == 0 {
        return;
    }
    let modifiers = event.keystroke.modifiers;
    if modifiers.control || modifiers.alt || modifiers.platform || modifiers.function {
        return;
    }

    let Some(current) = cursor.active() else {
        return;
    };
    let current = current.min(item_count.saturating_sub(1));
    let key = event.keystroke.key.as_str();
    let next = match key {
        "pageup" | "pagedown" => page_collection(list_state, key == "pagedown"),
        _ => collection_key_target(key, current, item_count).inspect(|&next| {
            list_state.scroll_to_reveal_item(next);
        }),
    };
    let Some(next) = next else {
        return;
    };

    // Keyboard navigation supersedes queued wheel easing, just like dragging the scrollbar.
    interaction.remaining.set(gpui::Point::default());
    cursor.set(next);
    window.refresh();
    cx.stop_propagation();
}

fn page_collection(list_state: &ListState, down: bool) -> Option<usize> {
    let last = list_state.item_count().checked_sub(1)?;
    let height = list_state.viewport_bounds().size.height;
    if height <= px(0.) || !f32::from(height).is_finite() {
        return None;
    }
    let current = -list_state.scroll_px_offset_for_scrollbar().y;
    let maximum = list_state.max_offset_for_scrollbar().y.max(px(0.));
    let target = (current + if down { height } else { -height })
        .max(px(0.))
        .min(maximum);

    // Map the absolute target through GPUI's measured/estimated height tree. A zero anchor also
    // normalizes bottom-aligned end sentinels, which scroll_by alone treats as past the last row.
    // Both mutations run synchronously; no render or managed row request occurs between them.
    list_state.scroll_to(ListOffset::default());
    list_state.scroll_by(target);
    // Keep the partial-row offset: revealing this item would undo the page movement for tall rows.
    Some(list_state.logical_scroll_top().item_ix.min(last))
}

fn collection_key_target(key: &str, current: usize, item_count: usize) -> Option<usize> {
    let last = item_count.checked_sub(1)?;
    Some(match key {
        "home" => 0,
        "end" => last,
        "up" => current.saturating_sub(1),
        "down" => (current + 1).min(last),
        _ => return None,
    })
}

fn last_op<'a>(
    snapshot: &'a ValidatedSnapshot,
    node: &SnapshotNode,
    code: u16,
) -> Option<&'a crate::abi::OpRecord> {
    snapshot.ops(node).iter().rev().find(|op| op.code == code)
}

#[cfg(test)]
mod tests {
    mod focus;
    mod shortcuts;
    use super::*;
    use crate::{
        abi::{ChildRecord, NodeRecord, OpRecord, RenderArena},
        resources::ResourceKey,
        semantic::{COMPONENT_BUTTON, COMPONENT_TEXT, ValueKind},
        snapshot::{RetainedStrings, SnapshotScratch},
    };
    use gpui::AppContext;

    fn key() -> ResourceKey {
        ResourceKey::new(4, "service-grid".into())
    }

    #[gpui::test]
    fn overlay_focus_request_survives_superseded_and_shadowed_registrations(
        cx: &mut gpui::TestAppContext,
    ) {
        cx.update(|cx| {
            let mut state = OverlayFocusState {
                focus: cx.focus_handle(),
                previous_focus: None,
                focus_pending: true,
            };
            let stack = OverlayStack::default();
            let old = stack.register(key(), OverlayKind::Overlay, 10, true);
            stack.begin_frame();
            let current = stack.register(key(), OverlayKind::Overlay, 10, true);
            assert!(!state.take_pending_focus(&stack, &old));
            assert!(state.focus_pending);
            let menu = stack.register(
                ResourceKey::new(4, "menu".into()),
                OverlayKind::PopoverMenu,
                20,
                true,
            );
            assert!(!state.take_pending_focus(&stack, &current));
            assert!(state.focus_pending);
            stack.set_captures_input(&menu, false);
            assert!(state.take_pending_focus(&stack, &current));
            assert!(!state.take_pending_focus(&stack, &current));
        });
    }

    fn inert_callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            click: None,
            list_render_range: None,
            control_event: None,
            application_started: None,
            window_closed: None,
            menu_action: None,
            dynamic_frame: None,
            render_completed: None,
            release_artifact: None,
            accept_artifact: None,
        }
    }

    fn style_snapshot(component: u16, data: &str, ops: &mut [OpRecord]) -> ValidatedSnapshot {
        let mut nodes = [NodeRecord {
            component,
            data_length: data.len() as u32,
            ..Default::default()
        }];
        let mut bytes = data.as_bytes().to_vec();
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: 1,
            node_capacity: 1,
            ops: ops.as_mut_ptr(),
            op_length: ops.len() as i32,
            op_capacity: ops.len() as i32,
            children: std::ptr::null_mut(),
            child_length: 0,
            child_capacity: 0,
            utf8: bytes.as_mut_ptr(),
            utf8_length: bytes.len() as i32,
            utf8_capacity: bytes.len() as i32,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };
        let mut snapshot = ValidatedSnapshot::default();
        snapshot
            .decode_into(
                &arena,
                0,
                &mut RetainedStrings::default(),
                &mut SnapshotScratch::default(),
            )
            .unwrap();
        snapshot
    }

    fn float_style(code: u16, value: f32) -> OpRecord {
        OpRecord {
            code,
            value_kind: ValueKind::F32 as u16,
            a: value.to_bits() as u64,
            ..Default::default()
        }
    }

    #[gpui::test]
    fn keyboard_focus_does_not_change_authored_control_geometry(cx: &mut gpui::TestAppContext) {
        use std::{cell::Cell, rc::Rc};
        struct FocusProbe {
            focus: FocusHandle,
            bounds: Rc<Cell<gpui::Bounds<gpui::Pixels>>>,
            snapshot: ValidatedSnapshot,
        }
        impl gpui::Render for FocusProbe {
            fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
                let bounds = self.bounds.clone();
                let element =
                    components::button("focus-probe".into(), false, NativeTheme::default())
                        .track_focus(&self.focus)
                        .w(px(240.))
                        .h(px(64.))
                        .border(px(6.))
                        .p(px(8.))
                        .child(
                            canvas(move |value, _, _| bounds.set(value), |_, _, _, _| {})
                                .size_full(),
                        );
                apply_interaction_styles(
                    element,
                    &self.snapshot.nodes[0],
                    &self.snapshot,
                    NativeTheme::default(),
                )
            }
        }
        let bounds = Rc::new(Cell::new(gpui::Bounds::default()));
        let (view, cx) = cx.add_window_view(|_, cx| FocusProbe {
            focus: cx.focus_handle(),
            bounds: bounds.clone(),
            snapshot: style_snapshot(crate::semantic::COMPONENT_BUTTON, "focus-probe", &mut []),
        });
        cx.simulate_resize(gpui::size(px(320.), px(160.)));
        cx.update(|window, _| window.refresh());
        let initial = bounds.get();
        assert_eq!(initial.size, gpui::size(px(212.), px(36.)));
        cx.simulate_keystrokes("tab");
        cx.update(|window, app| {
            let focus = view.read(app).focus.clone();
            focus.focus(window, app);
            assert!(window.last_input_was_keyboard());
            window.refresh();
        });
        assert_eq!(bounds.get(), initial);
    }

    #[gpui::test]
    fn growing_elements_preserve_explicit_minima_in_native_layout(cx: &mut gpui::TestAppContext) {
        let view = cx.new(|_| {
            ManagedView::new(1, inert_callbacks(), Default::default(), Default::default())
        });
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        for grow_first in [true, false] {
            for (width, height, expected) in [
                (
                    OP_MIN_WIDTH_PX,
                    OP_MIN_HEIGHT_PX,
                    gpui::size(px(240.), px(80.)),
                ),
                (
                    OP_MIN_WIDTH_PERCENT,
                    OP_MIN_HEIGHT_PERCENT,
                    gpui::size(px(480.), px(160.)),
                ),
            ] {
                let mut ops = vec![
                    float_style(OP_WIDTH_PX, 10.),
                    float_style(OP_HEIGHT_PX, 10.),
                ];
                if grow_first {
                    ops.push(float_style(OP_FLEX_GROW, 1.));
                }
                ops.extend([float_style(width, 240.), float_style(height, 80.)]);
                if !grow_first {
                    ops.push(float_style(OP_FLEX_GROW, 1.));
                }
                let snapshot = style_snapshot(crate::semantic::COMPONENT_DIV, "", &mut ops);
                cx.draw(
                    gpui::Point::default(),
                    gpui::size(px(200.), px(200.)),
                    |window, cx| {
                        view.update(cx, |view, cx| {
                            let mut element = view.materialize_node(0, &snapshot, window, cx);
                            let size = element.layout_as_root(
                                gpui::size(
                                    gpui::AvailableSpace::Definite(px(200.)),
                                    gpui::AvailableSpace::Definite(px(200.)),
                                ),
                                window,
                                cx,
                            );
                            assert_eq!(size, expected);
                        });
                        div()
                    },
                );
            }
        }
    }

    #[gpui::test]
    fn paired_foregrounds_reach_nested_text_paint_and_preserve_literal_children(
        cx: &mut gpui::TestAppContext,
    ) {
        use std::{cell::Cell, rc::Rc};

        fn text_probe(observed: Rc<Cell<Hsla>>) -> impl IntoElement {
            canvas(
                |_, window, _| {
                    let run = window.text_style().to_run(4);
                    let color = run.color;
                    let line =
                        window
                            .text_system()
                            .shape_line("text".into(), px(14.), &[run], None);
                    (color, line)
                },
                move |bounds, (color, line), window, cx| {
                    line.paint(bounds.origin, px(20.), TextAlign::Left, None, window, cx)
                        .unwrap();
                    observed.set(color);
                },
            )
            .w(px(60.))
            .h(px(20.))
        }

        struct PaintProbe {
            snapshot: ValidatedSnapshot,
            inherited: Rc<Cell<Hsla>>,
            literal: Rc<Cell<Hsla>>,
            disabled: bool,
        }
        impl gpui::Render for PaintProbe {
            fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
                let theme = NativeTheme::default();
                let node = &self.snapshot.nodes[0];
                let button = components::button("paired-paint".into(), self.disabled, theme)
                    .w(px(200.))
                    .h(px(80.))
                    .on_click(|_, _, _| {})
                    .child(div().child(div().child(text_probe(self.inherited.clone()))))
                    .child(
                        div()
                            .text_color(rgba(0x998877FF))
                            .child(text_probe(self.literal.clone())),
                    );
                let mut button = apply_styles(button, node, &self.snapshot);
                if !self.disabled {
                    button = apply_interaction_styles(button, node, &self.snapshot, theme);
                }
                presentation::disabled(button, self.disabled)
            }
        }

        let inherited = Rc::new(Cell::new(Hsla::default()));
        let literal = Rc::new(Cell::new(Hsla::default()));
        let mut ops = [
            (OP_BACKGROUND_RGBA, 0x112233FF),
            (OP_TEXT_RGBA, 0xAABBCCFF),
            (OP_HOVER_BACKGROUND_RGBA, 0x223344FF),
            (OP_HOVER_TEXT_RGBA, 0xBBCCDDFF),
            (OP_ACTIVE_BACKGROUND_RGBA, 0x334455FF),
            (OP_ACTIVE_TEXT_RGBA, 0xCCDDEEFF),
        ]
        .map(|(code, a)| OpRecord {
            code,
            a,
            value_kind: ValueKind::U32 as u16,
            ..Default::default()
        });
        let (view, cx) = cx.add_window_view(|_, _| PaintProbe {
            snapshot: style_snapshot(COMPONENT_BUTTON, "paired-paint", &mut ops),
            inherited: inherited.clone(),
            literal: literal.clone(),
            disabled: false,
        });
        cx.simulate_resize(gpui::size(px(320.), px(160.)));
        let outside = point(px(300.), px(140.));
        let inside = point(px(100.), px(40.));
        let assert_colors = |color| {
            assert_eq!(inherited.get(), rgba(color).into());
            assert_eq!(literal.get(), rgba(0x998877FF).into());
        };
        cx.simulate_mouse_move(outside, None, gpui::Modifiers::none());
        assert_colors(0xAABBCCFF);
        cx.simulate_mouse_move(inside, None, gpui::Modifiers::none());
        assert_colors(0xBBCCDDFF);
        cx.simulate_mouse_down(inside, MouseButton::Left, gpui::Modifiers::none());
        assert_colors(0xCCDDEEFF);
        cx.simulate_mouse_move(outside, MouseButton::Left, gpui::Modifiers::none());
        cx.simulate_mouse_up(outside, MouseButton::Left, gpui::Modifiers::none());
        assert_colors(0xAABBCCFF);

        view.update(cx, |view, cx| {
            view.disabled = true;
            cx.notify();
        });
        cx.simulate_mouse_move(inside, None, gpui::Modifiers::none());
        cx.simulate_mouse_down(inside, MouseButton::Left, gpui::Modifiers::none());
        assert_colors(0xAABBCCFF);
        cx.simulate_mouse_up(inside, MouseButton::Left, gpui::Modifiers::none());

        view.update(cx, |view, cx| {
            view.snapshot = style_snapshot(COMPONENT_BUTTON, "paired-paint", &mut []);
            cx.notify();
        });
        cx.update(|window, _| window.refresh());
        assert_colors(NativeTheme::default().text);
    }

    #[gpui::test]
    fn input_text_inherits_through_native_wrappers_and_explicit_style_wins(
        cx: &mut gpui::TestAppContext,
    ) {
        let view = cx.new(|_| {
            ManagedView::new(1, inert_callbacks(), Default::default(), Default::default())
        });
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        for explicit in [false, true, false] {
            let mut ops = vec![OpRecord {
                code: OP_RESOURCE_OWNER,
                value_kind: ValueKind::U32 as u16,
                a: 1,
                ..Default::default()
            }];
            if explicit {
                ops.push(OpRecord {
                    code: OP_TEXT_RGBA,
                    value_kind: ValueKind::U32 as u16,
                    a: 0x224466FF,
                    ..Default::default()
                });
            }
            let snapshot = style_snapshot(
                crate::semantic::COMPONENT_INPUT,
                "field\0value\0hint",
                &mut ops,
            );
            let mut retained_input = None;
            cx.draw(
                gpui::Point::default(),
                gpui::size(px(400.), px(100.)),
                |window, cx| {
                    view.update(cx, |view, cx| {
                        let configuration =
                            input_configuration(&snapshot, &snapshot.nodes[0]).unwrap();
                        retained_input =
                            Some(view.resources.input_resource(&configuration, window, cx));
                        let content = view.materialize_node(0, &snapshot, window, cx);
                        div().text_color(rgba(0xCC8844FF)).child(content)
                    })
                },
            );
            cx.read(|cx| {
                assert_eq!(
                    retained_input.as_ref().unwrap().read(cx).last_paint_color,
                    Some(rgba(if explicit { 0x224466FF } else { 0xCC8844FF }).into())
                );
            });
        }
    }

    #[test]
    fn custom_table_header_counts_are_validated_and_content_does_not_change_row_columns() {
        use crate::native_workloads::WorkloadArena;
        let mut baseline = None;
        for count in [0, 1, 2, 3, 2] {
            let mut arena = WorkloadArena::default();
            let root = arena.node_with_data(
                crate::semantic::COMPONENT_TABLE,
                None,
                "grid\0name\0Name\0size\0Size",
            );
            arena.op(root, OP_RESOURCE_OWNER, 1);
            arena.op(root, OP_TABLE_HEADER_BACKGROUND_RGBA, count as u64);
            arena.op(root, OP_TABLE_HEADER_TEXT_RGBA, (count as u64) << 8);
            arena.op(root, OP_TABLE_HEADER_BORDER_RGBA, (count as u64) << 16);
            arena.op(
                root,
                crate::semantic::OP_TABLE_COLUMN,
                120f32.to_bits() as u64,
            );
            arena.op(
                root,
                crate::semantic::OP_TABLE_COLUMN,
                80f32.to_bits() as u64,
            );
            for index in 0..count {
                arena.node_with_data(
                    crate::semantic::COMPONENT_TEXT,
                    Some(root),
                    if index == 0 { "Name ↑" } else { "Custom" },
                );
            }
            let mut snapshot = ValidatedSnapshot::default();
            let result = arena.decode_into(&mut snapshot);
            if count == 1 || count == 3 {
                assert_eq!(result, Err(-57));
            } else {
                result.unwrap();
                let (_, spec) = table_configuration(&snapshot, &snapshot.nodes[0]).unwrap();
                if let Some(baseline) = &baseline {
                    assert_eq!(baseline, &spec);
                } else {
                    baseline = Some(spec);
                }
            }
        }
    }

    #[gpui::test]
    fn table_header_colors_restore_theme_defaults_and_custom_content_inherits_text(
        cx: &mut gpui::TestAppContext,
    ) {
        use crate::native_workloads::WorkloadArena;
        use std::{cell::RefCell, rc::Rc};
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        for (phase, explicit) in [false, true, true, false].into_iter().enumerate() {
            let mut arena = WorkloadArena::default();
            let root =
                arena.node_with_data(crate::semantic::COMPONENT_TABLE, None, "grid\0a\0A\0b\0B");
            arena.op(root, OP_RESOURCE_OWNER, 1);
            for _ in 0..2 {
                arena.op(
                    root,
                    crate::semantic::OP_TABLE_COLUMN,
                    100f32.to_bits() as u64,
                );
            }
            if explicit {
                arena.op(root, OP_TABLE_HEADER_BACKGROUND_RGBA, 0x111111FF);
                arena.op(root, OP_TABLE_HEADER_BACKGROUND_RGBA, 0x22334480);
                arena.op(root, OP_TABLE_HEADER_TEXT_RGBA, 0x556677FF);
                arena.op(root, OP_TABLE_HEADER_BORDER_RGBA, 0x8899AA40);
            }
            let snapshot = arena.decode();
            let (_, spec) = table_configuration(&snapshot, &snapshot.nodes[0]).unwrap();
            let theme = NativeTheme {
                element_background: 0xAABBCCFF + phase as u32 * 0x100,
                text_muted: 0xBBCCDDFF + phase as u32 * 0x100,
                border_variant: 0xCCDDEEFF + phase as u32 * 0x100,
                ..Default::default()
            };
            let expected_text = if explicit {
                0x556677FF
            } else {
                theme.text_muted
            };
            for custom in [false, true] {
                let seen = Rc::new(RefCell::new(Vec::new()));
                cx.draw(
                    point(px(0.), px(0.)),
                    gpui::size(px(300.), px(100.)),
                    |_, _| {
                        let header = table_header_strip(&spec, theme, px(12.), |index| {
                            if !custom {
                                return None;
                            }
                            let seen = seen.clone();
                            let marker = canvas(
                                move |_, window, _| {
                                    seen.borrow_mut().push(window.text_style().color);
                                },
                                |_, _, _, _| {},
                            )
                            .w(px(10.))
                            .h(px(12.));
                            Some(
                                if index == 0 {
                                    div().child(marker)
                                } else {
                                    div().text_color(rgba(0x123456FF)).child(marker)
                                }
                                .into_any_element(),
                            )
                        });
                        let mut header =
                            apply_table_header_styles(header, &snapshot.nodes[0], &snapshot);
                        assert_eq!(
                            header.style().background,
                            Some(
                                rgba(if explicit {
                                    0x22334480
                                } else {
                                    theme.element_background
                                })
                                .into()
                            )
                        );
                        assert_eq!(
                            header.style().border_color,
                            Some(
                                rgba(if explicit {
                                    0x8899AA40
                                } else {
                                    theme.border_variant
                                })
                                .into()
                            )
                        );
                        assert_eq!(header.style().text.color, Some(rgba(expected_text).into()));
                        div().w_full().child(header)
                    },
                );
                if custom {
                    assert_eq!(
                        *seen.borrow(),
                        vec![rgba(expected_text).into(), rgba(0x123456FF).into()]
                    );
                }
            }
        }
        for code in [
            OP_TABLE_HEADER_BACKGROUND_RGBA,
            OP_TABLE_HEADER_TEXT_RGBA,
            OP_TABLE_HEADER_BORDER_RGBA,
        ] {
            let mut arena = WorkloadArena::default();
            let node = arena.node(crate::semantic::COMPONENT_DIV, None);
            arena.op(node, code, 0);
            assert!(
                arena
                    .decode_into(&mut ValidatedSnapshot::default())
                    .is_err()
            );
        }
    }

    #[gpui::test]
    fn custom_headers_align_with_rows_after_resize_and_allow_taller_content(
        cx: &mut gpui::TestAppContext,
    ) {
        use std::{cell::RefCell, rc::Rc};
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let (_, columns) = crate::resources::parse_table_spec(
            "grid\0a\0A\0b\0B\0c\0C",
            &[
                0.5f32.to_bits() as u64 | (1 << 32),
                100f32.to_bits() as u64 | (1 << 34),
                80f32.to_bits() as u64 | (2 << 34),
            ],
        )
        .unwrap();
        let spec = Rc::new(TableSpec { columns });
        for width in [320., 720., 320.] {
            for gutter in [0., 12.] {
                let bounds = Rc::new(RefCell::new(vec![gpui::Bounds::default(); 6]));
                let marker = |index: usize, height: f32| {
                    let bounds = bounds.clone();
                    canvas(
                        move |value, _, _| {
                            bounds.borrow_mut()[index] = value;
                        },
                        |_, _, _, _| {},
                    )
                    .w(px(10.))
                    .h(px(height))
                    .into_any_element()
                };
                cx.draw(
                    point(px(0.), px(0.)),
                    gpui::size(px(width), px(160.)),
                    |_, _| {
                        let header = table_header_strip(
                            &spec,
                            NativeTheme::default(),
                            px(gutter),
                            |index| Some(marker(index, 48.)),
                        );
                        let mut row = div().flex().flex_row().flex_grow(1.);
                        for (index, column) in spec.columns.iter().enumerate() {
                            row = row.child(
                                apply_column_layout(div().px(px(10.)), column)
                                    .child(marker(index + 3, 20.)),
                            );
                        }
                        div().flex().flex_col().w_full().child(header).child(
                            div()
                                .flex()
                                .flex_row()
                                .child(row)
                                .child(gutter_spacer(px(gutter))),
                        )
                    },
                );
                let bounds = bounds.borrow();
                for index in 0..3 {
                    assert_eq!(bounds[index].origin.x, bounds[index + 3].origin.x);
                    assert_eq!(bounds[index].size.width, bounds[index + 3].size.width);
                    assert!(bounds[index + 3].top() >= bounds[index].bottom());
                    assert_eq!(bounds[index].size.height, px(48.));
                }
            }
        }
    }

    #[gpui::test]
    fn header_button_focus_does_not_navigate_table_rows(cx: &mut gpui::TestAppContext) {
        use crate::native_workloads::WorkloadArena;
        struct HeaderView {
            native: Entity<ManagedView>,
            snapshot: ValidatedSnapshot,
        }
        impl gpui::Render for HeaderView {
            fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
                self.native.update(cx, |view, cx| {
                    view.materialize_node(0, &self.snapshot, window, cx)
                })
            }
        }
        unsafe extern "C" fn unavailable_rows(
            _: u64,
            _: u64,
            _: u64,
            _: u32,
            _: u32,
            _: *mut RenderArena,
            _: *mut u32,
            _: *mut u64,
        ) -> i32 {
            -1
        }
        let mut arena = WorkloadArena::default();
        let table =
            arena.node_with_data(crate::semantic::COMPONENT_TABLE, None, "grid\0name\0Name");
        arena.op(table, OP_RESOURCE_OWNER, 1);
        arena.op(
            table,
            crate::semantic::OP_TABLE_COLUMN,
            200f32.to_bits() as u64,
        );
        arena.op(table, crate::semantic::OP_LIST_RENDERER, 1);
        arena.op(table, crate::semantic::OP_LIST_ITEM_COUNT, 3);
        arena.op(table, OP_WIDTH_PERCENT, 100f32.to_bits() as u64);
        arena.op(table, OP_HEIGHT_PX, 150f32.to_bits() as u64);
        let header = arena.node_with_data(crate::semantic::COMPONENT_BUTTON, Some(table), "sort");
        arena.node_with_data(
            crate::semantic::COMPONENT_TEXT,
            Some(header),
            "Sort services",
        );
        let snapshot = arena.decode();
        let native = cx.new(|_| {
            let mut callbacks = inert_callbacks();
            // Row data is irrelevant to focus routing; keep the real row engine with error rows.
            callbacks.list_render_range = Some(unavailable_rows);
            ManagedView::new(1, callbacks, Default::default(), Default::default())
        });
        let (view, cx) = cx.add_window_view(|_, _| HeaderView {
            native: native.clone(),
            snapshot,
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(300.), px(160.)),
            |_, _| view.clone().into_any_element(),
        );
        let (state, cursor) = cx.update(|_, cx| {
            let configuration =
                list_configuration(&view.read(cx).snapshot, &view.read(cx).snapshot.nodes[0])
                    .unwrap();
            native.update(cx, |native, _| {
                let key = ResourceKey::new(1, "grid".into());
                let engine =
                    native
                        .resources
                        .list_resource(&key, &configuration, native.snapshot_revision);
                let engine = engine.borrow();
                (engine.state.clone(), engine.cursor.clone())
            })
        });
        cx.simulate_mouse_down(
            point(px(25.), px(16.)),
            MouseButton::Left,
            gpui::Modifiers::none(),
        );
        cx.simulate_mouse_up(
            point(px(25.), px(16.)),
            MouseButton::Left,
            gpui::Modifiers::none(),
        );
        let header_focus =
            cx.update(|window, cx| window.focused(cx).expect("header button has focus"));
        cx.simulate_keystrokes("down");
        assert_eq!(cursor.active(), Some(0));
        let first_row = state.bounds_for_item(0).unwrap().center();
        cx.simulate_mouse_down(first_row, MouseButton::Left, gpui::Modifiers::none());
        cx.simulate_mouse_up(first_row, MouseButton::Left, gpui::Modifiers::none());
        cx.update(|window, _| assert!(!header_focus.is_focused(window)));
        cx.simulate_keystrokes("down");
        assert_eq!(cursor.active(), Some(1));
    }

    #[test]
    fn interactive_element_ids_are_scoped_by_managed_view_owner() {
        let alpha = managed_control_state_id(17, "increment");
        let beta = managed_control_state_id(18, "increment");
        let other_key = managed_control_state_id(17, "decrement");

        assert_ne!(alpha, beta);
        assert_ne!(alpha, other_key);
        assert_eq!(alpha, managed_control_state_id(17, "increment"));
    }

    #[test]
    fn key_mouse_modifiers_use_the_click_bit_layout() {
        let modifiers = gpui::Modifiers {
            control: true,
            alt: false,
            shift: true,
            platform: true,
            function: false,
        };
        assert_eq!(modifiers_flags(&modifiers), 0b01101);

        let none = gpui::Modifiers::default();
        assert_eq!(modifiers_flags(&none), 0);
    }

    #[test]
    fn mouse_button_codes_cover_all_gpui_buttons() {
        assert_eq!(mouse_button_code(&MouseButton::Left), 0);
        assert_eq!(mouse_button_code(&MouseButton::Right), 1);
        assert_eq!(mouse_button_code(&MouseButton::Middle), 2);
        assert_eq!(
            mouse_button_code(&MouseButton::Navigate(gpui::NavigationDirection::Back)),
            3
        );
        assert_eq!(
            mouse_button_code(&MouseButton::Navigate(gpui::NavigationDirection::Forward)),
            4
        );
    }

    #[test]
    fn foundation_control_accessible_name_comes_from_semantic_text() {
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_BUTTON,
                data_length: 1,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_TEXT,
                data_offset: 1,
                data_length: 5,
                ..Default::default()
            },
        ];
        let mut children = [ChildRecord {
            parent: 0,
            child: 1,
        }];
        let mut utf8 = *b"xHello";
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: children.as_mut_ptr(),
            child_length: children.len() as i32,
            child_capacity: children.len() as i32,
            utf8: utf8.as_mut_ptr(),
            utf8_length: utf8.len() as i32,
            utf8_capacity: utf8.len() as i32,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };
        let mut snapshot = ValidatedSnapshot::default();
        snapshot
            .decode_into(
                &arena,
                0,
                &mut RetainedStrings::default(),
                &mut SnapshotScratch::default(),
            )
            .unwrap();

        assert_eq!(
            accessibility_label(&snapshot.nodes[0], &snapshot).as_deref(),
            Some("Hello")
        );
    }

    #[test]
    fn explicit_accessible_name_overrides_visible_text() {
        use crate::native_workloads::WorkloadArena;
        for component in [
            COMPONENT_BUTTON,
            crate::semantic::COMPONENT_CHECKBOX,
            crate::semantic::COMPONENT_RADIO,
        ] {
            let mut arena = WorkloadArena::default();
            let root = arena.node_with_data(component, None, "control");
            arena.node_with_data(COMPONENT_TEXT, Some(root), "Visible");
            arena.data_op(root, crate::semantic::OP_ACCESSIBLE_NAME, "First");
            arena.data_op(root, crate::semantic::OP_ACCESSIBLE_NAME, "Explicit");
            let snapshot = arena.decode();
            assert_eq!(
                accessibility_label(&snapshot.nodes[0], &snapshot).as_deref(),
                Some("Explicit")
            );
        }
    }

    #[test]
    fn row_state_ids_are_deterministic_for_identical_inputs() {
        let a = row_state_id(&key(), 12, Some(7), "service-row");
        let b = row_state_id(&key(), 12, Some(7), "service-row");
        assert_eq!(a, b);
    }

    #[test]
    fn keyed_row_identity_survives_rebatching_and_preceding_content_changes() {
        use crate::semantic::COMPONENT_DIV;
        fn decode(preceding_nodes: usize, label: &str) -> (ValidatedSnapshot, u32, u32) {
            let mut nodes = vec![NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            }];
            let mut children = Vec::new();
            for _ in 0..preceding_nodes {
                children.push(ChildRecord {
                    parent: 0,
                    child: nodes.len() as u32,
                });
                nodes.push(NodeRecord {
                    component: COMPONENT_TEXT,
                    ..Default::default()
                });
            }
            let row = nodes.len() as u32;
            nodes.push(NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            });
            nodes.push(NodeRecord {
                component: crate::semantic::COMPONENT_BUTTON,
                data_length: 3,
                ..Default::default()
            });
            nodes.push(NodeRecord {
                component: COMPONENT_TEXT,
                data_offset: 3,
                data_length: label.len() as u32,
                ..Default::default()
            });
            children.extend([
                ChildRecord {
                    parent: 0,
                    child: row,
                },
                ChildRecord {
                    parent: row,
                    child: row + 1,
                },
                ChildRecord {
                    parent: row + 1,
                    child: row + 2,
                },
            ]);
            let mut data = format!("key{label}").into_bytes();
            let mut ops = [OpRecord {
                node: row,
                code: crate::semantic::OP_LIST_ITEM_ID,
                value_kind: crate::semantic::ValueKind::U64 as u16,
                a: 7,
                b: 0,
            }];
            let arena = RenderArena {
                nodes: nodes.as_mut_ptr(),
                node_length: nodes.len() as i32,
                node_capacity: nodes.len() as i32,
                children: children.as_mut_ptr(),
                child_length: children.len() as i32,
                child_capacity: children.len() as i32,
                ops: ops.as_mut_ptr(),
                op_length: 1,
                op_capacity: 1,
                utf8: data.as_mut_ptr(),
                utf8_length: data.len() as i32,
                utf8_capacity: data.len() as i32,
                generation: 1,
                flags: 0,
                required_node_capacity: 0,
                required_op_capacity: 0,
                required_child_capacity: 0,
                required_utf8_capacity: 0,
            };
            let mut snapshot = ValidatedSnapshot::default();
            snapshot
                .decode_into(
                    &arena,
                    0,
                    &mut RetainedStrings::default(),
                    &mut SnapshotScratch::default(),
                )
                .unwrap();
            (snapshot, row, row + 1)
        }
        let (before, row_before, control_before) = decode(1, "old label");
        let (after, row_after, control_after) = decode(9, "new label");
        assert_ne!(control_before, control_after);
        let identity = |snapshot: &ValidatedSnapshot, row, control: u32, position| {
            let model = last_op(
                snapshot,
                &snapshot.nodes[row as usize],
                crate::semantic::OP_LIST_ITEM_ID,
            )
            .unwrap()
            .a;
            row_state_id(
                &key(),
                position,
                Some(model),
                &snapshot.nodes[control as usize].data,
            )
        };
        assert_eq!(
            identity(&before, row_before, control_before, 1),
            identity(&after, row_after, control_after, 9)
        );
        assert_ne!(
            row_state_id(&key(), 7, Some(7), "key"),
            row_state_id(&key(), 7, None, "key")
        );
    }

    #[test]
    fn row_state_ids_distinguish_identity_inputs() {
        let base = row_state_id(&key(), 12, Some(7), "service-row");
        // Different row identity (model ID vs positional fallback).
        assert_ne!(base, row_state_id(&key(), 12, Some(8), "service-row"));
        assert_ne!(base, row_state_id(&key(), 12, None, "service-row"));
        // Different node within the row subtree.
        assert_eq!(base, row_state_id(&key(), 12, Some(7), "service-row"));
        assert_ne!(base, row_state_id(&key(), 12, Some(7), "chevron"));
        // Different list identity.
        assert_ne!(
            base,
            row_state_id(
                &ResourceKey::new(5, "service-grid".into()),
                12,
                Some(7),
                "service-row"
            )
        );
        assert_ne!(
            base,
            row_state_id(
                &ResourceKey::new(4, "other-grid".into()),
                12,
                Some(7),
                "service-row"
            )
        );
    }

    #[test]
    fn collection_keyboard_navigation_is_bounded() {
        assert_eq!(collection_key_target("up", 0, 3), Some(0));
        assert_eq!(collection_key_target("down", 2, 3), Some(2));
        assert_eq!(collection_key_target("home", 2, 3), Some(0));
        assert_eq!(collection_key_target("end", 0, 3), Some(2));
        assert_eq!(collection_key_target("left", 1, 3), None);
        assert_eq!(collection_key_target("down", 0, 0), None);
    }

    struct PagingListView {
        state: ListState,
        heights: Vec<f32>,
        rendered: std::rc::Rc<std::cell::Cell<usize>>,
    }

    struct PointerCollectionView {
        state: ListState,
        cursor: std::rc::Rc<CollectionCursor>,
        focus: FocusHandle,
        clicks: std::rc::Rc<std::cell::Cell<usize>>,
        child_cursor: std::rc::Rc<std::cell::Cell<Option<usize>>>,
        row_events: Option<ListRowEvents>,
    }

    impl gpui::Render for PointerCollectionView {
        fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            let cursor = self.cursor.clone();
            let keyboard_cursor = cursor.clone();
            let focus = self.focus.clone();
            let keyboard_state = self.state.clone();
            let clicks = self.clicks.clone();
            let child_cursor = self.child_cursor.clone();
            let row_events = self.row_events;
            div()
                .id("pointer-collection")
                .track_focus(&focus)
                .flex()
                .flex_col()
                .w(px(200.))
                .h(px(120.))
                .on_key_down(move |event, window, cx| {
                    handle_collection_key_down(
                        event,
                        window,
                        cx,
                        &keyboard_cursor,
                        &keyboard_state,
                        &ScrollInteraction::default(),
                        5,
                    );
                })
                .child(
                    list(self.state.clone(), move |index, _, _| {
                        let clicks = clicks.clone();
                        let child_cursor = child_cursor.clone();
                        let observed_cursor = cursor.clone();
                        let content = div()
                            .id(("row", index))
                            .w_full()
                            .h(px(40.))
                            .on_click(move |_, _, _| clicks.set(clicks.get() + 1))
                            .child(div().w(px(20.)).h(px(20.)).on_mouse_down(
                                MouseButton::Left,
                                move |_, _, cx| {
                                    child_cursor.set(observed_cursor.active());
                                    cx.stop_propagation();
                                },
                            ));
                        CollectionRow::new(
                            content.into_any_element(),
                            cursor.clone(),
                            focus.clone(),
                            index,
                        )
                        .with_row_events(row_events.map(|packet| ListRowEvents {
                            index: index as u32,
                            item_id: Some(1000 + index as u64),
                            ..packet
                        }))
                        .into_any_element()
                    })
                    .w_full()
                    .flex_grow(1.)
                    .min_h_0(),
                )
        }
    }

    #[gpui::test]
    fn collection_pointer_sync_precedes_child_handlers_and_preserves_clicks_and_layout(
        cx: &mut gpui::TestAppContext,
    ) {
        let cursor = std::rc::Rc::new(CollectionCursor::new(5));
        let state = ListState::new(5, gpui::ListAlignment::Top, px(0.)).measure_all();
        let clicks = std::rc::Rc::new(std::cell::Cell::new(0));
        let child_cursor = std::rc::Rc::new(std::cell::Cell::new(None));
        let (view, cx) = cx.add_window_view(|_, cx| PointerCollectionView {
            state: state.clone(),
            cursor: cursor.clone(),
            focus: cx.focus_handle().tab_stop(true),
            clicks: clicks.clone(),
            child_cursor: child_cursor.clone(),
            row_events: None,
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(200.), px(120.)),
            |_, _| view.clone().into_any_element(),
        );
        assert_eq!(state.bounds_for_item(1).unwrap().size.height, px(40.));
        assert_eq!(state.max_offset_for_scrollbar().y, px(80.));

        let position = point(px(100.), px(60.));
        cx.simulate_mouse_down(position, MouseButton::Right, gpui::Modifiers::none());
        cx.simulate_mouse_up(position, MouseButton::Right, gpui::Modifiers::none());
        assert_eq!(cursor.active(), Some(0));
        cx.simulate_mouse_down(position, MouseButton::Left, gpui::Modifiers::none());
        assert_eq!(cursor.active(), Some(1));
        cx.simulate_mouse_up(position, MouseButton::Left, gpui::Modifiers::none());
        assert_eq!(clicks.get(), 1);
        cx.simulate_keystrokes("down");
        assert_eq!(cursor.active(), Some(2));
        assert_eq!(clicks.get(), 1); // Cursor movement does not activate a row.

        // Clicking a child that consumes mouse-down still updates the collection cursor first.
        cx.simulate_mouse_down(
            point(px(10.), px(10.)),
            MouseButton::Left,
            gpui::Modifiers::none(),
        );
        assert_eq!(child_cursor.get(), Some(0));
        assert_eq!(cursor.active(), Some(0));
    }

    #[gpui::test]
    fn collection_double_press_activation_respects_child_consumption_and_modifiers(
        cx: &mut gpui::TestAppContext,
    ) {
        thread_local! {
            static ACTIVATIONS: std::cell::RefCell<Vec<(u32, u64)>> = const { std::cell::RefCell::new(Vec::new()) };
        }
        unsafe extern "C" fn activate(_: u64, token: u64, event: *const NativeControlEvent) -> i32 {
            let event = unsafe { &*event };
            assert_eq!(token, 42);
            assert_eq!(event.flags, 2);
            assert_eq!(event.revision, 7);
            let bytes =
                unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
            let index = u32::from_le_bytes(bytes[..4].try_into().unwrap());
            let id = u64::from_le_bytes(bytes[8..16].try_into().unwrap());
            ACTIVATIONS.with_borrow_mut(|events| events.push((index, id)));
            0
        }
        ACTIVATIONS.with_borrow_mut(Vec::clear);
        let cursor = std::rc::Rc::new(CollectionCursor::new(5));
        let (view, cx) = cx.add_window_view(|_, cx| PointerCollectionView {
            state: ListState::new(5, gpui::ListAlignment::Top, px(0.)).measure_all(),
            cursor: cursor.clone(),
            focus: cx.focus_handle().tab_stop(true),
            clicks: Default::default(),
            child_cursor: Default::default(),
            row_events: Some(ListRowEvents {
                session_id: 1,
                callbacks: ManagedCallbacks {
                    control_event: Some(activate),
                    ..inert_callbacks()
                },
                activation_token: 42,
                selection_token: 0,
                index: 0,
                item_id: None,
                content_revision: Some(7),
            }),
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(200.), px(120.)),
            |_, _| view.clone().into_any_element(),
        );
        let body = point(px(100.), px(60.));
        cx.simulate_mouse_down(body, MouseButton::Left, gpui::Modifiers::none());
        cx.simulate_mouse_up(body, MouseButton::Left, gpui::Modifiers::none());
        ACTIVATIONS.with_borrow(|events| assert!(events.is_empty()));
        cx.simulate_event(MouseDownEvent {
            position: body,
            button: MouseButton::Left,
            click_count: 2,
            ..Default::default()
        });
        ACTIVATIONS.with_borrow(|events| assert_eq!(events.as_slice(), &[(1, 1001)]));
        cx.simulate_event(MouseDownEvent {
            position: point(px(10.), px(10.)),
            button: MouseButton::Left,
            click_count: 2,
            ..Default::default()
        });
        assert_eq!(cursor.active(), Some(0));
        cx.simulate_event(MouseDownEvent {
            position: body,
            button: MouseButton::Left,
            click_count: 2,
            modifiers: gpui::Modifiers {
                shift: true,
                ..Default::default()
            },
            ..Default::default()
        });
        cx.simulate_event(MouseDownEvent {
            position: body,
            button: MouseButton::Right,
            click_count: 2,
            ..Default::default()
        });
        ACTIVATIONS.with_borrow(|events| assert_eq!(events.len(), 1));
    }

    #[gpui::test]
    fn collection_selection_press_preserves_clicks_and_rejects_child_modified_and_stale_events(
        cx: &mut gpui::TestAppContext,
    ) {
        thread_local! {
            static REQUESTS: std::cell::RefCell<Vec<(u16, u32)>> = const { std::cell::RefCell::new(Vec::new()) };
        }
        unsafe extern "C" fn receive(_: u64, token: u64, event: *const NativeControlEvent) -> i32 {
            let event = unsafe { &*event };
            assert_eq!(event.flags, 2);
            assert_eq!(event.revision, 7);
            assert_eq!(
                token,
                if event.kind == crate::semantic::EVENT_LIST_ACTIVATED {
                    42
                } else {
                    43
                }
            );
            let bytes =
                unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) };
            let index = u32::from_le_bytes(bytes[..4].try_into().unwrap());
            assert_eq!(
                u64::from_le_bytes(bytes[8..].try_into().unwrap()),
                1000 + index as u64
            );
            REQUESTS.with_borrow_mut(|events| events.push((event.kind, index)));
            0
        }
        REQUESTS.with_borrow_mut(Vec::clear);
        let cursor = std::rc::Rc::new(CollectionCursor::new(5));
        let clicks = std::rc::Rc::new(std::cell::Cell::new(0));
        let (view, cx) = cx.add_window_view(|_, cx| PointerCollectionView {
            state: ListState::new(5, gpui::ListAlignment::Top, px(0.)).measure_all(),
            cursor: cursor.clone(),
            focus: cx.focus_handle().tab_stop(true),
            clicks: clicks.clone(),
            child_cursor: Default::default(),
            row_events: Some(ListRowEvents {
                session_id: 1,
                callbacks: ManagedCallbacks {
                    control_event: Some(receive),
                    ..inert_callbacks()
                },
                activation_token: 42,
                selection_token: 43,
                index: 0,
                item_id: None,
                content_revision: Some(7),
            }),
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(200.), px(120.)),
            |_, _| view.clone().into_any_element(),
        );
        let body = point(px(100.), px(60.));
        cx.simulate_mouse_down(body, MouseButton::Left, gpui::Modifiers::none());
        cx.simulate_mouse_up(body, MouseButton::Left, gpui::Modifiers::none());
        assert_eq!(clicks.get(), 1);
        assert_eq!(cursor.active(), Some(1));
        REQUESTS.with_borrow(|events| {
            assert_eq!(
                events.as_slice(),
                &[(crate::semantic::EVENT_LIST_SELECTION_REQUESTED, 1)]
            )
        });
        cx.simulate_event(MouseDownEvent {
            position: body,
            button: MouseButton::Left,
            click_count: 2,
            ..Default::default()
        });
        REQUESTS.with_borrow(|events| {
            assert_eq!(events[1], (crate::semantic::EVENT_LIST_ACTIVATED, 1))
        });
        cx.simulate_mouse_down(
            point(px(10.), px(10.)),
            MouseButton::Left,
            gpui::Modifiers::none(),
        );
        assert_eq!(cursor.active(), Some(0));
        for modifiers in [
            gpui::Modifiers {
                shift: true,
                ..Default::default()
            },
            gpui::Modifiers {
                control: true,
                ..Default::default()
            },
            gpui::Modifiers {
                alt: true,
                ..Default::default()
            },
            gpui::Modifiers {
                platform: true,
                ..Default::default()
            },
            gpui::Modifiers {
                function: true,
                ..Default::default()
            },
        ] {
            cx.simulate_mouse_down(body, MouseButton::Left, modifiers);
        }
        cx.simulate_mouse_down(body, MouseButton::Right, gpui::Modifiers::none());
        REQUESTS.with_borrow(|events| assert_eq!(events.len(), 2));
        cursor.invalidate_rows();
        cx.simulate_mouse_down(body, MouseButton::Left, gpui::Modifiers::none());
        REQUESTS.with_borrow(|events| assert_eq!(events.len(), 2));
    }

    impl gpui::Render for PagingListView {
        fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            let heights = self.heights.clone();
            let rendered = self.rendered.clone();
            list(self.state.clone(), move |index, _, _| {
                rendered.set(rendered.get() + 1);
                div().h(px(heights[index])).w_full().into_any_element()
            })
            .w_full()
            .h_full()
        }
    }

    fn draw_paging_list(
        cx: &mut gpui::VisualTestContext,
        state: &ListState,
        heights: &[f32],
        viewport_height: f32,
    ) {
        let view = cx.new(|_| PagingListView {
            state: state.clone(),
            heights: heights.to_vec(),
            rendered: Default::default(),
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(200.), px(viewport_height)),
            |_, _| view.into_any_element(),
        );
    }

    fn assert_list_offset(state: &ListState, index: usize, offset: f32) {
        let actual = state.logical_scroll_top();
        assert_eq!(actual.item_ix, index);
        assert_eq!(actual.offset_in_item, px(offset));
    }

    #[gpui::test]
    fn collection_paging_uses_measured_heights_partial_rows_and_resized_viewport(
        cx: &mut gpui::TestAppContext,
    ) {
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let heights = [20., 80., 30., 120., 50., 200.];
        let state = ListState::new(heights.len(), gpui::ListAlignment::Top, px(0.)).measure_all();
        draw_paging_list(cx, &state, &heights, 125.);
        assert_eq!(page_collection(&state, true), Some(2));
        assert_list_offset(&state, 2, 25.);
        assert_eq!(page_collection(&state, true), Some(4));
        assert_list_offset(&state, 4, 0.);
        assert_eq!(page_collection(&state, false), Some(2));
        assert_list_offset(&state, 2, 25.);

        draw_paging_list(cx, &state, &heights, 75.);
        assert_eq!(page_collection(&state, true), Some(3));
        assert_list_offset(&state, 3, 70.);
        assert_eq!(page_collection(&state, false), Some(2));
        assert_list_offset(&state, 2, 25.);
    }

    #[gpui::test]
    fn collection_paging_uses_unmeasured_hints_without_requesting_rows(
        cx: &mut gpui::TestAppContext,
    ) {
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let state = ListState::new(20_000, gpui::ListAlignment::Top, px(0.))
            .with_uniform_item_height(px(20.));
        let calls = std::rc::Rc::new(std::cell::Cell::new(0));
        let view = cx.new(|_| PagingListView {
            state: state.clone(),
            heights: vec![20.; 20_000],
            rendered: calls.clone(),
        });
        cx.draw(
            point(px(0.), px(0.)),
            gpui::size(px(200.), px(95.)),
            |_, _| view.into_any_element(),
        );
        // Mirror the retained resource's post-layout restoration after the initial width change.
        state.clone().with_uniform_item_height(px(20.));
        let before = calls.get();
        assert!(before < 20);
        state.scroll_to(ListOffset {
            item_ix: 12,
            offset_in_item: px(7.),
        });
        assert_eq!(page_collection(&state, true), Some(17));
        assert_list_offset(&state, 17, 2.);
        assert_eq!(page_collection(&state, false), Some(12));
        assert_list_offset(&state, 12, 7.);
        assert_eq!(calls.get(), before);

        // A shrinking datasource clamps against its new native count and scroll range.
        state.reset_with_uniform_height(3, px(20.));
        assert_eq!(page_collection(&state, true), Some(0));
        assert_list_offset(&state, 0, 0.);
        state.reset(0);
        assert_eq!(page_collection(&state, false), None);
    }

    #[gpui::test]
    fn collection_paging_traverses_tall_rows_and_clamps_both_ends(cx: &mut gpui::TestAppContext) {
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let heights = [400., 20., 20.];
        let state = ListState::new(3, gpui::ListAlignment::Top, px(0.)).measure_all();
        draw_paging_list(cx, &state, &heights, 100.);
        for offset in [100., 200., 300., 340., 340.] {
            assert_eq!(page_collection(&state, true), Some(0));
            assert_list_offset(&state, 0, offset);
        }
        for offset in [240., 140., 40., 0., 0.] {
            assert_eq!(page_collection(&state, false), Some(0));
            assert_list_offset(&state, 0, offset);
        }
    }

    #[gpui::test]
    fn collection_paging_key_handler_preserves_offset_and_cancels_wheel_easing(
        cx: &mut gpui::TestAppContext,
    ) {
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let state = ListState::new(3, gpui::ListAlignment::Top, px(0.)).measure_all();
        draw_paging_list(cx, &state, &[400., 20., 20.], 100.);
        let cursor = CollectionCursor::new(3);
        cursor.set(2);
        let interaction = ScrollInteraction::default();
        interaction.remaining.set(point(px(0.), px(50.)));
        let mut event = KeyDownEvent {
            keystroke: gpui::Keystroke::parse("pagedown").unwrap(),
            is_held: true,
            prefer_character_input: false,
        };
        cx.update(|window, cx| {
            handle_collection_key_down(&event, window, cx, &cursor, &state, &interaction, 3);
            assert_eq!(cursor.active(), Some(0));
            assert_list_offset(&state, 0, 100.);
            assert_eq!(interaction.remaining.get(), gpui::Point::default());

            interaction.remaining.set(point(px(0.), px(50.)));
            event.keystroke.modifiers.control = true;
            handle_collection_key_down(&event, window, cx, &cursor, &state, &interaction, 3);
            assert_list_offset(&state, 0, 100.);
            assert_eq!(interaction.remaining.get().y, px(50.));

            event.keystroke = gpui::Keystroke::parse("down").unwrap();
            handle_collection_key_down(&event, window, cx, &cursor, &state, &interaction, 3);
            assert_eq!(cursor.active(), Some(1));
            assert_eq!(interaction.remaining.get(), gpui::Point::default());
        });
    }

    #[gpui::test]
    fn collection_paging_handles_bottom_alignment_and_missing_viewports(
        cx: &mut gpui::TestAppContext,
    ) {
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);
        let state = ListState::new(10, gpui::ListAlignment::Bottom, px(0.)).measure_all();
        assert_eq!(page_collection(&state, true), None);
        assert_list_offset(&state, 10, 0.);
        draw_paging_list(cx, &state, &[20.; 10], 60.);
        assert_eq!(page_collection(&state, false), Some(4));
        assert_list_offset(&state, 4, 0.);
        assert_eq!(page_collection(&state, true), Some(7));
        assert_list_offset(&state, 7, 0.);
        assert_eq!(page_collection(&state, true), Some(7));
        assert_list_offset(&state, 7, 0.);
        draw_paging_list(cx, &state, &[20.; 10], 0.);
        let before = state.logical_scroll_top();
        assert_eq!(page_collection(&state, true), None);
        assert_list_offset(&state, before.item_ix, f32::from(before.offset_in_item));
    }

    #[test]
    fn drawing_commands_preserve_path_boundaries_and_outlive_replaced_snapshots() {
        use crate::native_workloads::WorkloadArena;
        let mut arena = WorkloadArena::default();
        let root = arena.node(crate::semantic::COMPONENT_DRAWING, None);
        arena.node(crate::semantic::COMPONENT_PATH, Some(root));
        let first = arena.node(crate::semantic::COMPONENT_PATH, Some(root));
        arena.node(crate::semantic::COMPONENT_PATH, Some(root));
        let second = arena.node(crate::semantic::COMPONENT_PATH, Some(root));
        arena.node(crate::semantic::COMPONENT_PATH, Some(root));
        // Arena operations need not follow child order or be grouped by node.
        arena.op(second, OP_PATH_FILL_RGBA, 0x445566FF);
        arena.op(first, OP_PATH_STROKE_RGBA, 0x112233FF);
        arena.point(first, OP_PATH_MOVE_TO, 1., 2.);
        arena.point(second, OP_PATH_MOVE_TO, 3., 4.);
        arena.point(second, OP_PATH_LINE_TO, 5., 6.);
        arena.op(first, OP_PATH_STROKE_WIDTH_PX, 2f32.to_bits() as u64);
        arena.point(first, OP_PATH_LINE_TO, 7., 8.);
        let mut snapshot = arena.decode();
        let commands = snapshot.drawing_commands(&snapshot.nodes[0]);
        drawing_frame_arena(1, 64, true)
            .decode_into(&mut snapshot)
            .unwrap();
        drop(snapshot);

        let mut paths = commands.paths();
        let stroke = paths.next().unwrap();
        assert_eq!(stroke.len(), 4);
        assert!(stroke.iter().all(|op| op.node == first));
        assert_eq!(
            last_op_in(stroke, OP_PATH_STROKE_RGBA).unwrap().a,
            0x112233FF
        );
        assert!(last_op_in(stroke, OP_PATH_FILL_RGBA).is_none());
        assert_eq!(op_f32x2(&stroke[1]), (1., 2.));
        assert_eq!(op_f32x2(&stroke[3]), (7., 8.));
        let fill = paths.next().unwrap();
        assert_eq!(fill.len(), 3);
        assert!(fill.iter().all(|op| op.node == second));
        assert_eq!(last_op_in(fill, OP_PATH_FILL_RGBA).unwrap().a, 0x445566FF);
        assert!(last_op_in(fill, OP_PATH_STROKE_RGBA).is_none());
        assert_eq!(op_f32x2(&fill[1]), (3., 4.));
        assert_eq!(op_f32x2(&fill[2]), (5., 6.));
        assert!(paths.next().is_none());

        let empty = drawing_frame_snapshot(0, 0, false);
        assert!(
            empty
                .drawing_commands(&empty.nodes[0])
                .paths()
                .next()
                .is_none()
        );
    }

    #[test]
    #[ignore = "opt-in Release measurement; run eng/measure-native.ps1"]
    fn native_workload_measurements_drawing_preparation() {
        use crate::native_workloads::{WorkloadArena, measure};
        for (paths, segments) in [(1, 64), (64, 64), (64, 512)] {
            let mut arena = WorkloadArena::default();
            let root = arena.node(crate::semantic::COMPONENT_DRAWING, None);
            arena.point(root, OP_DRAWING_VIEW_BOX_ORIGIN, 0., 0.);
            arena.point(root, OP_DRAWING_VIEW_BOX_SIZE, 512., 128.);
            for _ in 0..paths {
                let path = arena.node(crate::semantic::COMPONENT_PATH, Some(root));
                arena.op(path, OP_PATH_STROKE_RGBA, 0x112233FF);
                arena.op(path, OP_PATH_STROKE_WIDTH_PX, 2f32.to_bits() as u64);
                arena.point(path, OP_PATH_MOVE_TO, 0., 0.);
                for segment in 1..=segments {
                    arena.point(path, OP_PATH_LINE_TO, segment as f32, (segment % 17) as f32);
                }
            }
            let snapshot = arena.decode();
            let node = &snapshot.nodes[0];
            let operations = snapshot
                .children(node)
                .iter()
                .map(|child| snapshot.ops(&snapshot.nodes[*child as usize]))
                .collect::<Vec<_>>();
            let copied_bytes = operations
                .iter()
                .map(|ops| std::mem::size_of_val(*ops))
                .sum::<usize>();
            println!(
                "drawing paths={paths} segments={segments} copied_command_bytes={copied_bytes} snapshot_buffers={}",
                snapshot.buffer_capacity_bytes()
            );
            // Drop the concrete canvas so its captures are released each iteration. AnyElement
            // lives in GPUI's element arena even after its handle is dropped outside a frame.
            measure("drawing-canvas-prepare-and-drop", 64, || {
                std::hint::black_box(prepare_drawing(
                    std::hint::black_box(0),
                    std::hint::black_box(&snapshot),
                ));
            });
            for (width, height) in [(512., 128.), (1024., 256.)] {
                let bounds =
                    gpui::Bounds::new(point(px(0.), px(0.)), gpui::size(px(width), px(height)));
                let view_box = Some(DrawingViewBox {
                    x: 0.,
                    y: 0.,
                    width: 512.,
                    height: 128.,
                });
                assert!(
                    build_drawing_path(
                        operations[0],
                        bounds,
                        view_box,
                        DrawingPaint::Stroke { width: 2. }
                    )
                    .is_some()
                );
                measure(&format!("drawing-tessellate-{width}x{height}"), 16, || {
                    for ops in &operations {
                        std::hint::black_box(build_drawing_path(
                            std::hint::black_box(ops),
                            std::hint::black_box(bounds),
                            view_box,
                            DrawingPaint::Stroke { width: 2. },
                        ));
                    }
                });
            }
        }
    }

    struct DrawingFrameWorkload {
        snapshots: [ValidatedSnapshot; 2],
        active: usize,
        renders: std::rc::Rc<std::cell::Cell<usize>>,
        viewport: std::rc::Rc<std::cell::Cell<gpui::Size<Pixels>>>,
    }

    impl gpui::Render for DrawingFrameWorkload {
        fn render(&mut self, window: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            self.renders.set(self.renders.get() + 1);
            self.viewport.set(window.viewport_size());
            let snapshot = &self.snapshots[self.active];
            materialize_drawing(0, snapshot)
        }
    }

    fn drawing_frame_snapshot(paths: usize, segments: usize, changed: bool) -> ValidatedSnapshot {
        drawing_frame_arena(paths, segments, changed).decode()
    }

    fn drawing_frame_arena(
        paths: usize,
        segments: usize,
        changed: bool,
    ) -> crate::native_workloads::WorkloadArena {
        use crate::native_workloads::WorkloadArena;
        let mut arena = WorkloadArena::default();
        let root = arena.node(crate::semantic::COMPONENT_DRAWING, None);
        arena.point(root, OP_DRAWING_VIEW_BOX_ORIGIN, 0., 0.);
        arena.point(root, OP_DRAWING_VIEW_BOX_SIZE, 512., 128.);
        arena.op(root, OP_WIDTH_PERCENT, 100f32.to_bits() as u64);
        arena.op(root, OP_HEIGHT_PERCENT, 100f32.to_bits() as u64);
        for _ in 0..paths {
            let path = arena.node(crate::semantic::COMPONENT_PATH, Some(root));
            arena.op(
                path,
                OP_PATH_STROKE_RGBA,
                if changed { 0x445566FF } else { 0x112233FF },
            );
            arena.op(path, OP_PATH_STROKE_WIDTH_PX, 2f32.to_bits() as u64);
            arena.point(path, OP_PATH_MOVE_TO, 0., 0.);
            for segment in 1..=segments {
                arena.point(
                    path,
                    OP_PATH_LINE_TO,
                    segment as f32,
                    (segment % 17) as f32 + if changed { 8. } else { 0. },
                );
            }
        }
        arena
    }

    #[gpui::test]
    #[ignore = "opt-in Release measurement; run eng/measure-native.ps1"]
    fn native_workload_measurements_drawing_frames(cx: &mut gpui::TestAppContext) {
        use crate::native_workloads::measure;
        for (paths, segments) in [(0, 0), (1, 64), (64, 64), (64, 512)] {
            let renders = std::rc::Rc::new(std::cell::Cell::new(0));
            let viewport = std::rc::Rc::new(std::cell::Cell::new(gpui::Size::default()));
            let (view, window_cx) = cx.add_window_view(|_, _| DrawingFrameWorkload {
                snapshots: [
                    drawing_frame_snapshot(paths, segments, false),
                    drawing_frame_snapshot(paths, segments, true),
                ],
                active: 0,
                renders: renders.clone(),
                viewport: viewport.clone(),
            });
            for (width, height, replace) in [
                (512., 128., false),
                (1024., 256., false),
                (512., 128., true),
            ] {
                window_cx.simulate_resize(gpui::size(px(width), px(height)));
                let before = renders.get();
                let mut frames = 0;
                measure(
                    &format!("drawing-frame-{paths}x{segments}-{width}x{height}-replace-{replace}"),
                    16,
                    || {
                        window_cx.update(|window, cx| {
                            if replace {
                                view.update(cx, |view, _| {
                                    view.active ^= 1;
                                    view.snapshots[view.active].clear_drawing_cache();
                                });
                            }
                            // Force native materialization on each frame while keeping snapshot input
                            // stable unless the case explicitly swaps the predecoded description.
                            window.refresh();
                            window.draw(cx).clear(cx);
                        });
                        frames += 1;
                    },
                );
                assert_eq!(
                    renders.get() - before,
                    frames,
                    "the native view must render every measured frame"
                );
                assert_eq!(viewport.get(), gpui::size(px(width), px(height)));
                window_cx.update(|_, cx| {
                    let view = view.read(cx);
                    println!(
                        "drawing-cache-{paths}x{segments}: retained_geometry_bytes={} free_command_buffer_bytes={}",
                        view.snapshots[view.active].drawing_cache_bytes(),
                        view.snapshots[view.active].drawing_command_pool_bytes()
                    );
                });
            }
        }
    }

    #[gpui::test]
    fn drawing_cache_rebuilds_for_resize_and_decoded_style_and_geometry_changes(
        cx: &mut gpui::TestAppContext,
    ) {
        let (view, window_cx) = cx.add_window_view(|_, _| DrawingFrameWorkload {
            snapshots: [
                drawing_frame_snapshot(1, 64, false),
                ValidatedSnapshot::default(),
            ],
            active: 0,
            renders: Default::default(),
            viewport: Default::default(),
        });
        let draw = |cx: &mut gpui::VisualTestContext| {
            cx.update(|window, cx| {
                window.refresh();
                window.draw(cx).clear(cx);
            })
        };
        window_cx.simulate_resize(gpui::size(px(512.), px(128.)));
        draw(window_cx);
        draw(window_cx);
        let before = window_cx.update(|_, cx| {
            view.read(cx).snapshots[0]
                .drawing_cache()
                .borrow()
                .cached_paths(0)
                .unwrap()
        });
        draw(window_cx);
        let hit = window_cx.update(|_, cx| {
            view.read(cx).snapshots[0]
                .drawing_cache()
                .borrow()
                .cached_paths(0)
                .unwrap()
        });
        assert!(std::rc::Rc::ptr_eq(&before, &hit));
        window_cx.simulate_resize(gpui::size(px(1024.), px(256.)));
        draw(window_cx);
        draw(window_cx);
        let resized = window_cx.update(|_, cx| {
            view.read(cx).snapshots[0]
                .drawing_cache()
                .borrow()
                .cached_paths(0)
                .unwrap()
        });
        assert!(!std::rc::Rc::ptr_eq(&before, &resized));
        assert_ne!(before[0].0.bounds, resized[0].0.bounds);

        let mut changed = drawing_frame_arena(1, 64, true);
        changed.point(0, OP_DRAWING_VIEW_BOX_SIZE, 256., 128.);
        changed.op(0, OP_PADDING_PX, 8f32.to_bits() as u64);
        changed.op(1, OP_PATH_STROKE_WIDTH_PX, 4f32.to_bits() as u64);
        changed.op(1, OP_PATH_DASH_PX, 3f32.to_bits() as u64);
        changed.op(1, OP_PATH_FILL_RGBA, 0xAABBCCFF);
        changed.op(1, OP_PATH_FILL_RULE, 1);
        let old_cache = window_cx.update(|_, cx| view.read(cx).snapshots[0].drawing_cache());
        window_cx.update(|_, cx| {
            view.update(cx, |view, _| {
                changed.decode_into(&mut view.snapshots[0]).unwrap();
                assert_eq!(view.snapshots[0].drawing_cache_bytes(), 0);
            })
        });
        draw(window_cx);
        draw(window_cx);
        let updated = window_cx.update(|_, cx| {
            view.read(cx).snapshots[0]
                .drawing_cache()
                .borrow()
                .cached_paths(0)
                .unwrap()
        });
        assert_eq!(updated.len(), 2);
        assert_eq!((updated[0].1, updated[1].1), (0xAABBCCFF, 0x445566FF));
        assert_ne!(updated[1].0.bounds, resized[0].0.bounds);
        // Cached results must be identical to a fresh build with current inputs.
        window_cx.update(|_, cx| {
            let snapshot = &view.read(cx).snapshots[0];
            let operations = snapshot.ops(&snapshot.nodes[1]);
            let bounds = gpui::Bounds::new(point(px(8.), px(8.)), gpui::size(px(1008.), px(240.)));
            let view_box = Some(DrawingViewBox {
                x: 0.,
                y: 0.,
                width: 256.,
                height: 128.,
            });
            for (index, paint) in [
                DrawingPaint::Fill {
                    rule: FillRule::EvenOdd,
                },
                DrawingPaint::Stroke { width: 4. },
            ]
            .into_iter()
            .enumerate()
            {
                let fresh = build_drawing_path(operations, bounds, view_box, paint).unwrap();
                assert_eq!(fresh.bounds, updated[index].0.bounds);
                assert_eq!(fresh.vertices.len(), updated[index].0.vertices.len());
                for (fresh, cached) in fresh.vertices.iter().zip(&updated[index].0.vertices) {
                    assert_eq!(fresh.xy_position, cached.xy_position);
                    assert_eq!(fresh.st_position, cached.st_position);
                }
            }
        });
        let weak = std::rc::Rc::downgrade(&old_cache);
        drop(old_cache);
        assert!(
            weak.upgrade().is_none(),
            "old snapshot cache must retire after old frame release"
        );
    }

    #[test]
    fn drawing_cache_lifetime_follows_snapshot_and_surviving_frame_handles() {
        let mut snapshot = drawing_frame_snapshot(1, 64, false);
        let cache = snapshot.drawing_cache();
        let weak = std::rc::Rc::downgrade(&cache);
        let mut invalid = crate::native_workloads::WorkloadArena::default();
        invalid.node(u16::MAX, None);
        assert!(invalid.decode_into(&mut snapshot).is_err());
        assert!(std::rc::Rc::ptr_eq(&cache, &snapshot.drawing_cache()));
        let mut replacement = drawing_frame_arena(1, 64, true);
        replacement.decode_into(&mut snapshot).unwrap();
        assert!(!std::rc::Rc::ptr_eq(&cache, &snapshot.drawing_cache()));
        drop(snapshot);
        assert!(weak.upgrade().is_some());
        drop(cache);
        assert!(weak.upgrade().is_none());
    }

    #[test]
    fn drawing_path_builds_in_view_box_coordinates() {
        fn point_op(code: u16, x: f32, y: f32) -> OpRecord {
            OpRecord {
                code,
                value_kind: ValueKind::F32x2 as u16,
                a: x.to_bits() as u64 | ((y.to_bits() as u64) << 32),
                ..Default::default()
            }
        }

        let operations = [
            point_op(OP_PATH_MOVE_TO, 0.0, 100.0),
            point_op(OP_PATH_LINE_TO, 50.0, 20.0),
            point_op(OP_PATH_LINE_TO, 100.0, 60.0),
        ];
        let bounds = gpui::Bounds::new(point(px(10.0), px(20.0)), gpui::size(px(200.0), px(80.0)));
        let view_box = DrawingViewBox {
            x: 0.0,
            y: 0.0,
            width: 100.0,
            height: 100.0,
        };

        assert!(
            build_drawing_path(
                &operations,
                bounds,
                Some(view_box),
                DrawingPaint::Stroke { width: 2.0 }
            )
            .is_some()
        );
        let mapped = drawing_point((50.0, 25.0), bounds, Some(view_box));
        assert_eq!(f32::from(mapped.x), 110.0);
        assert_eq!(f32::from(mapped.y), 40.0);
    }

    #[test]
    fn drawing_circle_uses_the_smaller_view_box_scale_for_both_axes() {
        let bounds = gpui::Bounds::new(point(px(10.0), px(20.0)), gpui::size(px(200.0), px(80.0)));
        let view_box = DrawingViewBox {
            x: 0.0,
            y: 0.0,
            width: 100.0,
            height: 100.0,
        };

        assert_eq!(
            f32::from(drawing_uniform_radius(5.0, bounds, Some(view_box))),
            4.0
        );
    }
}
