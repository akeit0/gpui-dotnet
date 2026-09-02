use std::collections::{HashMap, HashSet};

use gpui::{
    App, AppContext as _, Axis, Context, Entity, EventEmitter, FocusHandle, Focusable, IntoElement,
    ParentElement, Render, SharedString, Styled, WeakEntity, Window, div, px,
};
use gpui_base::dock::{DockArea, DockLayout, DockPlacement, Panel as BasePanel, PanelEvent};
use gpui_component::dock::{DockSkin, Panel as ComponentPanel, PanelControl, panel_handle};

use crate::{
    app_host::ManagedView,
    resources::{ResourceKey, resource_key},
    semantic::{
        COMPONENT_DOCK_PANEL, COMPONENT_DOCK_REGION, COMPONENT_DOCK_SPLIT, COMPONENT_DOCK_TABS,
        OP_DOCK_ACTIVE_INDEX, OP_DOCK_AXIS, OP_DOCK_INITIAL_SIZE_PX, OP_DOCK_LOCKED,
        OP_DOCK_PANEL_CLOSABLE, OP_DOCK_PANEL_INNER_PADDING, OP_DOCK_PANEL_ZOOMABLE,
        OP_DOCK_REGION_COLLAPSIBLE, OP_DOCK_REGION_OPEN, OP_DOCK_REGION_SIDE,
    },
    snapshot::{SnapshotNode, ValidatedSnapshot},
};

#[derive(Clone)]
pub(crate) struct DockConfiguration {
    pub(crate) key: ResourceKey,
    pub(crate) locked: bool,
    pub(crate) center: DockLayoutSpec,
    pub(crate) regions: Vec<DockRegionSpec>,
}

#[derive(Clone)]
pub(crate) enum DockLayoutSpec {
    Split {
        axis: Axis,
        children: Vec<DockSplitChildSpec>,
    },
    Tabs {
        active_index: usize,
        panels: Vec<DockPanelSpec>,
    },
}

#[derive(Clone)]
pub(crate) struct DockSplitChildSpec {
    layout: DockLayoutSpec,
    initial_size: Option<f32>,
}

#[derive(Clone)]
pub(crate) struct DockPanelSpec {
    id: SharedString,
    title: SharedString,
    content_node: u32,
    closable: bool,
    zoomable: bool,
    inner_padding: bool,
}

#[derive(Clone)]
pub(crate) struct DockRegionSpec {
    placement: DockPlacement,
    layout: DockLayoutSpec,
    initial_size: Option<f32>,
    initially_open: bool,
    collapsible: bool,
}

impl DockConfiguration {
    fn region(&self, placement: DockPlacement) -> Option<&DockRegionSpec> {
        self.regions
            .iter()
            .find(|region| region.placement == placement)
    }

    fn collect_panels<'a>(&'a self, panels: &mut Vec<&'a DockPanelSpec>) {
        self.center.collect_panels(panels);
        for region in &self.regions {
            region.layout.collect_panels(panels);
        }
    }
}

impl DockLayoutSpec {
    fn same_structure(&self, other: &Self) -> bool {
        match (self, other) {
            (
                Self::Split {
                    axis: left_axis,
                    children: left_children,
                },
                Self::Split {
                    axis: right_axis,
                    children: right_children,
                },
            ) => {
                left_axis == right_axis
                    && left_children.len() == right_children.len()
                    && left_children
                        .iter()
                        .zip(right_children)
                        .all(|(left, right)| {
                            left.initial_size == right.initial_size
                                && left.layout.same_structure(&right.layout)
                        })
            }
            (
                Self::Tabs {
                    active_index: left_active,
                    panels: left_panels,
                },
                Self::Tabs {
                    active_index: right_active,
                    panels: right_panels,
                },
            ) => {
                left_active == right_active
                    && left_panels.len() == right_panels.len()
                    && left_panels
                        .iter()
                        .zip(right_panels)
                        .all(|(left, right)| left.id == right.id)
            }
            _ => false,
        }
    }

    fn collect_panels<'a>(&'a self, panels: &mut Vec<&'a DockPanelSpec>) {
        match self {
            Self::Split { children, .. } => {
                for child in children {
                    child.layout.collect_panels(panels);
                }
            }
            Self::Tabs { panels: group, .. } => panels.extend(group),
        }
    }
}

pub(crate) struct ManagedDockResource {
    area: Entity<DockArea>,
    panels: HashMap<SharedString, Entity<ManagedDockPanel>>,
    declaration: Option<DockConfiguration>,
    locked: bool,
}

enum RegionUpdate {
    Remove(DockPlacement),
    Configure {
        placement: DockPlacement,
        layout: Option<DockLayout>,
        initial_size: Option<f32>,
        initially_open: bool,
        collapsible: bool,
        newly_added: bool,
        collapsible_changed: bool,
    },
}

impl ManagedDockResource {
    pub(crate) fn new(
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Self {
        let (area, _) = DockSkin::dock_area(configuration.key.key.clone(), None, window, cx);
        let mut resource = Self {
            area,
            panels: HashMap::new(),
            declaration: None,
            locked: configuration.locked,
        };
        resource.configure(configuration, owner, window, cx);
        resource
    }

    pub(crate) fn area(&self) -> Entity<DockArea> {
        self.area.clone()
    }

    pub(crate) fn configure(
        &mut self,
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) {
        let mut declared = Vec::new();
        configuration.collect_panels(&mut declared);

        let mut active_ids = HashSet::with_capacity(declared.len());
        for panel in declared {
            active_ids.insert(panel.id.clone());
            if let Some(existing) = self.panels.get(&panel.id).cloned() {
                existing.update(cx, |existing, cx| {
                    existing.configure(panel, owner.clone(), cx)
                });
            } else {
                let created = cx.new(|cx| ManagedDockPanel::new(panel, owner.clone(), cx));
                self.panels.insert(panel.id.clone(), created);
            }
        }

        let center_changed = self
            .declaration
            .as_ref()
            .is_none_or(|previous| !previous.center.same_structure(&configuration.center));
        let locked_changed = self.locked != configuration.locked;
        let mut region_updates = Vec::new();
        let mut region_structure_changed = false;
        for placement in [
            DockPlacement::Left,
            DockPlacement::Bottom,
            DockPlacement::Right,
        ] {
            let current = configuration.region(placement);
            let previous = self
                .declaration
                .as_ref()
                .and_then(|declaration| declaration.region(placement));
            match (current, previous) {
                (None, None) => {}
                (None, Some(_)) => {
                    region_structure_changed = true;
                    region_updates.push(RegionUpdate::Remove(placement));
                }
                (Some(current), previous) => {
                    let newly_added = previous.is_none();
                    let layout_changed = previous
                        .is_none_or(|previous| !previous.layout.same_structure(&current.layout));
                    let collapsible_changed =
                        previous.is_none_or(|previous| previous.collapsible != current.collapsible);
                    if layout_changed || collapsible_changed {
                        region_updates.push(RegionUpdate::Configure {
                            placement,
                            layout: layout_changed
                                .then(|| build_layout(&current.layout, &self.panels, cx)),
                            initial_size: current.initial_size,
                            initially_open: current.initially_open,
                            collapsible: current.collapsible,
                            newly_added,
                            collapsible_changed,
                        });
                    }
                    region_structure_changed |= layout_changed;
                }
            }
        }
        let structure_changed = center_changed || region_structure_changed;

        if structure_changed || locked_changed || !region_updates.is_empty() {
            let center =
                center_changed.then(|| build_layout(&configuration.center, &self.panels, cx));
            self.area.update(cx, |area, cx| {
                if locked_changed || self.declaration.is_none() {
                    area.set_locked(configuration.locked, window, cx);
                }
                if let Some(center) = center {
                    area.set_center(center, window, cx);
                }
                for update in region_updates {
                    match update {
                        RegionUpdate::Remove(placement) => area.remove_dock(placement, window, cx),
                        RegionUpdate::Configure {
                            placement,
                            layout,
                            initial_size,
                            initially_open,
                            collapsible,
                            newly_added,
                            collapsible_changed,
                        } => {
                            if let Some(layout) = layout {
                                area.set_dock(placement, layout, window, cx);
                            }
                            if newly_added {
                                if let Some(initial_size) = initial_size {
                                    area.set_dock_size(placement, px(initial_size), window, cx);
                                }
                                if !initially_open {
                                    area.toggle_dock(placement, window, cx);
                                }
                            }
                            if newly_added || collapsible_changed {
                                area.set_dock_collapsible(placement, collapsible, window, cx);
                            }
                        }
                    }
                }
            });
        }

        self.panels.retain(|id, _| active_ids.contains(id));
        self.declaration = Some(configuration.clone());
        self.locked = configuration.locked;
    }
}

fn build_layout(
    specification: &DockLayoutSpec,
    panels: &HashMap<SharedString, Entity<ManagedDockPanel>>,
    cx: &App,
) -> DockLayout {
    match specification {
        DockLayoutSpec::Split { axis, children } => {
            let mut layout = match axis {
                Axis::Horizontal => DockLayout::h_split(),
                Axis::Vertical => DockLayout::v_split(),
            };
            for child in children {
                layout = layout.child(
                    build_layout(&child.layout, panels, cx),
                    child.initial_size.map(px),
                );
            }
            layout
        }
        DockLayoutSpec::Tabs {
            active_index,
            panels: group,
        } => {
            let mut layout = DockLayout::tabs().active_index(*active_index);
            for panel in group {
                let entity = panels
                    .get(&panel.id)
                    .expect("validated Dock declarations create every panel")
                    .clone();
                layout = layout.panel_view(panel_handle(entity), cx);
            }
            layout
        }
    }
}

struct ManagedDockPanel {
    owner: WeakEntity<ManagedView>,
    title: SharedString,
    content_node: u32,
    closable: bool,
    zoomable: bool,
    inner_padding: bool,
    focus_handle: FocusHandle,
}

impl ManagedDockPanel {
    fn new(
        specification: &DockPanelSpec,
        owner: WeakEntity<ManagedView>,
        cx: &mut Context<Self>,
    ) -> Self {
        Self {
            owner,
            title: specification.title.clone(),
            content_node: specification.content_node,
            closable: specification.closable,
            zoomable: specification.zoomable,
            inner_padding: specification.inner_padding,
            focus_handle: cx.focus_handle(),
        }
    }

    fn configure(
        &mut self,
        specification: &DockPanelSpec,
        owner: WeakEntity<ManagedView>,
        cx: &mut Context<Self>,
    ) {
        self.owner = owner;
        self.title = specification.title.clone();
        self.content_node = specification.content_node;
        self.closable = specification.closable;
        self.zoomable = specification.zoomable;
        self.inner_padding = specification.inner_padding;
        cx.notify();
    }
}

impl BasePanel for ManagedDockPanel {
    fn panel_name(&self) -> &'static str {
        "GpuiDotnetPanel"
    }

    fn closable(&self, _: &App) -> bool {
        self.closable
    }

    fn zoomable(&self, _: &App) -> bool {
        self.zoomable
    }
}

impl ComponentPanel for ManagedDockPanel {
    fn tab_name(&self, _: &App) -> Option<SharedString> {
        Some(self.title.clone())
    }

    fn title(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
        self.title.clone()
    }

    fn zoom_control(&self, _: &App) -> Option<PanelControl> {
        self.zoomable.then_some(PanelControl::Menu)
    }

    fn inner_padding(&self, _: &App) -> bool {
        self.inner_padding
    }
}

impl EventEmitter<PanelEvent> for ManagedDockPanel {}

impl Focusable for ManagedDockPanel {
    fn focus_handle(&self, _: &App) -> FocusHandle {
        self.focus_handle.clone()
    }
}

impl Render for ManagedDockPanel {
    fn render(&mut self, window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let content_node = self.content_node;
        match self.owner.update(cx, |view, cx| {
            if content_node as usize >= view.snapshot.nodes.len() {
                return div().into_any_element();
            }
            div()
                .size_full()
                .flex()
                .flex_col()
                .child(view.materialize_node(content_node, &view.snapshot, window, cx))
                .into_any_element()
        }) {
            Ok(content) => content,
            Err(_) => div().into_any_element(),
        }
    }
}

pub(crate) fn dock_configuration(
    snapshot: &ValidatedSnapshot,
    node: &SnapshotNode,
) -> Option<DockConfiguration> {
    let key = resource_key(snapshot, node)?;
    let mut center = None;
    let mut regions = Vec::new();
    for child_id in snapshot.children(node) {
        let child = snapshot.nodes.get(*child_id as usize)?;
        if child.component == COMPONENT_DOCK_REGION {
            regions.push(parse_region(snapshot, child)?);
        } else {
            center = Some(parse_layout(snapshot, *child_id)?);
        }
    }
    Some(DockConfiguration {
        key,
        locked: last_u32(snapshot, node, OP_DOCK_LOCKED).unwrap_or(0) != 0,
        center: center?,
        regions,
    })
}

fn parse_region(snapshot: &ValidatedSnapshot, node: &SnapshotNode) -> Option<DockRegionSpec> {
    let placement = match last_u32(snapshot, node, OP_DOCK_REGION_SIDE).unwrap_or(0) {
        0 => DockPlacement::Left,
        1 => DockPlacement::Bottom,
        2 => DockPlacement::Right,
        _ => return None,
    };
    let layout = *snapshot.children(node).first()?;
    Some(DockRegionSpec {
        placement,
        layout: parse_layout(snapshot, layout)?,
        initial_size: last_f32(snapshot, node, OP_DOCK_INITIAL_SIZE_PX),
        initially_open: last_u32(snapshot, node, OP_DOCK_REGION_OPEN).unwrap_or(1) != 0,
        collapsible: last_u32(snapshot, node, OP_DOCK_REGION_COLLAPSIBLE).unwrap_or(1) != 0,
    })
}

fn parse_layout(snapshot: &ValidatedSnapshot, node_id: u32) -> Option<DockLayoutSpec> {
    let node = snapshot.nodes.get(node_id as usize)?;
    match node.component {
        COMPONENT_DOCK_SPLIT => {
            let axis = match last_u32(snapshot, node, OP_DOCK_AXIS).unwrap_or(0) {
                0 => Axis::Horizontal,
                1 => Axis::Vertical,
                _ => return None,
            };
            let mut children = Vec::with_capacity(snapshot.children(node).len());
            for child_id in snapshot.children(node) {
                let child = snapshot.nodes.get(*child_id as usize)?;
                children.push(DockSplitChildSpec {
                    layout: parse_layout(snapshot, *child_id)?,
                    initial_size: last_f32(snapshot, child, OP_DOCK_INITIAL_SIZE_PX),
                });
            }
            Some(DockLayoutSpec::Split { axis, children })
        }
        COMPONENT_DOCK_TABS => {
            let mut panels = Vec::with_capacity(snapshot.children(node).len());
            for panel in snapshot.children(node) {
                panels.push(parse_panel(snapshot, *panel)?);
            }
            Some(DockLayoutSpec::Tabs {
                active_index: last_u32(snapshot, node, OP_DOCK_ACTIVE_INDEX).unwrap_or(0) as usize,
                panels,
            })
        }
        _ => None,
    }
}

fn parse_panel(snapshot: &ValidatedSnapshot, node_id: u32) -> Option<DockPanelSpec> {
    let node = snapshot.nodes.get(node_id as usize)?;
    if node.component != COMPONENT_DOCK_PANEL {
        return None;
    }
    let mut fields = node.data.split('\0');
    let id = fields.next()?;
    let title = fields.next()?;
    let trailing = fields.next()?;
    if id.is_empty() || !trailing.is_empty() || fields.next().is_some() {
        return None;
    }
    Some(DockPanelSpec {
        id: SharedString::from(id),
        title: SharedString::from(title),
        content_node: *snapshot.children(node).first()?,
        closable: last_u32(snapshot, node, OP_DOCK_PANEL_CLOSABLE).unwrap_or(1) != 0,
        zoomable: last_u32(snapshot, node, OP_DOCK_PANEL_ZOOMABLE).unwrap_or(1) != 0,
        inner_padding: last_u32(snapshot, node, OP_DOCK_PANEL_INNER_PADDING).unwrap_or(0) != 0,
    })
}

fn last_u32(snapshot: &ValidatedSnapshot, node: &SnapshotNode, code: u16) -> Option<u32> {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|operation| operation.code == code)
        .map(|operation| operation.a as u32)
}

fn last_f32(snapshot: &ValidatedSnapshot, node: &SnapshotNode, code: u16) -> Option<f32> {
    last_u32(snapshot, node, code).map(f32::from_bits)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn panel(id: &'static str, title: &'static str, content_node: u32) -> DockPanelSpec {
        DockPanelSpec {
            id: id.into(),
            title: title.into(),
            content_node,
            closable: true,
            zoomable: true,
            inner_padding: false,
        }
    }

    fn tabs(active_index: usize, panels: Vec<DockPanelSpec>) -> DockLayoutSpec {
        DockLayoutSpec::Tabs {
            active_index,
            panels,
        }
    }

    #[test]
    fn content_and_panel_presentation_changes_do_not_reset_native_layout() {
        let declaration = tabs(0, vec![panel("editor", "Editor", 3)]);
        let updated = DockLayoutSpec::Tabs {
            active_index: 0,
            panels: vec![DockPanelSpec {
                title: "Renamed".into(),
                content_node: 9,
                closable: false,
                zoomable: false,
                inner_padding: true,
                ..panel("editor", "Editor", 3)
            }],
        };

        assert!(declaration.same_structure(&updated));
    }

    #[test]
    fn declarative_structure_changes_replace_native_layout() {
        let declaration = DockLayoutSpec::Split {
            axis: Axis::Horizontal,
            children: vec![DockSplitChildSpec {
                layout: tabs(0, vec![panel("editor", "Editor", 3)]),
                initial_size: Some(240.0),
            }],
        };
        let changed_size = DockLayoutSpec::Split {
            axis: Axis::Horizontal,
            children: vec![DockSplitChildSpec {
                layout: tabs(0, vec![panel("editor", "Editor", 3)]),
                initial_size: Some(320.0),
            }],
        };
        let changed_panel = DockLayoutSpec::Split {
            axis: Axis::Horizontal,
            children: vec![DockSplitChildSpec {
                layout: tabs(0, vec![panel("preview", "Preview", 3)]),
                initial_size: Some(240.0),
            }],
        };

        assert!(!declaration.same_structure(&changed_size));
        assert!(!declaration.same_structure(&changed_panel));
    }
}
