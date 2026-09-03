use std::collections::{HashMap, HashSet};
use std::{cell::Cell, cell::RefCell, rc::Rc};

use gpui::{
    App, AppContext as _, Axis, Context, Entity, EventEmitter, FocusHandle, Focusable, IntoElement,
    ParentElement, Render, SharedString, Styled, WeakEntity, Window, div, px,
};
use gpui_base::dock::{
    DockArea, DockAreaState, DockLayout, DockPlacement, Panel as BasePanel, PanelEvent, PanelInfo,
    PanelState,
};

use crate::{
    abi::{ManagedCallbacks, NativeControlEvent},
    app_host::ManagedView,
    dock_skin::dock_area,
    resources::{ResourceCommand, ResourceKey, resource_key},
    semantic::{
        COMMAND_DOCK_CLOSE_PANEL, COMMAND_DOCK_EXPORT_LAYOUT, COMMAND_DOCK_IMPORT_LAYOUT,
        COMMAND_DOCK_SET_REGION_OPEN, COMPONENT_DOCK_PANEL, COMPONENT_DOCK_REGION,
        COMPONENT_DOCK_SPLIT, COMPONENT_DOCK_TABS, EVENT_DOCK_LAYOUT_CHANGED,
        EVENT_DOCK_LAYOUT_EXPORTED, EVENT_DOCK_PANEL_CLOSED, OP_DOCK_ACTIVE_INDEX, OP_DOCK_AXIS,
        OP_DOCK_INITIAL_SIZE_PX, OP_DOCK_LOCKED, OP_DOCK_ON_CLOSED, OP_DOCK_ON_LAYOUT,
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
    pub(crate) layout_token: u64,
    pub(crate) closed_token: u64,
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

    /// Removes natively closed panels from the structural declaration without
    /// touching their entities: a tombstoned panel stays closed until the
    /// managed declaration drops its id, so the next snapshot cannot resurrect
    /// it. Returns `None` when nothing declarable remains.
    fn without_tombstones(&self, tombstones: &HashSet<SharedString>) -> Option<DockConfiguration> {
        if tombstones.is_empty() {
            return Some(self.clone());
        }
        let center = self.center.without_tombstones(tombstones)?;
        let mut regions = Vec::with_capacity(self.regions.len());
        for region in &self.regions {
            let Some(layout) = region.layout.without_tombstones(tombstones) else {
                continue;
            };
            regions.push(DockRegionSpec {
                placement: region.placement,
                layout,
                initial_size: region.initial_size,
                initially_open: region.initially_open,
                collapsible: region.collapsible,
            });
        }
        Some(DockConfiguration {
            key: self.key.clone(),
            locked: self.locked,
            center,
            regions,
            layout_token: self.layout_token,
            closed_token: self.closed_token,
        })
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

    /// Prunes tombstoned panels, dropping groups left empty. Returns `None`
    /// when no live panel remains in this subtree.
    fn without_tombstones(&self, tombstones: &HashSet<SharedString>) -> Option<DockLayoutSpec> {
        match self {
            Self::Split { axis, children } => {
                let mut kept = Vec::with_capacity(children.len());
                for child in children {
                    let Some(layout) = child.layout.without_tombstones(tombstones) else {
                        continue;
                    };
                    kept.push(DockSplitChildSpec {
                        layout,
                        initial_size: child.initial_size,
                    });
                }
                if kept.is_empty() {
                    return None;
                }
                Some(DockLayoutSpec::Split {
                    axis: *axis,
                    children: kept,
                })
            }
            Self::Tabs {
                active_index,
                panels,
            } => {
                let kept: Vec<DockPanelSpec> = panels
                    .iter()
                    .filter(|panel| !tombstones.contains(&panel.id))
                    .cloned()
                    .collect();
                if kept.is_empty() {
                    return None;
                }
                Some(DockLayoutSpec::Tabs {
                    active_index: (*active_index).min(kept.len() - 1),
                    panels: kept,
                })
            }
        }
    }
}

/// Version of the GPUI.NET layout document envelope. The envelope is ours;
/// the nested layout is the foundation's opaque persisted state. Bumping this
/// is a protocol change: imports reject anything they do not understand.
const LAYOUT_ENVELOPE_FORMAT: u64 = 1;

/// Reads a layout document envelope produced by [`ManagedDockResource::export_layout`].
/// Only the current envelope is accepted: bare foundation documents and
/// unknown formats are rejected rather than guessed at.
fn parse_layout_envelope(document: &str) -> Option<DockAreaState> {
    let envelope: serde_json::Value = serde_json::from_str(document).ok()?;
    if envelope.get("format")?.as_u64()? != LAYOUT_ENVELOPE_FORMAT {
        return None;
    }
    serde_json::from_value(envelope.get("layout")?.clone()).ok()
}

/// Native event routing shared by one Dock area and its panels. Panels reach
/// it from `on_removed`, which fires inside base reconciliation without any
/// handle on the retaining resource.
pub(crate) struct DockEventSink {
    session: u64,
    callbacks: ManagedCallbacks,
    layout_token: Cell<u64>,
    closed_token: Cell<u64>,
    revision: Cell<u64>,
    /// Panels removed by the live declaration: their `on_removed` is
    /// application-driven and fires no close event.
    silent: RefCell<HashSet<SharedString>>,
    /// Panels closed natively while still declared: kept out of rebuilt
    /// layouts until the declaration drops their ids.
    tombstones: RefCell<HashSet<SharedString>>,
}

pub(crate) fn emit_dock_event(sink: &Rc<DockEventSink>, kind: u16, data: &[u8]) {
    let token = match kind {
        EVENT_DOCK_LAYOUT_CHANGED | EVENT_DOCK_LAYOUT_EXPORTED => sink.layout_token.get(),
        EVENT_DOCK_PANEL_CLOSED => sink.closed_token.get(),
        _ => 0,
    };
    if token == 0 {
        return;
    }
    let Some(callback) = sink.callbacks.control_event else {
        return;
    };
    let revision = sink.revision.get().wrapping_add(1).max(1);
    sink.revision.set(revision);
    let event = NativeControlEvent {
        kind,
        flags: 0,
        reserved: 0,
        revision,
        data: data.as_ptr(),
        data_length: data.len() as i32,
        reserved2: 0,
    };
    let _ = unsafe { callback(sink.session, token, &event) };
}

pub(crate) struct ManagedDockResource {
    area: Entity<DockArea>,
    panels: HashMap<SharedString, Entity<ManagedDockPanel>>,
    declaration: Option<DockConfiguration>,
    raw_ids: HashSet<SharedString>,
    raw: Option<DockConfiguration>,
    locked: bool,
    owner: WeakEntity<ManagedView>,
    events: Rc<DockEventSink>,
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
        session: u64,
        callbacks: ManagedCallbacks,
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Self {
        let area = dock_area(configuration.key.key.clone(), None, window, cx);
        let mut resource = Self {
            area,
            panels: HashMap::new(),
            declaration: None,
            raw_ids: HashSet::new(),
            raw: None,
            locked: configuration.locked,
            owner: owner.clone(),
            events: Rc::new(DockEventSink {
                session,
                callbacks,
                layout_token: Cell::new(0),
                closed_token: Cell::new(0),
                revision: Cell::new(0),
                silent: RefCell::new(HashSet::new()),
                tombstones: RefCell::new(HashSet::new()),
            }),
        };
        resource.configure(configuration, owner, window, cx);
        resource
    }

    pub(crate) fn area(&self) -> Entity<DockArea> {
        self.area.clone()
    }

    pub(crate) fn events(&self) -> Rc<DockEventSink> {
        self.events.clone()
    }

    pub(crate) fn configure(
        &mut self,
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) {
        self.owner = owner.clone();
        let mut declared = Vec::new();
        configuration.collect_panels(&mut declared);

        let mut raw_ids = HashSet::with_capacity(declared.len());
        for panel in &declared {
            raw_ids.insert(panel.id.clone());
        }
        // Panels the declaration dropped are application-driven: silence
        // their teardown and release their tombstones, so a later panel with
        // the same id installs fresh.
        let removed: Vec<SharedString> = self.raw_ids.difference(&raw_ids).cloned().collect();
        self.events
            .silent
            .borrow_mut()
            .extend(removed.iter().cloned());
        self.events
            .tombstones
            .borrow_mut()
            .retain(|id| raw_ids.contains(id));
        self.events.layout_token.set(configuration.layout_token);
        self.events.closed_token.set(configuration.closed_token);

        let effective = configuration.without_tombstones(&self.events.tombstones.borrow());
        let Some(effective) = effective.as_ref() else {
            // Every declared panel is natively closed: the native tree is
            // already correct, so there is nothing to reconcile.
            self.raw_ids = raw_ids;
            self.events.silent.borrow_mut().clear();
            return;
        };

        for panel in declared {
            if let Some(existing) = self.panels.get(&panel.id).cloned() {
                existing.update(cx, |existing, cx| {
                    existing.configure(panel, owner.clone(), cx)
                });
            } else {
                let events = self.events.clone();
                let created = cx.new(|cx| ManagedDockPanel::new(panel, owner.clone(), events, cx));
                self.panels.insert(panel.id.clone(), created);
            }
        }

        let center_changed = self
            .declaration
            .as_ref()
            .is_none_or(|previous| !previous.center.same_structure(&effective.center));
        let locked_changed = self.locked != effective.locked;
        let mut region_updates = Vec::new();
        let mut region_structure_changed = false;
        for placement in [
            DockPlacement::Left,
            DockPlacement::Bottom,
            DockPlacement::Right,
        ] {
            let current = effective.region(placement);
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
                                .then(|| build_layout(&current.layout, &self.panels)),
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
            let center = center_changed.then(|| build_layout(&effective.center, &self.panels));
            let locked = effective.locked;
            let declaration_is_none = self.declaration.is_none();
            self.area.update(cx, |area, cx| {
                if locked_changed || declaration_is_none {
                    area.set_locked(locked, window, cx);
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

        self.events.silent.borrow_mut().clear();
        self.panels.retain(|id, _| raw_ids.contains(id));
        self.declaration = Some(effective.clone());
        self.raw = Some(configuration.clone());
        self.raw_ids = raw_ids;
        self.locked = effective.locked;
    }

    /// Applies one queued controller command. All commands consume on first
    /// materialization; failures (unknown panel, malformed document) are
    /// no-ops rather than retried, since the snapshot that produced them will
    /// not change under a retry.
    pub(crate) fn apply_command(
        &mut self,
        command: &ResourceCommand,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) {
        let data: &str = command.data.as_ref();
        match command.command {
            COMMAND_DOCK_CLOSE_PANEL => self.close_panel(data, window, cx),
            COMMAND_DOCK_SET_REGION_OPEN => {
                self.set_region_open(command.a as u32, command.b != 0, window, cx)
            }
            COMMAND_DOCK_IMPORT_LAYOUT => self.import_layout(data, window, cx),
            COMMAND_DOCK_EXPORT_LAYOUT => self.export_layout(cx),
            _ => {}
        }
    }

    fn close_panel(&mut self, id: &str, window: &mut Window, cx: &mut Context<ManagedView>) {
        let Some(panel) = self.panels.get(id).cloned() else {
            return;
        };
        // Not silenced: a controller close is a native close and fires the
        // closed event like the chrome control does.
        self.area.update(cx, |area, cx| {
            area.remove_panel(panel, window, cx);
        });
    }

    fn set_region_open(
        &mut self,
        side: u32,
        open: bool,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) {
        let placement = match side {
            0 => DockPlacement::Left,
            1 => DockPlacement::Bottom,
            2 => DockPlacement::Right,
            _ => return,
        };
        self.area.update(cx, |area, cx| {
            if area.is_dock_open(placement) != open {
                area.toggle_dock(placement, window, cx);
            }
        });
    }

    /// Replaces native structure from an exported layout document. Structure
    /// (splits, sizes, active tabs, region placement and open state) comes
    /// from the document; panel content, titles, and options come from the
    /// live declaration, joined by panel id. Unknown persisted panels are
    /// pruned; declared panels missing from the document are appended to the
    /// center, so an import never silently drops live content. Lock state
    /// always comes from the declaration.
    fn import_layout(
        &mut self,
        document: &str,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) {
        let Some(state) = parse_layout_envelope(document) else {
            return;
        };
        let Some(configuration) = self.import_configuration(&state) else {
            return;
        };
        // An explicit import restores what it names: tombstones only guard
        // against snapshot resurrection, not against a deliberate restore.
        let mut restored = Vec::new();
        configuration.collect_panels(&mut restored);
        let restored: HashSet<&SharedString> = restored.iter().map(|spec| &spec.id).collect();
        self.events
            .tombstones
            .borrow_mut()
            .retain(|id| !restored.contains(id));
        let owner = self.owner.clone();
        self.configure(&configuration, owner, window, cx);
        // `configure` only seeds open state for newly added regions; an
        // import restores it for existing ones too.
        self.area.update(cx, |area, cx| {
            for region in &configuration.regions {
                if area.is_dock_open(region.placement) != region.initially_open {
                    area.toggle_dock(region.placement, window, cx);
                }
                if let Some(size) = region.initial_size {
                    area.set_dock_size(region.placement, px(size), window, cx);
                }
            }
        });
    }

    fn export_layout(&mut self, cx: &mut Context<ManagedView>) {
        let state = self.area.read(cx).dump(cx);
        let document = serde_json::json!({
            "format": LAYOUT_ENVELOPE_FORMAT,
            "layout": state,
        })
        .to_string();
        emit_dock_event(
            &self.events,
            EVENT_DOCK_LAYOUT_EXPORTED,
            document.as_bytes(),
        );
    }

    /// Joins an exported document with the live declaration. Structure comes
    /// from the document; every panel spec (content, title, options) comes
    /// from the declaration. Returns `None` when the document names no
    /// usable center.
    fn import_configuration(&self, state: &DockAreaState) -> Option<DockConfiguration> {
        // Joined against the raw declaration: tombstoned panels are still
        // declared, and an explicit import restores the ones it names.
        let raw = self.raw.as_ref()?;
        let mut specs = HashMap::new();
        let mut declared = Vec::new();
        raw.collect_panels(&mut declared);
        for spec in declared {
            specs.insert(spec.id.as_str(), spec);
        }
        let mut center = layout_spec_from_state(&state.center, &specs)?;
        let mut regions = Vec::new();
        for dock in [&state.left_dock, &state.right_dock, &state.bottom_dock]
            .into_iter()
            .flatten()
        {
            let Some(layout) = layout_spec_from_state(dock.panel(), &specs) else {
                continue;
            };
            let collapsible = raw
                .region(dock.placement())
                .map_or(true, |region| region.collapsible);
            regions.push(DockRegionSpec {
                placement: dock.placement(),
                layout,
                initial_size: Some(dock.size().as_f32()),
                initially_open: dock.open(),
                collapsible,
            });
        }
        // Declared panels missing from the document are appended to the
        // center, so an import never silently drops live content. Panels the
        // user closed natively stay closed unless the document names them.
        let mut placed = HashSet::new();
        let mut collected = Vec::new();
        center.collect_panels(&mut collected);
        for region in &regions {
            region.layout.collect_panels(&mut collected);
        }
        placed.extend(collected.iter().map(|spec| spec.id.clone()));
        let tombstones = self.events.tombstones.borrow();
        let mut missing: Vec<DockPanelSpec> = specs
            .values()
            .filter(|spec| !placed.contains(&spec.id) && !tombstones.contains(&spec.id))
            .map(|spec| (*spec).clone())
            .collect();
        missing.sort_by(|left, right| left.id.as_str().cmp(right.id.as_str()));
        if !missing.is_empty() {
            append_panels(&mut center, missing);
        }
        Some(DockConfiguration {
            key: raw.key.clone(),
            locked: raw.locked,
            center,
            regions,
            layout_token: raw.layout_token,
            closed_token: raw.closed_token,
        })
    }
}

/// Converts one persisted layout node into a declarative spec, resolving
/// leaves against the live declaration by panel id. Unknown leaves are
/// pruned; tiles subtrees (never declared by the managed schema) are
/// skipped; malformed nodes resolve to `None`.
fn layout_spec_from_state(
    state: &PanelState,
    specs: &HashMap<&str, &DockPanelSpec>,
) -> Option<DockLayoutSpec> {
    match &state.info {
        PanelInfo::Stack { sizes, .. } => {
            let axis = state.info.axis()?;
            let mut children = Vec::with_capacity(state.children.len());
            for (index, child) in state.children.iter().enumerate() {
                let Some(layout) = layout_spec_from_state(child, specs) else {
                    continue;
                };
                children.push(DockSplitChildSpec {
                    layout,
                    // The writer emits 0.0 for unconstrained slots, which
                    // reads back as no constraint rather than a zero panel.
                    initial_size: sizes.get(index).and_then(|size| {
                        let slots = size.as_f32();
                        (slots > 0.0).then_some(slots)
                    }),
                });
            }
            if children.is_empty() {
                return None;
            }
            Some(DockLayoutSpec::Split { axis, children })
        }
        PanelInfo::Tabs { active_index } => {
            let mut panels = Vec::with_capacity(state.children.len());
            for child in &state.children {
                let PanelInfo::Panel(value) = &child.info else {
                    continue;
                };
                let Some(id) = value.get("id").and_then(|id| id.as_str()) else {
                    continue;
                };
                let Some(spec) = specs.get(id) else {
                    continue;
                };
                panels.push((*spec).clone());
            }
            if panels.is_empty() {
                return None;
            }
            Some(DockLayoutSpec::Tabs {
                active_index: (*active_index).min(panels.len() - 1),
                panels,
            })
        }
        // A lone leaf where a container belongs wraps into a tab group; any
        // other shape (including tiles) has no declarative equivalent.
        PanelInfo::Panel(value) => {
            let id = value.get("id")?.as_str()?;
            let spec = specs.get(id)?;
            Some(DockLayoutSpec::Tabs {
                active_index: 0,
                panels: vec![(*spec).clone()],
            })
        }
        PanelInfo::Tiles { .. } => None,
    }
}

/// Appends orphaned declaration panels to the first tab group, depth-first,
/// so an import preserves content the document does not name.
fn append_panels(center: &mut DockLayoutSpec, missing: Vec<DockPanelSpec>) {
    if missing.is_empty() {
        return;
    }
    if let Some(tabs) = first_tabs_mut(center) {
        tabs.extend(missing);
        return;
    }
    // Unreachable through `layout_spec_from_state` (converted centers always
    // contain a tab group), but a split without one must still absorb content.
    if let DockLayoutSpec::Split { children, .. } = center {
        children.push(DockSplitChildSpec {
            layout: DockLayoutSpec::Tabs {
                active_index: 0,
                panels: missing,
            },
            initial_size: None,
        });
    }
}

fn first_tabs_mut(spec: &mut DockLayoutSpec) -> Option<&mut Vec<DockPanelSpec>> {
    match spec {
        DockLayoutSpec::Tabs { panels, .. } => Some(panels),
        DockLayoutSpec::Split { children, .. } => children
            .iter_mut()
            .find_map(|child| first_tabs_mut(&mut child.layout)),
    }
}

fn build_layout(
    specification: &DockLayoutSpec,
    panels: &HashMap<SharedString, Entity<ManagedDockPanel>>,
) -> DockLayout {
    match specification {
        DockLayoutSpec::Split { axis, children } => {
            let mut layout = match axis {
                Axis::Horizontal => DockLayout::h_split(),
                Axis::Vertical => DockLayout::v_split(),
            };
            for child in children {
                layout = layout.child(
                    build_layout(&child.layout, panels),
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
                layout = layout.panel(entity);
            }
            layout
        }
    }
}

pub(crate) struct ManagedDockPanel {
    id: SharedString,
    owner: WeakEntity<ManagedView>,
    title: SharedString,
    content_node: u32,
    closable: bool,
    zoomable: bool,
    #[allow(dead_code)]
    inner_padding: bool,
    focus_handle: FocusHandle,
    events: Rc<DockEventSink>,
}

impl ManagedDockPanel {
    fn new(
        specification: &DockPanelSpec,
        owner: WeakEntity<ManagedView>,
        events: Rc<DockEventSink>,
        cx: &mut Context<Self>,
    ) -> Self {
        Self {
            id: specification.id.clone(),
            owner,
            title: specification.title.clone(),
            content_node: specification.content_node,
            closable: specification.closable,
            zoomable: specification.zoomable,
            inner_padding: specification.inner_padding,
            focus_handle: cx.focus_handle(),
            events,
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

    /// The tab title the local dock skin draws. Presentation lives in the
    /// skin; this accessor keeps the title reachable through the foundation's
    /// object-safe panel handle.
    pub(crate) fn tab_title(&self) -> SharedString {
        self.title.clone()
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

    fn dump(&self, _: &App) -> PanelState {
        // The panel id rejoins exported structure with the live declaration
        // on import; titles, flags, and content always come from the
        // declaration, never from the document.
        PanelState {
            panel_name: "GpuiDotnetPanel".to_string(),
            children: Vec::new(),
            info: PanelInfo::panel(serde_json::json!({ "id": self.id.as_str() })),
        }
    }

    /// A panel leaving the Dock for good. Declaration-driven removals were
    /// silenced up front; anything else is a native close, which tombstones
    /// the id (so snapshots cannot resurrect it) and notifies the bound
    /// closed handler, if any.
    fn on_removed(&mut self, _window: &mut Window, _cx: &mut Context<Self>) {
        if self.events.silent.borrow_mut().remove(&self.id) {
            return;
        }
        if !self.events.tombstones.borrow_mut().insert(self.id.clone()) {
            return;
        }
        emit_dock_event(
            &self.events,
            EVENT_DOCK_PANEL_CLOSED,
            self.id.as_str().as_bytes(),
        );
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
        layout_token: last_callback(snapshot, node, OP_DOCK_ON_LAYOUT),
        closed_token: last_callback(snapshot, node, OP_DOCK_ON_CLOSED),
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

fn last_callback(snapshot: &ValidatedSnapshot, node: &SnapshotNode, code: u16) -> u64 {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|operation| operation.code == code)
        .map_or(0, |operation| operation.a)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::semantic::RESOURCE_DOCK;

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

    fn leaf(id: &str) -> PanelState {
        let mut state = PanelState::new("GpuiDotnetPanel");
        state.info = PanelInfo::panel(serde_json::json!({ "id": id }));
        state
    }
    fn specs<'a>(panels: &'a [DockPanelSpec]) -> HashMap<&'a str, &'a DockPanelSpec> {
        panels.iter().map(|spec| (spec.id.as_str(), spec)).collect()
    }

    #[test]
    fn persisted_tabs_resolve_live_specs_and_prune_unknown_panels() {
        let declared = vec![panel("editor", "Editor", 3), panel("preview", "Preview", 4)];
        let specs = specs(&declared);
        let mut state = PanelState::new("TabPanel");
        state.info = PanelInfo::tabs(5);
        state.children = vec![leaf("editor"), leaf("ghost"), leaf("preview")];

        let converted = layout_spec_from_state(&state, &specs).expect("known panels convert");
        let DockLayoutSpec::Tabs {
            active_index,
            panels,
        } = converted
        else {
            panic!("tabs persist as tabs");
        };
        // Out-of-range active index clamps to the pruned group; content and
        // presentation come from the live declaration, not the document.
        assert_eq!(active_index, 1);
        assert_eq!(panels.len(), 2);
        assert_eq!(panels[0].content_node, 3);
        assert_eq!(panels[1].title.as_str(), "Preview");
    }

    #[test]
    fn persisted_splits_keep_axes_and_slot_sizes() {
        let declared = vec![panel("a", "A", 1), panel("b", "B", 2)];
        let specs = specs(&declared);
        let mut left = PanelState::new("TabPanel");
        left.info = PanelInfo::tabs(0);
        left.children = vec![leaf("a")];
        let mut right = PanelState::new("TabPanel");
        right.info = PanelInfo::tabs(0);
        right.children = vec![leaf("b")];
        let mut state = PanelState::new("StackPanel");
        state.info = PanelInfo::stack(vec![px(240.), px(0.)], Axis::Vertical);
        state.children = vec![left, right];

        let converted = layout_spec_from_state(&state, &specs).expect("splits convert");
        let DockLayoutSpec::Split { axis, children } = converted else {
            panic!("stacks persist as splits");
        };
        assert_eq!(axis, Axis::Vertical);
        assert_eq!(children.len(), 2);
        assert_eq!(children[0].initial_size, Some(240.0));
        // Zero slots persist as unconstrained, matching the tree encoding.
        assert_eq!(children[1].initial_size, None);
    }

    #[test]
    fn tiles_and_foreign_leaves_do_not_convert() {
        let declared = vec![panel("a", "A", 1)];
        let specs = specs(&declared);
        let mut state = PanelState::new("Tiles");
        state.info = PanelInfo::tiles(vec![]);
        state.children = vec![leaf("a")];
        assert!(layout_spec_from_state(&state, &specs).is_none());

        let mut foreign = PanelState::new("SomethingElse");
        foreign.info = PanelInfo::panel(serde_json::json!({ "other": true }));
        assert!(layout_spec_from_state(&foreign, &specs).is_none());
    }

    fn enveloped(layout: serde_json::Value) -> String {
        serde_json::json!({ "format": LAYOUT_ENVELOPE_FORMAT, "layout": layout }).to_string()
    }

    fn empty_layout() -> serde_json::Value {
        let mut center = PanelState::new("StackPanel");
        center.info = PanelInfo::stack(vec![], Axis::Horizontal);
        serde_json::to_value(DockAreaState {
            center,
            ..Default::default()
        })
        .expect("persisted state serializes")
    }

    #[test]
    fn layout_envelope_accepts_only_the_current_format() {
        let layout = empty_layout();
        assert!(parse_layout_envelope(&enveloped(layout.clone())).is_some());
        // Bare foundation documents are rejected rather than guessed at.
        assert!(parse_layout_envelope(&layout.to_string()).is_none());
        assert!(parse_layout_envelope("not json").is_none());
        assert!(
            parse_layout_envelope(
                &serde_json::json!({ "format": LAYOUT_ENVELOPE_FORMAT + 1, "layout": layout })
                    .to_string()
            )
            .is_none()
        );
        assert!(
            parse_layout_envelope(
                &serde_json::json!({ "format": LAYOUT_ENVELOPE_FORMAT }).to_string()
            )
            .is_none()
        );
    }

    #[test]
    fn tombstoned_panels_leave_the_declaration_without_touching_entities() {
        let configuration = DockConfiguration {
            key: ResourceKey::new(7, "dock".into()),
            locked: false,
            center: tabs(
                0,
                vec![panel("editor", "Editor", 3), panel("preview", "Preview", 4)],
            ),
            regions: Vec::new(),
            layout_token: 0,
            closed_token: 0,
        };
        let tombstones: HashSet<SharedString> = ["preview".into()].into_iter().collect();
        let effective = configuration
            .without_tombstones(&tombstones)
            .expect("one live panel remains");
        let mut collected = Vec::new();
        effective.collect_panels(&mut collected);
        assert_eq!(collected.len(), 1);
        assert_eq!(collected[0].id.as_str(), "editor");

        let all: HashSet<SharedString> = ["editor".into(), "preview".into()].into_iter().collect();
        assert!(configuration.without_tombstones(&all).is_none());
    }

    #[test]
    fn import_appends_orphaned_declaration_panels_to_the_center() {
        let mut center = tabs(0, vec![panel("editor", "Editor", 3)]);
        append_panels(&mut center, vec![panel("preview", "Preview", 4)]);
        let DockLayoutSpec::Tabs { panels, .. } = &center else {
            panic!("tabs stay tabs");
        };
        assert_eq!(panels.len(), 2);
        assert_eq!(panels[1].id.as_str(), "preview");

        append_panels(&mut center, Vec::new());
        let DockLayoutSpec::Tabs { panels, .. } = &center else {
            panic!("tabs stay tabs");
        };
        assert_eq!(panels.len(), 2);
    }

    use std::cell::RefCell as TestRefCell;

    use crate::{
        abi::NativeControlEvent, app_host::ManagedView, resources::ResourceStore,
        theme::NativeTheme,
    };

    thread_local! {
        static CAPTURED: TestRefCell<Vec<(u64, u16, u64, Vec<u8>)>> =
            TestRefCell::new(Vec::new());
    }

    unsafe extern "C" fn capture_event(
        _: u64,
        token: u64,
        event: *const NativeControlEvent,
    ) -> i32 {
        let event = unsafe { &*event };
        let data =
            unsafe { std::slice::from_raw_parts(event.data, event.data_length as usize) }.to_vec();
        CAPTURED.with(|captured| {
            captured
                .borrow_mut()
                .push((token, event.kind, event.revision, data))
        });
        0
    }

    fn capture_callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            click: None,
            list_render_range: None,
            dynamic_frame: None,
            control_event: Some(capture_event),
            application_started: None,
            window_closed: None,
            menu_action: None,
        }
    }

    fn dock_key() -> ResourceKey {
        ResourceKey::new(7, "dock".into())
    }

    fn area_configuration() -> DockConfiguration {
        DockConfiguration {
            key: dock_key(),
            locked: false,
            center: tabs(
                0,
                vec![panel("editor", "Editor", 3), panel("preview", "Preview", 4)],
            ),
            regions: vec![DockRegionSpec {
                placement: DockPlacement::Left,
                layout: tabs(0, vec![panel("files", "Files", 5)]),
                initial_size: Some(200.0),
                initially_open: true,
                collapsible: true,
            }],
            layout_token: 11,
            closed_token: 12,
        }
    }

    fn dock_command(command: u16, a: u64, b: u64, data: &str) -> ResourceCommand {
        ResourceCommand {
            key: dock_key(),
            resource_kind: RESOURCE_DOCK,
            command,
            a,
            b,
            data: data.into(),
        }
    }

    fn center_panel_ids(area: &Entity<DockArea>, cx: &App) -> Vec<String> {
        let state = area.read(cx).dump(cx);
        assert_eq!(state.center.children.len(), 1);
        state.center.children[0]
            .children
            .iter()
            .filter_map(|leaf| match &leaf.info {
                PanelInfo::Panel(value) => value
                    .get("id")
                    .and_then(|id| id.as_str())
                    .map(str::to_string),
                _ => None,
            })
            .collect()
    }

    /// Controller commands travel the real dispatch/pending/materialize path
    /// into a live area: close tombstones and emits, region open toggles,
    /// export captures JSON, and import restores from it.
    #[gpui::test]
    fn dock_controller_commands_drive_the_retained_area(cx: &mut gpui::TestAppContext) {
        cx.update(|cx| {
            gpui_base::init(cx);
        });
        CAPTURED.with(|captured| captured.borrow_mut().clear());
        let theme = Rc::new(TestRefCell::new(NativeTheme::default()));
        let view = cx.update(|cx| cx.new(|_| ManagedView::new(1, capture_callbacks(), theme)));
        let owner = view.downgrade();
        let store = Rc::new(ResourceStore::new(
            1,
            capture_callbacks(),
            Rc::new(TestRefCell::new(NativeTheme::default())),
        ));
        cx.update(|cx| {
            view.update(cx, |view, _| view.resources = store.clone());
        });
        let configuration = area_configuration();
        let (_, cx) = cx.add_window_view(|_, _| gpui::Empty);

        let materialize = |cx: &mut gpui::VisualTestContext| {
            cx.update(|window, cx| {
                view.update(cx, |_, cx| {
                    store.dock_resource(&configuration, owner.clone(), window, cx)
                })
            })
        };
        let area = materialize(cx);
        let drained = || CAPTURED.with(|captured| std::mem::take(&mut *captured.borrow_mut()));
        let _ = drained();

        // Export first: two panels, before anything closes.
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_EXPORT_LAYOUT, 0, 0, "")));
        materialize(cx);
        let exported = drained()
            .into_iter()
            .find(|(token, kind, _, _)| *token == 11 && *kind == EVENT_DOCK_LAYOUT_EXPORTED)
            .expect("export emits the layout document");
        let document = String::from_utf8(exported.3).expect("export is UTF-8 JSON");
        assert!(document.contains("\"editor\"") && document.contains("\"preview\""));
        let envelope: serde_json::Value =
            serde_json::from_str(&document).expect("export is an envelope");
        assert_eq!(
            envelope.get("format").and_then(|format| format.as_u64()),
            Some(LAYOUT_ENVELOPE_FORMAT)
        );
        assert!(
            envelope
                .get("layout")
                .is_some_and(|layout| layout.is_object())
        );

        // Controller close fires the closed event, tombstones the id against
        // resurrection, and reports the coarse layout change.
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_CLOSE_PANEL, 0, 0, "preview")));
        materialize(cx);
        assert_eq!(cx.read(|cx| center_panel_ids(&area, cx)), vec!["editor"]);
        let events = drained();
        let closed = events
            .iter()
            .find(|(token, kind, _, _)| *token == 12 && *kind == EVENT_DOCK_PANEL_CLOSED)
            .expect("close emits the panel id");
        assert_eq!(closed.3, b"preview");
        assert!(
            events
                .iter()
                .any(|(token, kind, _, _)| *token == 11 && *kind == EVENT_DOCK_LAYOUT_CHANGED)
        );
        // Revisions advance monotonically across kinds on one area.
        let revisions: Vec<u64> = events.iter().map(|(_, _, revision, _)| *revision).collect();
        assert!(revisions.windows(2).all(|pair| pair[0] < pair[1]));

        // The tombstoned panel survives re-materialization of the same
        // declaration instead of resurrecting.
        materialize(cx);
        assert_eq!(cx.read(|cx| center_panel_ids(&area, cx)), vec!["editor"]);
        let _ = drained();

        // Importing the pre-close document restores the panel explicitly.
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_IMPORT_LAYOUT, 0, 0, &document)));
        materialize(cx);
        assert_eq!(
            cx.read(|cx| center_panel_ids(&area, cx)),
            vec!["editor", "preview"]
        );
        let _ = drained();

        // Region open state toggles programmatically.
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_SET_REGION_OPEN, 0, 0, "")));
        materialize(cx);
        assert!(!cx.read(|cx| area.read(cx).is_dock_open(DockPlacement::Left)));
        let _ = drained();
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_SET_REGION_OPEN, 0, 1, "")));
        materialize(cx);
        assert!(cx.read(|cx| area.read(cx).is_dock_open(DockPlacement::Left)));
        let _ = drained();

        // Unknown panels and malformed documents are consumed silently.
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_CLOSE_PANEL, 0, 0, "ghost")));
        assert!(!store.dispatch(dock_command(COMMAND_DOCK_IMPORT_LAYOUT, 0, 0, "not json")));
        materialize(cx);
        assert_eq!(
            cx.read(|cx| center_panel_ids(&area, cx)),
            vec!["editor", "preview"]
        );
        assert!(drained().is_empty());
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
