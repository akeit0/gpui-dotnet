//! The GPUI.NET appearance for the foundation dock.
//!
//! The layout tree, drag geometry, and container entities live in
//! [`gpui_base::dock`]. This module is a deliberately small skin over them that
//! depends only on `gpui` and `gpui-base`, so the default native host does not
//! link the complete `gpui-component` facade. It draws tab bars with titles,
//! close/zoom controls, dock collapse affordances, and dock resize handles,
//! all styled from the projected [`gpui_base::Theme`] tokens. Panel titles are
//! recovered from [`ManagedDockPanel`](crate::dock::ManagedDockPanel) through
//! the same object-safe downcast the foundation documents.
//!
//! Tiles canvases are unreachable through the managed schema (layouts describe
//! only splits and tab groups), so their renderer draws titles and close
//! controls without move/resize gestures.

/// Names the drop-target overlay in the debug-bounds map, so tests can ask a
/// really-drawn frame whether a hovered group previewed its drop rect.
pub(crate) const DROP_PREVIEW_SELECTOR: &str = "gpui-dotnet-drop-preview";

use std::{cell::Cell, rc::Rc, sync::Arc};

use gpui::{
    AnyElement, AnyView, App, AppContext as _, Axis, Context, Div, Element, ElementId, Empty,
    Entity, InteractiveElement as _, IntoElement, MouseButton, MouseMoveEvent, MouseUpEvent,
    ParentElement as _, Pixels, Render, SharedString, Stateful, StatefulInteractiveElement as _,
    Style, Styled as _, WeakEntity, Window, div, prelude::FluentBuilder as _, px,
};
use gpui_base::dock::{
    AnyDrag, DockArea, DockAreaRenderer, DockContext, DockPlacement, DragPanel, DropIndicator,
    NodeId, PaneNode, PaneRef, PanelView, TabGroupContext, TabGroupRenderer, TileContext,
    TilesRenderer,
};

use crate::{
    dock::ManagedDockPanel,
    dock_icons::{DockIcon, icon},
};

/// Builds a [`DockArea`] wearing this skin, mirroring the construction dance
/// the styled skin documents: the skin needs the area's own weak handle for
/// dock toggle affordances, so it is created inside the constructor.
pub(crate) fn dock_area(
    id: impl Into<SharedString>,
    version: Option<usize>,
    window: &mut Window,
    cx: &mut App,
) -> Entity<DockArea> {
    cx.new(|cx| {
        let skin = GpuiDotnetDockSkin::new(cx);
        DockArea::new(id, version, window, cx).with_renderer(skin)
    })
}

struct SkinShared {
    area: WeakEntity<DockArea>,
    /// The dock whose resize handle is being dragged, if any. Only one can be.
    resizing: Cell<Option<DockPlacement>>,
}

struct GpuiDotnetDockSkin {
    shared: Rc<SkinShared>,
}

impl GpuiDotnetDockSkin {
    fn new(cx: &mut Context<DockArea>) -> Rc<Self> {
        Rc::new(Self {
            shared: Rc::new(SkinShared {
                area: cx.weak_entity(),
                resizing: Cell::new(None),
            }),
        })
    }
}

impl DockAreaRenderer for GpuiDotnetDockSkin {
    fn split_frame(&self, node: NodeId, _: Axis, _: &mut Window, cx: &mut App) -> Stateful<Div> {
        let tokens = gpui_base::Theme::global(cx).tokens;
        div()
            .id(("gpui-dotnet-dock-split", node.as_u64()))
            .bg(tokens.colors.muted)
    }

    fn render_dock(
        &self,
        dock: &DockContext,
        content: AnyElement,
        _: &mut Window,
        cx: &mut App,
    ) -> AnyElement {
        let tokens = gpui_base::Theme::global(cx).tokens;
        div()
            .flex()
            .size_full()
            .relative()
            .bg(tokens.colors.background)
            .border_color(tokens.colors.border)
            .map(|this| match dock.placement() {
                DockPlacement::Left => this.border_r_1(),
                DockPlacement::Right => this.border_l_1(),
                DockPlacement::Bottom => this.border_t_1(),
                DockPlacement::Center => this,
            })
            .child(content)
            .child(render_resize_handle(dock, &self.shared))
            .child(DockResizeTracker {
                dock: dock.clone(),
                shared: self.shared.clone(),
            })
            .into_any_element()
    }

    fn tab_group_renderer(&self) -> Rc<dyn TabGroupRenderer> {
        Rc::new(TabGroupSkin {
            shared: self.shared.clone(),
        })
    }

    fn tiles_renderer(&self) -> Rc<dyn TilesRenderer> {
        Rc::new(TilesSkin)
    }
}

/// The edge a dock resizes from. One id per placement: the docks render under
/// the same ancestor, so a shared literal would collapse the handles into one
/// element id and presses would start the wrong dock's drag. Pressing the
/// handle arms the placement on the shared skin; the tracker below routes
/// window-level moves to it.
fn render_resize_handle(dock: &DockContext, shared: &Rc<SkinShared>) -> impl IntoElement {
    let id = match dock.placement() {
        DockPlacement::Left => "gpui-dotnet-resize-handle-left",
        DockPlacement::Right => "gpui-dotnet-resize-handle-right",
        DockPlacement::Bottom => "gpui-dotnet-resize-handle-bottom",
        DockPlacement::Center => "gpui-dotnet-resize-handle-center",
    };
    let placement = dock.placement();
    let shared = shared.clone();
    div()
        .id(id)
        .absolute()
        .map(|this| match placement {
            DockPlacement::Left => this.right_0().top_0().bottom_0().w(px(5.)),
            DockPlacement::Right => this.left_0().top_0().bottom_0().w(px(5.)),
            DockPlacement::Bottom => this.top_0().left_0().right_0().h(px(5.)),
            DockPlacement::Center => this.w(px(0.)).h(px(0.)),
        })
        .map(|this| match placement {
            DockPlacement::Bottom => this.cursor_ns_resize(),
            _ => this.cursor_ew_resize(),
        })
        .on_mouse_down(MouseButton::Left, move |_, _, cx| {
            cx.stop_propagation();
            shared.resizing.set(Some(placement));
        })
}

struct DockResizeTracker {
    dock: DockContext,
    shared: Rc<SkinShared>,
}

impl IntoElement for DockResizeTracker {
    type Element = Self;

    fn into_element(self) -> Self::Element {
        self
    }
}

impl Element for DockResizeTracker {
    type RequestLayoutState = ();
    type PrepaintState = ();

    fn id(&self) -> Option<ElementId> {
        None
    }

    fn source_location(&self) -> Option<&'static std::panic::Location<'static>> {
        None
    }

    fn request_layout(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        window: &mut Window,
        cx: &mut App,
    ) -> (gpui::LayoutId, Self::RequestLayoutState) {
        (window.request_layout(Style::default(), None, cx), ())
    }

    fn prepaint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: gpui::Bounds<Pixels>,
        _: &mut Self::RequestLayoutState,
        _: &mut Window,
        _: &mut App,
    ) -> Self::PrepaintState {
    }

    fn paint(
        &mut self,
        _: Option<&gpui::GlobalElementId>,
        _: Option<&gpui::InspectorElementId>,
        _: gpui::Bounds<Pixels>,
        _: &mut Self::RequestLayoutState,
        _: &mut Self::PrepaintState,
        window: &mut Window,
        _: &mut App,
    ) {
        // A resize is driven by pointer moves that land anywhere in the
        // window, not only on the handle, so it cannot be a listener on the
        // handle itself. This element paints nothing and exists for its paint
        // hook, which is where a window-level mouse listener is registered.
        let placement = self.dock.placement();
        let dock = self.dock.clone();
        let shared = self.shared.clone();
        window.on_mouse_event(move |event: &MouseMoveEvent, phase, window, cx| {
            if !phase.bubble() || shared.resizing.get() != Some(placement) {
                return;
            }
            // Dragging a closed dock's handle reopens it. The live state is
            // read rather than the render-time snapshot, which would still
            // say closed for the rest of the frame and toggle it shut again
            // on the next move.
            let open = shared
                .area
                .upgrade()
                .is_some_and(|area| area.read(cx).is_dock_open(placement));
            if !open {
                _ = shared
                    .area
                    .update(cx, |area, cx| area.toggle_dock(placement, window, cx));
            }
            dock.resize_to(event.position, window, cx);
        });

        let placement = self.dock.placement();
        let shared = self.shared.clone();
        window.on_mouse_event(move |_: &MouseUpEvent, phase, _, cx| {
            if !phase.bubble() || shared.resizing.get() != Some(placement) {
                return;
            }
            shared.resizing.set(None);
            // The size lives on the dock, not in the layout tree, so nothing
            // else tells a subscriber to persist it.
            _ = shared.area.update(cx, |_, cx| {
                cx.emit(gpui_base::dock::DockEvent::LayoutChanged);
            });
        });
    }
}

struct TabGroupSkin {
    shared: Rc<SkinShared>,
}

impl TabGroupSkin {
    /// Whether a dock's collapse affordance belongs in this group's tab bar.
    /// Mirrors the styled skin: the left-most top-most center group owns the
    /// left toggle, the right-most top-most center group owns the right
    /// toggle, and the left-most top-most bottom group owns the bottom
    /// toggle.
    fn dock_toggle(
        &self,
        placement: DockPlacement,
        group: &TabGroupContext,
        cx: &mut App,
    ) -> Option<AnyElement> {
        if group.is_zoomed() {
            return None;
        }
        let area = self.shared.area.upgrade()?;
        let (designated, is_open) = {
            let area_ref = area.read(cx);
            if !area_ref.is_dock_collapsible(placement) {
                return None;
            }
            let designated = match placement {
                DockPlacement::Left => area_ref
                    .layout(DockPlacement::Center)
                    .and_then(|tree| left_top_group(tree.root())),
                DockPlacement::Right => area_ref
                    .layout(DockPlacement::Center)
                    .and_then(|tree| right_top_group(tree.root())),
                DockPlacement::Bottom => area_ref
                    .layout(DockPlacement::Bottom)
                    .and_then(|tree| left_top_group(tree.root())),
                DockPlacement::Center => None,
            };
            (designated, area_ref.is_dock_open(placement))
        };
        if designated != Some(group.node()) {
            return None;
        }
        // Collapse chevrons drawn as vector paths: the minimal skin ships no
        // icon font and no bundled assets.
        let glyph = match (placement, is_open) {
            (DockPlacement::Left, true) => DockIcon::ChevronLeft,
            (DockPlacement::Left, false) => DockIcon::ChevronRight,
            (DockPlacement::Right, true) => DockIcon::ChevronRight,
            (DockPlacement::Right, false) => DockIcon::ChevronLeft,
            (DockPlacement::Bottom, true) => DockIcon::ChevronDown,
            (DockPlacement::Bottom, false) => DockIcon::ChevronUp,
            (DockPlacement::Center, _) => return None,
        };
        let area = self.shared.area.clone();
        let placement_id: usize = match placement {
            DockPlacement::Left => 0,
            DockPlacement::Right => 1,
            DockPlacement::Bottom => 2,
            DockPlacement::Center => 3,
        };
        let tokens = gpui_base::Theme::global(cx).tokens;
        Some(
            div()
                .id(("gpui-dotnet-dock-toggle", placement_id))
                .flex_shrink_0()
                .w(px(22.))
                .h(px(22.))
                .flex()
                .items_center()
                .justify_center()
                .rounded(px(4.))
                .cursor_pointer()
                .on_click(move |_, window, cx| {
                    cx.stop_propagation();
                    _ = area.update(cx, |area, cx| area.toggle_dock(placement, window, cx));
                })
                .child(icon(glyph, tokens.colors.muted_foreground, px(14.)))
                .into_any_element(),
        )
    }

    fn render_tab(
        &self,
        group: &TabGroupContext,
        ix: usize,
        _: &mut Window,
        cx: &mut App,
    ) -> AnyElement {
        let tokens = gpui_base::Theme::global(cx).tokens;
        let panel = &group.panels()[ix];
        let displayed = group
            .active_panel()
            .is_some_and(|active| active.panel_id(cx) == panel.panel_id(cx));
        let drag = group
            .is_draggable()
            .then(|| group.drag_panel(ix, cx))
            .flatten();
        let droppable = group.is_droppable();
        let closable = group.is_closable() && panel.closable(cx);

        let mut tab = div()
            .id(("gpui-dotnet-dock-tab", ix))
            .flex()
            .flex_row()
            .items_center()
            .h_full()
            .min_w_0()
            .flex_shrink_1()
            .px(px(8.))
            .gap(px(6.))
            .cursor_pointer()
            .overflow_hidden()
            .whitespace_nowrap()
            .text_ellipsis()
            .text_sm()
            .when(displayed, |this| {
                this.bg(tokens.colors.surface)
                    .text_color(tokens.colors.foreground)
            })
            .when(!displayed, |this| {
                this.text_color(tokens.colors.muted_foreground)
            })
            .child(
                div()
                    .flex_1()
                    .min_w_0()
                    .overflow_hidden()
                    .whitespace_nowrap()
                    .text_ellipsis()
                    .child(panel_title(panel, cx)),
            )
            .on_click({
                let group = group.clone();
                let shared = self.shared.clone();
                move |_, window, cx| {
                    group.select_tab(ix, window, cx);
                    // Clicking the strip of a collapsed dock reopens it. The
                    // collapsed flag implies the group lives in a closed dock;
                    // find which one holds this node.
                    if group.is_collapsed() {
                        reopen_dock_holding(&shared, group.node(), window, cx);
                    }
                }
            });
        if let Some(drag) = drag {
            let panel = panel.clone();
            tab = tab.on_drag(drag, move |drag, offset, _, cx| {
                cx.stop_propagation();
                drag.set_drag_offset(offset);
                drag.set_preview_size(gpui::size(px(96.), px(30.)));
                cx.new(|_| DragPreview {
                    panel: panel.clone(),
                })
            });
        }
        if droppable {
            let group_for_panel = group.clone();
            let target_ix = ix;
            tab = tab
                .on_drop(move |drag: &DragPanel, window, cx| {
                    group_for_panel.drop_panel(drag.clone(), Some(target_ix), true, window, cx);
                })
                .on_drop({
                    let group = group.clone();
                    move |item: &AnyDrag, window, cx| {
                        group.drop_item(item.clone(), None, window, cx);
                    }
                });
        }
        if closable {
            let panel_id = panel.panel_id(cx);
            let group = group.clone();
            tab = tab.child(
                div()
                    .id(("gpui-dotnet-dock-close", ix))
                    .flex_shrink_0()
                    .w(px(18.))
                    .h(px(18.))
                    .flex()
                    .items_center()
                    .justify_center()
                    .rounded_full()
                    .cursor_pointer()
                    .on_click(move |_, window, cx| {
                        cx.stop_propagation();
                        group.close(panel_id, window, cx);
                    })
                    .child(icon(
                        DockIcon::Close,
                        tokens.colors.muted_foreground,
                        px(12.),
                    )),
            );
        }
        tab.into_any_element()
    }
}

#[cfg(test)]
fn placement_label(placement: DockPlacement) -> &'static str {
    match placement {
        DockPlacement::Left => "left",
        DockPlacement::Right => "right",
        DockPlacement::Bottom => "bottom",
        DockPlacement::Center => "center",
    }
}

fn left_top_group(node: &PaneNode) -> Option<NodeId> {
    match node.kind() {
        PaneRef::Tabs { .. } => Some(node.id()),
        PaneRef::Split { children, .. } => children.first().and_then(left_top_group),
        PaneRef::Tiles { .. } => None,
    }
}

fn right_top_group(node: &PaneNode) -> Option<NodeId> {
    match node.kind() {
        PaneRef::Tabs { .. } => Some(node.id()),
        PaneRef::Split { axis, children, .. } => match axis {
            Axis::Vertical => children.first(),
            Axis::Horizontal => children.last(),
        }
        .and_then(right_top_group),
        PaneRef::Tiles { .. } => None,
    }
}

fn tree_contains(node: &PaneNode, target: NodeId) -> bool {
    if node.id() == target {
        return true;
    }
    match node.kind() {
        PaneRef::Split { children, .. } => {
            children.iter().any(|child| tree_contains(child, target))
        }
        PaneRef::Tabs { .. } | PaneRef::Tiles { .. } => false,
    }
}

fn reopen_dock_holding(shared: &Rc<SkinShared>, node: NodeId, window: &mut Window, cx: &mut App) {
    let Some(area) = shared.area.upgrade() else {
        return;
    };
    for placement in [
        DockPlacement::Left,
        DockPlacement::Right,
        DockPlacement::Bottom,
    ] {
        let holds = area
            .read(cx)
            .layout(placement)
            .is_some_and(|tree| tree.root().id() == node || tree_contains(tree.root(), node));
        if holds {
            _ = area.update(cx, |area, cx| {
                if !area.is_dock_open(placement) {
                    area.toggle_dock(placement, window, cx);
                }
            });
            return;
        }
    }
}

impl TabGroupRenderer for TabGroupSkin {
    fn frame(&self, _: &TabGroupContext, _: &mut Window, cx: &mut App) -> Stateful<Div> {
        let tokens = gpui_base::Theme::global(cx).tokens;
        div()
            .id("gpui-dotnet-tab-group")
            .bg(tokens.colors.background)
            .border_1()
            .border_color(tokens.colors.border)
    }

    fn render_tab_bar(
        &self,
        group: &TabGroupContext,
        window: &mut Window,
        cx: &mut App,
    ) -> AnyElement {
        let tokens = gpui_base::Theme::global(cx).tokens;
        let visible: Vec<usize> = group
            .panels()
            .iter()
            .enumerate()
            .filter(|(_, panel)| panel.visible(cx))
            .map(|(ix, _)| ix)
            .collect();
        if visible.is_empty() {
            return Empty.into_any_element();
        }

        let left = self.dock_toggle(DockPlacement::Left, group, cx);
        let bottom = self.dock_toggle(DockPlacement::Bottom, group, cx);
        let right = self.dock_toggle(DockPlacement::Right, group, cx);
        let tabs_count = group.panels().len();
        let droppable = group.is_droppable();

        let mut bar = div()
            .flex()
            .flex_row()
            .items_center()
            .h(px(30.))
            .w_full()
            .flex_shrink_0()
            .overflow_hidden()
            .bg(tokens.colors.muted)
            .border_b_1()
            .border_color(tokens.colors.border);
        if left.is_some() || bottom.is_some() {
            bar = bar.child(
                div()
                    .flex()
                    .flex_row()
                    .items_center()
                    .flex_shrink_0()
                    .children(left)
                    .children(bottom),
            );
        }
        for ix in visible {
            bar = bar.child(self.render_tab(group, ix, window, cx));
        }
        // Empty space so a panel can be dropped past the last tab.
        let mut space = div()
            .id("gpui-dotnet-dock-tab-space")
            .h_full()
            .flex_1()
            .min_w(px(16.));
        if droppable {
            let node = group.node();
            let group_for_panel = group.clone();
            let group_for_item = group.clone();
            space = space
                .on_drop(move |drag: &DragPanel, window, cx| {
                    let ix = (drag.source() == node).then(|| tabs_count.saturating_sub(1));
                    group_for_panel.drop_panel(drag.clone(), ix, false, window, cx);
                })
                .on_drop(move |item: &AnyDrag, window, cx| {
                    group_for_item.drop_item(item.clone(), None, window, cx);
                });
        }
        bar = bar.child(space);

        // Trailing controls: zoom affordance plus the right dock toggle.
        let zoomable = group.active_panel().is_some_and(|panel| panel.zoomable(cx));
        if group.is_zoomed() || zoomable {
            let zoomed = group.is_zoomed();
            let group = group.clone();
            bar = bar.child(
                div()
                    .id("gpui-dotnet-dock-zoom")
                    .flex_shrink_0()
                    .w(px(22.))
                    .h(px(22.))
                    .flex()
                    .items_center()
                    .justify_center()
                    .rounded(px(4.))
                    .cursor_pointer()
                    .on_click(move |_, window, cx| {
                        cx.stop_propagation();
                        group.toggle_zoom(window, cx);
                    })
                    .child(icon(
                        if zoomed {
                            DockIcon::ZoomOut
                        } else {
                            DockIcon::ZoomIn
                        },
                        tokens.colors.muted_foreground,
                        px(14.),
                    )),
            );
        }
        if let Some(right) = right {
            bar = bar.child(right);
        }
        bar.into_any_element()
    }

    fn render_active_panel(
        &self,
        panel: AnyView,
        group: &TabGroupContext,
        _: &mut Window,
        _: &mut App,
    ) -> AnyElement {
        if group.is_collapsed() {
            return Empty.into_any_element();
        }
        panel.into_any_element()
    }

    /// The drop-target preview: the rect the drop would land in, drawn over
    /// the content region. Base reports it relative to the group's content
    /// bounds, which is exactly the frame this child is laid out in, so the
    /// absolute offsets line up with no conversion.
    fn render_drop_indicator(
        &self,
        indicator: DropIndicator,
        _: &mut Window,
        cx: &mut App,
    ) -> Option<AnyElement> {
        let tokens = gpui_base::Theme::global(cx).tokens;
        let to = indicator.to();
        Some(
            div()
                .absolute()
                .left(to.origin().x)
                .top(to.origin().y)
                .w(to.size().width)
                .h(to.size().height)
                .bg(tokens.colors.primary.alpha(0.16))
                .border_2()
                .border_color(tokens.colors.ring)
                .rounded(px(6.))
                .debug_selector(|| DROP_PREVIEW_SELECTOR.to_string())
                .into_any_element(),
        )
    }
}

struct TilesSkin;

impl TilesRenderer for TilesSkin {
    fn render_drag_bar(&self, tile: &TileContext, _: &mut Window, cx: &mut App) -> AnyElement {
        let tokens = gpui_base::Theme::global(cx).tokens;
        let mut bar = div()
            .flex()
            .flex_row()
            .items_center()
            .justify_between()
            .h(px(28.))
            .w_full()
            .px(px(8.))
            .bg(tokens.colors.muted)
            .text_color(tokens.colors.foreground)
            .text_sm()
            .child(panel_title(tile.panel(), cx));
        if tile.is_closable() {
            let tile = tile.clone();
            bar = bar.child(
                div()
                    .id("gpui-dotnet-tile-close")
                    .flex_shrink_0()
                    .w(px(18.))
                    .h(px(18.))
                    .flex()
                    .items_center()
                    .justify_center()
                    .cursor_pointer()
                    .on_click(move |_, window, cx| {
                        cx.stop_propagation();
                        tile.close(window, cx);
                    })
                    .child(icon(
                        DockIcon::Close,
                        tokens.colors.muted_foreground,
                        px(12.),
                    )),
            );
        }
        bar.into_any_element()
    }
}

/// A panel's title, or its registered name when it reached the skin without a
/// managed wrapper. Panels installed through [`DockLayout::panel`] always
/// carry the managed entity, so the fallback only fires for foreign handles.
fn panel_title(panel: &std::sync::Arc<dyn PanelView>, cx: &mut App) -> AnyElement {
    if let Some(entity) = panel.as_any().downcast_ref::<Entity<ManagedDockPanel>>() {
        return entity.read(cx).tab_title().into_any_element();
    }
    SharedString::from(panel.panel_name(cx)).into_any_element()
}

/// The preview that follows the cursor while a panel is dragged.
///
/// Holds the panel handle, not a built element: every `AnyElement` is boxed
/// into the frame's element arena, which GPUI clears each frame, so retaining
/// one across frames panics on the next dereference. The title is rebuilt on
/// every render instead.
struct DragPreview {
    panel: Arc<dyn PanelView>,
}

impl Render for DragPreview {
    fn render(&mut self, _: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let tokens = gpui_base::Theme::global(cx).tokens;
        div()
            .id("gpui-dotnet-drag-panel")
            .flex()
            .items_center()
            .h(px(30.))
            .w(px(96.))
            .px(px(8.))
            .overflow_hidden()
            .whitespace_nowrap()
            .border_1()
            .border_color(tokens.colors.border)
            .rounded(px(6.))
            .bg(tokens.colors.surface)
            .text_color(tokens.colors.foreground)
            .text_sm()
            .opacity(0.85)
            .child(panel_title(&self.panel, cx))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use gpui::{EventEmitter, FocusHandle, Focusable, Modifiers, MouseButton};
    use gpui_base::dock::{DockLayout, Panel, PanelEvent};

    struct Probe {
        focus: FocusHandle,
    }

    impl gpui_base::dock::Panel for Probe {
        fn panel_name(&self) -> &'static str {
            "Probe"
        }

        fn closable(&self, _: &App) -> bool {
            false
        }
    }

    impl EventEmitter<PanelEvent> for Probe {}
    impl Focusable for Probe {
        fn focus_handle(&self, _: &App) -> FocusHandle {
            self.focus.clone()
        }
    }
    impl Render for Probe {
        fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            Empty
        }
    }

    fn probe(cx: &mut App) -> Entity<Probe> {
        cx.new(|cx| Probe {
            focus: cx.focus_handle(),
        })
    }

    #[gpui::test]
    fn dock_area_builds_with_the_local_skin(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        let (area, cx) = cx.add_window_view(|window, cx| {
            DockArea::new("gpui-dotnet-skin-probe", None, window, cx)
                .with_renderer(GpuiDotnetDockSkin::new(cx))
        });
        cx.run_until_parked();
        cx.read(|cx| {
            assert_eq!(
                area.read(cx).id(),
                SharedString::from("gpui-dotnet-skin-probe")
            );
            assert!(area.read(cx).is_empty(DockPlacement::Center, cx));
        });
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });
    }

    #[gpui::test]
    fn side_dock_toggles_target_distinct_center_groups(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        let (area, cx) = cx.add_window_view(|window, cx| {
            DockArea::new("gpui-dotnet-toggle-probe", None, window, cx)
                .with_renderer(GpuiDotnetDockSkin::new(cx))
        });
        cx.update(|window, cx| {
            let left = probe(cx);
            let first = probe(cx);
            let second = probe(cx);
            area.update(cx, |area, cx| {
                area.set_center(
                    DockLayout::h_split()
                        .child(DockLayout::tabs().panel(first), None)
                        .child(DockLayout::tabs().panel(second), None),
                    window,
                    cx,
                );
                area.set_dock(
                    DockPlacement::Left,
                    DockLayout::tabs().panel(left),
                    window,
                    cx,
                );
            });
        });
        cx.run_until_parked();

        let (left_owner, right_owner, center_root) = cx.read(|cx| {
            let area = area.read(cx);
            let center = area.layout(DockPlacement::Center).expect("center exists");
            (
                left_top_group(center.root()),
                right_top_group(center.root()),
                center.root().id(),
            )
        });
        assert_ne!(
            left_owner, right_owner,
            "left and right toggles live in different groups"
        );
        for owner in [left_owner, right_owner] {
            let node = owner.expect("a two-group center names toggle owners");
            assert!(
                cx.read(|cx| {
                    area.read(cx)
                        .layout(DockPlacement::Center)
                        .is_some_and(|tree| tree_contains(tree.root(), node))
                }),
                "toggle owner {node:?} is inside the center tree rooted at {center_root:?}"
            );
        }
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });
    }

    #[gpui::test]
    fn bare_panel_handles_recover_their_concrete_entity(cx: &mut gpui::TestAppContext) {
        // Locks the downcast idiom `panel_title` relies on: layouts built
        // with `DockLayout::panel` store the bare entity, whose object-safe
        // handle downcasts back to `Entity<P>`.
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        let panel = cx.update(probe);
        let handle: Arc<dyn PanelView> = Arc::new(panel.clone());
        cx.read(|cx| {
            let recovered = handle
                .as_any()
                .downcast_ref::<Entity<Probe>>()
                .expect("a bare entity handle recovers");
            assert_eq!(recovered.entity_id(), panel.entity_id());
            assert!(!recovered.read(cx).closable(cx));
        });
    }

    #[gpui::test]
    fn dragging_a_tab_renders_a_fresh_preview_every_frame(cx: &mut gpui::TestAppContext) {
        // The drag preview follows the cursor, so it re-renders on frames
        // after the drag started. Retaining its title element across the
        // frame arena clear panics in `ArenaBox::validate`; the preview must
        // rebuild the title on every render.
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        let (area, cx) = cx.add_window_view(|window, cx| {
            DockArea::new("gpui-dotnet-drag-probe", None, window, cx)
                .with_renderer(GpuiDotnetDockSkin::new(cx))
        });
        cx.simulate_resize(gpui::size(gpui::px(800.), gpui::px(600.)));
        cx.update(|window, cx| {
            let alpha = probe(cx);
            let beta = probe(cx);
            area.update(cx, |area, cx| {
                area.set_center(
                    DockLayout::h_split()
                        .child(DockLayout::tabs().panel(alpha), None)
                        .child(DockLayout::tabs().panel(beta), None),
                    window,
                    cx,
                );
            });
        });
        cx.run_until_parked();
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });

        let press = |cx: &mut gpui::VisualTestContext| {
            cx.simulate_mouse_down(
                gpui::point(gpui::px(40.), gpui::px(15.)),
                MouseButton::Left,
                Modifiers::none(),
            );
        };
        let drag_to = |cx: &mut gpui::VisualTestContext, x: f32| {
            cx.simulate_mouse_move(
                gpui::point(gpui::px(x), gpui::px(15.)),
                MouseButton::Left,
                Modifiers::none(),
            );
            cx.run_until_parked();
            cx.update(|window, cx| {
                window.draw(cx).clear(cx);
            });
        };
        press(cx);
        // Past the drag threshold: the preview is born here, then repainted
        // on each of the following frames while the pointer keeps moving.
        drag_to(cx, 52.);
        drag_to(cx, 200.);

        // Into the second group's content: hovering the tab bar alone offers
        // per-tab drops, while the content region reports the positioned
        // indicator the overlay draws.
        cx.simulate_mouse_move(
            gpui::point(gpui::px(600.), gpui::px(300.)),
            MouseButton::Left,
            Modifiers::none(),
        );
        cx.run_until_parked();
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });

        // Hovering the second group previews the rect the drop would land in.
        assert!(
            cx.debug_bounds(DROP_PREVIEW_SELECTOR).is_some(),
            "the hovered group previews its drop rect while a drag hovers it"
        );

        cx.simulate_mouse_up(
            gpui::point(gpui::px(600.), gpui::px(300.)),
            MouseButton::Left,
            Modifiers::none(),
        );
        cx.run_until_parked();
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });
        assert!(
            cx.debug_bounds(DROP_PREVIEW_SELECTOR).is_none(),
            "the drop preview leaves with the drag"
        );

        // The drop landed in the second group, emptying the first: one split
        // root holding both panels.
        let dumped = cx.read(|cx| area.read(cx).dump(cx));
        assert_eq!(dumped.center.children.len(), 1);
        assert_eq!(dumped.center.children[0].children.len(), 2);
    }

    #[test]
    fn dock_placement_labels_are_stable_element_ids() {
        assert_eq!(placement_label(DockPlacement::Left), "left");
        assert_eq!(placement_label(DockPlacement::Right), "right");
        assert_eq!(placement_label(DockPlacement::Bottom), "bottom");
        assert_eq!(placement_label(DockPlacement::Center), "center");
    }
}
