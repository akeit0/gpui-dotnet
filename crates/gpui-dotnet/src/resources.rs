use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
};

use gpui::{
    AnyElement, AppContext, Context, Entity, IntoElement, ListAlignment, ListOffset, ListState,
    ParentElement, Pixels, Point, ScrollHandle, SharedString, Subscription, WeakEntity, Window,
    div, point, px,
};

use crate::{
    abi::{ManagedCallbacks, NativeResourceCommand},
    app_host::ManagedView,
    arena::with_render_output,
    dock::{DockConfiguration, ManagedDockResource, dock_configuration},
    extension::{
        NativeExtensionResourceKey, NativeExtensionStore, declaration as extension_declaration,
    },
    input::{InputBindings, InputInitialState, ManagedInput},
    scrolling::{DEFAULT_SCROLLBAR_WIDTH, ScrollbarMetrics},
    semantic::{
        COMMAND_LIST_REFRESH, COMMAND_LIST_RESET, COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_LIST_SPLICE,
        COMMAND_SCROLL_TO_BOTTOM, COMMAND_SCROLL_TO_OFFSET, COMMAND_SCROLL_TO_TOP, NativeAdapter,
        OP_INPUT_DISABLED, OP_INPUT_ON_CHANGED, OP_INPUT_ON_FOCUS_CHANGED, OP_INPUT_ON_SUBMITTED,
        OP_INPUT_PASSWORD, OP_INPUT_READ_ONLY, OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE,
        OP_LIST_CONTENT_REVISION, OP_LIST_ESTIMATED_ITEM_HEIGHT_PX, OP_LIST_ITEM_COUNT,
        OP_LIST_ITEM_ID, OP_LIST_OVERDRAW_PX, OP_LIST_RENDERER, OP_RESOURCE_OWNER,
        OP_SCROLLBAR_GUTTER, OP_SCROLLBAR_WIDTH, OP_SLIDER_AXIS, OP_SLIDER_DISABLED, OP_SLIDER_MAX,
        OP_SLIDER_MIN, OP_SLIDER_ON_CHANGED, OP_SLIDER_ON_RELEASED, OP_SLIDER_RANGE_END,
        OP_SLIDER_RANGE_START, OP_SLIDER_SCALE, OP_SLIDER_STEP, OP_SLIDER_VALUE, OP_TABLE_COLUMN,
        RESOURCE_DOCK, RESOURCE_INPUT, RESOURCE_LIST, RESOURCE_SCROLL, RESOURCE_SLIDER,
        component_metadata,
    },
    slider::{ManagedSlider, SliderValue},
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
    theme::{NativeTheme, SharedTheme},
};

#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub(crate) struct ResourceKey {
    pub(crate) owner_view: u32,
    pub(crate) key: SharedString,
}

impl ResourceKey {
    pub(crate) fn new(owner_view: u32, key: SharedString) -> Self {
        Self { owner_view, key }
    }
}

#[derive(Clone, Debug)]
pub(crate) struct ResourceCommand {
    pub(crate) key: ResourceKey,
    pub(crate) resource_kind: u16,
    pub(crate) command: u16,
    pub(crate) a: u64,
    pub(crate) b: u64,
    pub(crate) data: SharedString,
}

impl ResourceCommand {
    pub(crate) fn from_abi(command: &NativeResourceCommand, key: &str, data: &str) -> Self {
        Self {
            key: ResourceKey::new(command.owner_view, SharedString::new(Arc::<str>::from(key))),
            resource_kind: command.resource_kind,
            command: command.command,
            a: command.a,
            b: command.b,
            data: SharedString::new(Arc::<str>::from(data)),
        }
    }
}

pub(crate) struct ResourceStore {
    session_id: u64,
    callbacks: ManagedCallbacks,
    theme: SharedTheme,
    scrolls: RefCell<HashMap<ResourceKey, Rc<ManagedScrollResource>>>,
    lists: RefCell<HashMap<ResourceKey, Rc<RefCell<ManagedListResource>>>>,
    tables: RefCell<HashMap<ResourceKey, Rc<TableSpec>>>,
    inputs: RefCell<HashMap<ResourceKey, Entity<ManagedInput>>>,
    sliders: RefCell<HashMap<ResourceKey, Entity<ManagedSlider>>>,
    docks: RefCell<HashMap<ResourceKey, Rc<RefCell<ManagedDockResource>>>>,
    dock_subscriptions: RefCell<HashMap<ResourceKey, Subscription>>,
    extensions: NativeExtensionStore,
    pending: RefCell<HashMap<(u16, ResourceKey), Vec<ResourceCommand>>>,
    active_scratch: RefCell<HashSet<(u16, ResourceKey)>>,
    extension_active_scratch: RefCell<HashSet<NativeExtensionResourceKey>>,
}

impl ResourceStore {
    pub(crate) fn invalidate_artifacts(&self, keys: &[crate::abi::NativeArtifactKey]) -> bool {
        let mut changed = false;
        for engine in self.lists.borrow().values() {
            changed |= engine.borrow_mut().invalidate_artifacts(keys);
        }
        changed
    }

    pub(crate) fn publish_presence(
        &self,
        presence: &mut crate::presence::ResourcePresence,
        revision: u64,
    ) {
        presence.accept(
            &self.active_scratch.borrow(),
            &self.extension_active_scratch.borrow(),
            revision,
        );
    }

    pub(crate) fn new(session_id: u64, callbacks: ManagedCallbacks, theme: SharedTheme) -> Self {
        Self {
            session_id,
            callbacks,
            theme,
            scrolls: RefCell::new(HashMap::new()),
            lists: RefCell::new(HashMap::new()),
            tables: RefCell::new(HashMap::new()),
            inputs: RefCell::new(HashMap::new()),
            sliders: RefCell::new(HashMap::new()),
            docks: RefCell::new(HashMap::new()),
            dock_subscriptions: RefCell::new(HashMap::new()),
            extensions: NativeExtensionStore::new(),
            pending: RefCell::new(HashMap::new()),
            active_scratch: RefCell::new(HashSet::new()),
            extension_active_scratch: RefCell::new(HashSet::new()),
        }
    }

    pub(crate) fn theme(&self) -> NativeTheme {
        *self.theme.borrow()
    }

    pub(crate) fn extensions(&self) -> &NativeExtensionStore {
        &self.extensions
    }

    pub(crate) fn scroll_resource(&self, key: &ResourceKey) -> Rc<ManagedScrollResource> {
        if let Some(resource) = self.scrolls.borrow().get(key) {
            return resource.clone();
        }

        let resource = Rc::new(ManagedScrollResource::new());
        self.scrolls
            .borrow_mut()
            .insert(key.clone(), resource.clone());
        self.apply_pending(RESOURCE_SCROLL, key);
        resource
    }

    pub(crate) fn list_resource(
        &self,
        key: &ResourceKey,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
    ) -> Rc<RefCell<ManagedListResource>> {
        let existing = self.lists.borrow().get(key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = Rc::new(RefCell::new(ManagedListResource::new(
                self.session_id,
                self.callbacks,
                configuration,
                snapshot_revision,
            )));
            self.lists.borrow_mut().insert(key.clone(), created.clone());
            created
        };

        resource
            .borrow_mut()
            .configure(configuration, snapshot_revision);
        self.apply_pending(RESOURCE_LIST, key);
        resource
    }

    /// Binds the column metadata declared by the current snapshot to a table's row engine.
    /// A changed column table changes row layout, so every cached row batch is invalidated.
    pub(crate) fn bind_table_spec(
        &self,
        key: &ResourceKey,
        spec: Rc<TableSpec>,
        row_engine: &Rc<RefCell<ManagedListResource>>,
    ) {
        let mut tables = self.tables.borrow_mut();
        if matches!(tables.get(key), Some(existing) if **existing == *spec) {
            return;
        }
        row_engine.borrow_mut().invalidate_all_batches();
        tables.insert(key.clone(), spec);
    }

    pub(crate) fn table_spec(&self, key: &ResourceKey) -> Option<Rc<TableSpec>> {
        self.tables.borrow().get(key).cloned()
    }

    /// Clones the row-engine handles for diagnostics aggregation. Safe outside the render path
    /// (frame boundaries hold no RefCell borrows).
    pub(crate) fn list_engines(&self) -> Vec<Rc<RefCell<ManagedListResource>>> {
        self.lists.borrow().values().cloned().collect()
    }

    /// Discards retained managed row snapshots after an ambient theme or managed-code update.
    /// Tables use the same row engines as lists, so this covers both.
    pub(crate) fn invalidate_managed_rendered_rows(&self) {
        for engine in self.list_engines() {
            engine.borrow_mut().invalidate_all_batches();
        }
    }

    pub(crate) fn list_engine_count(&self) -> usize {
        self.lists.borrow().len()
    }

    pub(crate) fn input_resource(
        &self,
        configuration: &InputConfiguration,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Entity<ManagedInput> {
        let existing = self.inputs.borrow().get(&configuration.key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = cx.new(|cx| {
                ManagedInput::new(
                    self.session_id,
                    self.callbacks,
                    InputInitialState {
                        value: configuration.initial_value.as_ref(),
                        placeholder: configuration.placeholder.as_ref(),
                        disabled: configuration.disabled,
                        read_only: configuration.read_only,
                        password: configuration.password,
                        bindings: configuration.bindings,
                    },
                    self.theme.clone(),
                    cx,
                )
            });
            self.inputs
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            created
        };

        resource.update(cx, |input, cx| {
            input.configure(
                configuration.placeholder.as_ref(),
                configuration.disabled,
                configuration.read_only,
                configuration.password,
                configuration.bindings,
                cx,
            );
        });
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_INPUT, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.update(cx, |input, cx| input.apply_command(&command, window, cx));
        }
        resource
    }

    pub(crate) fn slider_resource(
        &self,
        configuration: &SliderConfiguration,
        cx: &mut Context<ManagedView>,
    ) -> Entity<ManagedSlider> {
        let existing = self.sliders.borrow().get(&configuration.key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = cx.new(|cx| {
                ManagedSlider::new(
                    self.session_id,
                    self.callbacks,
                    configuration,
                    self.theme.clone(),
                    cx,
                )
            });
            self.sliders
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            created
        };

        resource.update(cx, |slider, cx| slider.configure(configuration, cx));
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_SLIDER, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.update(cx, |slider, cx| slider.apply_command(&command, cx));
        }
        resource
    }

    pub(crate) fn dock_resource(
        &self,
        configuration: &DockConfiguration,
        owner: WeakEntity<ManagedView>,
        window: &mut Window,
        cx: &mut Context<ManagedView>,
    ) -> Entity<gpui_base::dock::DockArea> {
        let resource = if let Some(existing) = self.docks.borrow().get(&configuration.key).cloned()
        {
            existing
                .borrow_mut()
                .configure(configuration, owner, window, cx);
            existing
        } else {
            let created = Rc::new(RefCell::new(ManagedDockResource::new(
                self.session_id,
                self.callbacks,
                configuration,
                owner,
                window,
                cx,
            )));
            self.docks
                .borrow_mut()
                .insert(configuration.key.clone(), created.clone());
            // Layout changes report through the area's event emitter, which
            // outlives any single render: subscribe once per retained area.
            // The handler reads the current layout token at fire time, since
            // render-bound bindings may be re-registered across snapshots.
            let events = created.borrow().events();
            let subscription = cx.subscribe_in(
                &created.borrow().area(),
                window,
                move |_: &mut ManagedView,
                      _: &Entity<gpui_base::dock::DockArea>,
                      event: &gpui_base::dock::DockEvent,
                      _: &mut Window,
                      _: &mut Context<ManagedView>| {
                    if matches!(event, gpui_base::dock::DockEvent::LayoutChanged) {
                        crate::dock::emit_dock_event(
                            &events,
                            crate::semantic::EVENT_DOCK_LAYOUT_CHANGED,
                            &[],
                        );
                    }
                },
            );
            self.dock_subscriptions
                .borrow_mut()
                .insert(configuration.key.clone(), subscription);
            created
        };

        // Controller commands queue until the resource exists; the
        // declaration wins ties by applying first.
        let pending = self
            .pending
            .borrow_mut()
            .remove(&(RESOURCE_DOCK, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.borrow_mut().apply_command(&command, window, cx);
        }

        let area = resource.borrow().area();
        area
    }

    pub(crate) fn dispatch(&self, command: ResourceCommand) -> bool {
        let applied = match command.resource_kind {
            RESOURCE_SCROLL => self.apply_scroll_command(&command),
            RESOURCE_LIST => self.apply_list_command(&command),
            RESOURCE_INPUT | RESOURCE_SLIDER | RESOURCE_DOCK => false,
            _ => true,
        };
        if !applied {
            self.pending
                .borrow_mut()
                .entry((command.resource_kind, command.key.clone()))
                .or_default()
                .push(command);
        }
        applied
    }

    fn apply_pending(&self, resource_kind: u16, key: &ResourceKey) {
        let pending_key = (resource_kind, key.clone());
        let commands = self.pending.borrow_mut().remove(&pending_key);
        let Some(commands) = commands else {
            return;
        };
        for command in commands {
            let _ = match command.resource_kind {
                RESOURCE_SCROLL => self.apply_scroll_command(&command),
                RESOURCE_LIST => self.apply_list_command(&command),
                _ => true,
            };
        }
    }

    fn apply_scroll_command(&self, command: &ResourceCommand) -> bool {
        let Some(resource) = self.scrolls.borrow().get(&command.key).cloned() else {
            return false;
        };
        let handle = &resource.handle;
        resource.interaction.remaining.set(Point::default());
        match command.command {
            COMMAND_SCROLL_TO_OFFSET => {
                let x = f32::from_bits(command.a as u32);
                let y = f32::from_bits(command.b as u32);
                if x.is_finite() && y.is_finite() {
                    handle.set_offset(point(px(-x), px(-y)));
                }
            }
            COMMAND_SCROLL_TO_TOP => handle.set_offset(point(px(0.), px(0.))),
            COMMAND_SCROLL_TO_BOTTOM => handle.scroll_to_bottom(),
            _ => {}
        }
        true
    }

    fn apply_list_command(&self, command: &ResourceCommand) -> bool {
        let Some(resource) = self.lists.borrow().get(&command.key).cloned() else {
            // Structural commands only preserve measurements. If the resource does not exist yet,
            // there are no measurements to preserve and the next managed snapshot will construct
            // ListState directly at the authoritative item count. Keep only imperative scrolling.
            return matches!(command.command, COMMAND_LIST_SPLICE..=COMMAND_LIST_REFRESH);
        };
        resource.borrow_mut().apply_command(command);
        true
    }

    pub(crate) fn retain_snapshot(&self, snapshot: &ValidatedSnapshot) {
        let mut active = self.active_scratch.borrow_mut();
        active.clear();
        let mut extension_active = self.extension_active_scratch.borrow_mut();
        extension_active.clear();
        for node in &snapshot.nodes {
            let Some(metadata) = component_metadata(node.component) else {
                continue;
            };
            if !matches!(
                metadata.adapter,
                NativeAdapter::Scroll
                    | NativeAdapter::List
                    | NativeAdapter::Table
                    | NativeAdapter::Input
                    | NativeAdapter::Slider
                    | NativeAdapter::DockArea
                    | NativeAdapter::NativeExtension
            ) {
                continue;
            }
            let active_resource = match metadata.adapter {
                NativeAdapter::Scroll => {
                    resource_key(snapshot, node).map(|key| (RESOURCE_SCROLL, key))
                }
                NativeAdapter::List => resource_key(snapshot, node).map(|key| (RESOURCE_LIST, key)),
                NativeAdapter::Table => table_key(snapshot, node).map(|key| (RESOURCE_LIST, key)),
                NativeAdapter::Input => input_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_INPUT, configuration.key)),
                NativeAdapter::Slider => slider_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_SLIDER, configuration.key)),
                NativeAdapter::DockArea => dock_configuration(snapshot, node)
                    .map(|configuration| (RESOURCE_DOCK, configuration.key)),
                NativeAdapter::NativeExtension => {
                    if let (Some(owner), Some(declaration)) = (
                        last_u32(snapshot, node, OP_RESOURCE_OWNER),
                        extension_declaration(node),
                    ) {
                        if owner != 0 {
                            extension_active.insert(declaration.resource_key(owner));
                        }
                    }
                    None
                }
                _ => None,
            };
            if let Some(resource) = active_resource {
                active.insert(resource);
            }
        }
        self.scrolls
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_SCROLL, key.clone())));
        self.lists.borrow_mut().retain(|key, engine| {
            if active.contains(&(RESOURCE_LIST, key.clone())) {
                true
            } else {
                // A previous frame can still retain an Rc to the engine. Its artifact
                // lifetimes end at declaration removal, independently of that Rc.
                engine.borrow_mut().invalidate_all_batches();
                false
            }
        });
        self.tables
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_LIST, key.clone())));
        self.inputs
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_INPUT, key.clone())));
        self.sliders
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_SLIDER, key.clone())));
        self.docks
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_DOCK, key.clone())));
        self.dock_subscriptions
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_DOCK, key.clone())));
        self.extensions.retain(&extension_active);
        self.pending
            .borrow_mut()
            .retain(|(kind, key), _| active.contains(&(*kind, key.clone())));
    }
}

pub(crate) struct InputConfiguration {
    pub(crate) key: ResourceKey,
    pub(crate) initial_value: SharedString,
    pub(crate) placeholder: SharedString,
    pub(crate) disabled: bool,
    pub(crate) read_only: bool,
    pub(crate) password: bool,
    pub(crate) bindings: InputBindings,
}

#[derive(Clone, Copy)]
pub(crate) struct SliderBindings {
    pub(crate) changed: u64,
    pub(crate) released: u64,
}

impl Default for SliderBindings {
    fn default() -> Self {
        Self {
            changed: 0,
            released: 0,
        }
    }
}

pub(crate) struct SliderConfiguration {
    pub(crate) key: ResourceKey,
    pub(crate) min: f32,
    pub(crate) max: f32,
    pub(crate) step: f32,
    pub(crate) initial_value: Option<SliderValue>,
    pub(crate) axis: gpui::Axis,
    pub(crate) disabled: bool,
    pub(crate) logarithmic: bool,
    pub(crate) bindings: SliderBindings,
}

pub(crate) struct ListConfiguration {
    pub(crate) item_count: usize,
    pub(crate) renderer_token: u64,
    pub(crate) batch_size: usize,
    pub(crate) overdraw: Pixels,
    pub(crate) alignment: ListAlignment,
    pub(crate) estimated_item_height: Pixels,
    pub(crate) content_revision: Option<u64>,
    pub(crate) scrollbar: ScrollbarMetrics,
}

/// One declared table column. Widths are declarative intents (px or fraction); the native
/// header strip and every reconciled row cell use the same resolved intent so Taffy aligns them.
#[derive(Clone, Debug, PartialEq)]
pub(crate) struct TableColumnSpec {
    pub(crate) key: SharedString,
    pub(crate) header: SharedString,
    pub(crate) width: Pixels,
    pub(crate) width_is_fraction: bool,
    pub(crate) alignment: u32,
}

/// Column metadata for one table resource. A change between snapshots means row layout
/// changed, which invalidates every cached row batch.
#[derive(Clone, Debug, PartialEq)]
pub(crate) struct TableSpec {
    pub(crate) columns: Vec<TableColumnSpec>,
}

#[derive(Default)]
pub(crate) struct ScrollInteraction {
    pub(crate) remaining: Cell<Point<Pixels>>,
    pub(crate) animating: Cell<bool>,
}

pub(crate) struct ManagedScrollResource {
    pub(crate) handle: ScrollHandle,
    pub(crate) interaction: Rc<ScrollInteraction>,
}

impl ManagedScrollResource {
    fn new() -> Self {
        Self {
            handle: ScrollHandle::new(),
            interaction: Rc::new(ScrollInteraction::default()),
        }
    }
}

/// Coarse list-cache telemetry for benchmarks and diagnostics. Counters are monotonic and
/// cheap to read; the ABI does not expose them yet, so they are asserted from native tests.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) struct ListTelemetry {
    pub(crate) batch_loads: u64,
    pub(crate) batch_cache_hits: u64,
    pub(crate) batch_evictions: u64,
    pub(crate) batch_invalidations: u64,
    pub(crate) full_invalidations: u64,
    pub(crate) batch_crossings: u64,
    pub(crate) rendered_rows: u64,
}

/// A queued structural hint normalized for validation and replay. Every entry uses the item
/// indices produced by all entries before it, so a queued Refresh after a Splice refers to
/// post-splice indices.
#[derive(Clone, Copy, Debug)]
enum ListChange {
    ScrollTo(usize),
    Splice {
        start: usize,
        removed: usize,
        inserted: usize,
    },
    Reset(usize),
    Refresh {
        start: usize,
        count: usize,
    },
}

pub(crate) struct ManagedListResource {
    source_id: u64,
    session_id: u64,
    callbacks: ManagedCallbacks,
    pub(crate) state: ListState,
    pub(crate) interaction: Rc<ScrollInteraction>,
    pub(crate) item_count: usize,
    renderer_token: u64,
    batch_size: usize,
    overdraw: Pixels,
    alignment: ListAlignment,
    estimated_item_height: Pixels,
    hinted_viewport_width: Option<Pixels>,
    snapshot_revision: u64,
    content_revision: Option<u64>,
    batches: HashMap<u32, CachedBatch>,
    // Batches own decoded output; validation/grouping scratch is reused serially by the engine.
    scratch: SnapshotScratch,
    pending_commands: Vec<ResourceCommand>,
    use_clock: u64,
    frame_start: u64,
    last_batch: Option<u32>,
    telemetry: ListTelemetry,
}

impl ManagedListResource {
    fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
    ) -> Self {
        Self {
            source_id: next_source_id(),
            session_id,
            callbacks,
            state: ListState::new(
                configuration.item_count,
                configuration.alignment,
                configuration.overdraw,
            )
            .with_uniform_item_height(configuration.estimated_item_height),
            interaction: Rc::new(ScrollInteraction::default()),
            item_count: configuration.item_count,
            renderer_token: configuration.renderer_token,
            batch_size: configuration.batch_size,
            overdraw: configuration.overdraw,
            alignment: configuration.alignment,
            estimated_item_height: configuration.estimated_item_height,
            hinted_viewport_width: None,
            snapshot_revision,
            content_revision: configuration.content_revision,
            batches: HashMap::new(),
            scratch: SnapshotScratch::default(),
            pending_commands: Vec::new(),
            use_clock: 0,
            frame_start: 0,
            last_batch: None,
            telemetry: ListTelemetry::default(),
        }
    }

    fn configure(&mut self, configuration: &ListConfiguration, snapshot_revision: u64) {
        let revision_changed = self.snapshot_revision != snapshot_revision;
        let content_changed = match (self.content_revision, configuration.content_revision) {
            (Some(previous), Some(current)) => previous != current,
            (None, None) => revision_changed,
            _ => true,
        };
        let layout_changed = self.alignment != configuration.alignment
            || self.overdraw != configuration.overdraw
            || self.estimated_item_height != configuration.estimated_item_height;

        if layout_changed {
            // Rebuilding ListState already discards all measurements, so structural hints that
            // were waiting for this managed commit no longer provide any additional value.
            self.state = ListState::new(
                configuration.item_count,
                configuration.alignment,
                configuration.overdraw,
            )
            .with_uniform_item_height(configuration.estimated_item_height);
            self.item_count = configuration.item_count;
            self.alignment = configuration.alignment;
            self.overdraw = configuration.overdraw;
            self.estimated_item_height = configuration.estimated_item_height;
            self.hinted_viewport_width = None;
            self.pending_commands.clear();
            self.clear_batches();
        } else if revision_changed && !self.pending_commands.is_empty() {
            self.commit_pending_commands(configuration.item_count);
        } else if self.item_count != configuration.item_count {
            // A normal declarative count change without a ListController splice hint still has to
            // be correct; it simply cannot preserve the old per-item measurements precisely.
            self.state.reset_with_uniform_height(
                configuration.item_count,
                configuration.estimated_item_height,
            );
            self.item_count = configuration.item_count;
            self.clear_batches();
        }

        if self.renderer_token != configuration.renderer_token
            || self.batch_size != configuration.batch_size
        {
            self.renderer_token = configuration.renderer_token;
            self.batch_size = configuration.batch_size;
            self.clear_batches();
        }
        if revision_changed {
            self.snapshot_revision = snapshot_revision;
        }
        if content_changed {
            self.clear_batches();
        }
        self.content_revision = configuration.content_revision;
    }

    fn apply_command(&mut self, command: &ResourceCommand) {
        match command.command {
            COMMAND_LIST_SCROLL_TO_ITEM if self.pending_commands.is_empty() => {
                let index = command.a as usize;
                if index < self.item_count {
                    self.scroll_to_item(index);
                }
            }
            COMMAND_LIST_SCROLL_TO_ITEM..=COMMAND_LIST_REFRESH => {
                // Structural list commands are measurement-preservation hints. Applying them
                // immediately can race a frame that still materializes the previous managed
                // snapshot. Queue them until snapshot_revision advances, then commit the whole
                // command sequence against the new declarative item_count. Multi-range refresh
                // arrives as several queued Refresh commands and is batched here.
                self.pending_commands.push(command.clone());
            }
            _ => {}
        }
    }

    fn commit_pending_commands(&mut self, declared_item_count: usize) {
        let changes = self.parse_pending_changes();

        let mut expected_count = self.item_count;
        let mut valid = true;
        for change in &changes {
            match *change {
                ListChange::ScrollTo(_) => {}
                ListChange::Splice {
                    start,
                    removed,
                    inserted,
                } => {
                    if start > expected_count || removed > expected_count.saturating_sub(start) {
                        valid = false;
                        break;
                    }
                    expected_count = expected_count - removed + inserted;
                }
                ListChange::Reset(count) => expected_count = count,
                ListChange::Refresh { start, count } => {
                    if start > expected_count || count > expected_count.saturating_sub(start) {
                        valid = false;
                        break;
                    }
                }
            }
        }

        if !valid || expected_count != declared_item_count {
            // Managed state is authoritative. If the hints do not describe the snapshot that was
            // actually committed, fall back to a full native list reset rather than risking
            // measurement/index corruption.
            self.reset_native_state(declared_item_count);
            return;
        }

        let mut current_count = self.item_count;
        let mut inserted_unmeasured_items = false;
        for change in changes {
            match change {
                ListChange::ScrollTo(index) => {
                    if index < current_count {
                        self.scroll_to_item(index);
                    }
                }
                ListChange::Splice {
                    start,
                    removed,
                    inserted,
                } => {
                    // Items at or after `start` shift, so every cached batch that reaches into
                    // that suffix is stale. Batches entirely before `start` survive.
                    let batch = self.batch_size.max(1) as u32;
                    self.invalidate_batches_from((start as u32 / batch) * batch);
                    self.state.splice(start..start + removed, inserted);
                    inserted_unmeasured_items |= inserted > 0;
                    current_count = current_count - removed + inserted;
                }
                ListChange::Reset(count) => {
                    current_count = count;
                    self.state
                        .reset_with_uniform_height(count, self.estimated_item_height);
                    inserted_unmeasured_items = false;
                    self.clear_batches();
                }
                ListChange::Refresh { start, count } => {
                    self.invalidate_batches_intersecting(start, count);
                    self.state.remeasure_items(start..start + count);
                }
            }
        }
        if inserted_unmeasured_items {
            // GPUI's splice API does not accept a size hint for inserted items. Reapplying the
            // uniform hint fills those gaps while retaining each unaffected row's previous
            // measured height as its new hint, so the full scrollbar range remains available.
            self.state
                .clone()
                .with_uniform_item_height(self.estimated_item_height);
        }
        self.item_count = declared_item_count;
        self.pending_commands.clear();
    }

    fn parse_pending_changes(&self) -> Vec<ListChange> {
        let mut changes = Vec::new();
        for command in &self.pending_commands {
            match command.command {
                COMMAND_LIST_SCROLL_TO_ITEM => {
                    changes.push(ListChange::ScrollTo(command.a as usize))
                }
                COMMAND_LIST_SPLICE => changes.push(ListChange::Splice {
                    start: command.a as usize,
                    removed: (command.b >> 32) as usize,
                    inserted: command.b as u32 as usize,
                }),
                COMMAND_LIST_RESET => changes.push(ListChange::Reset(command.a as usize)),
                COMMAND_LIST_REFRESH => changes.push(ListChange::Refresh {
                    start: command.a as usize,
                    count: command.b as usize,
                }),
                _ => {}
            }
        }
        changes
    }

    fn reset_native_state(&mut self, declared_item_count: usize) {
        self.state
            .reset_with_uniform_height(declared_item_count, self.estimated_item_height);
        self.item_count = declared_item_count;
        self.pending_commands.clear();
        self.clear_batches();
    }

    fn clear_batches(&mut self) {
        if !self.batches.is_empty() {
            self.telemetry.full_invalidations += 1;
        }
        self.batches.clear();
        self.last_batch = None;
    }

    fn invalidate_batches_from(&mut self, first_batch: u32) {
        let before = self.batches.len();
        self.batches.retain(|&key, _| key < first_batch);
        self.telemetry.batch_invalidations += (before - self.batches.len()) as u64;
    }

    fn invalidate_batches_intersecting(&mut self, start: usize, count: usize) {
        if count == 0 {
            return;
        }
        let batch = self.batch_size.max(1) as u32;
        let first = (start as u32 / batch) * batch;
        let last = ((start + count - 1) as u32 / batch) * batch;
        let before = self.batches.len();
        self.batches.retain(|&key, _| key < first || key > last);
        self.telemetry.batch_invalidations += (before - self.batches.len()) as u64;
    }

    fn scroll_to_item(&mut self, index: usize) {
        self.interaction.remaining.set(Point::default());
        // Use GPUI's logical list offset directly. Unlike reveal-by-pixel operations, this does
        // not require preceding variable-height rows to be measured and keeps distant jumps
        // virtualized.
        self.state.scroll_to(ListOffset {
            item_ix: index,
            offset_in_item: px(0.),
        });
    }

    /// GPUI invalidates every cached height and size hint when the list width changes. The
    /// maintenance canvas runs after list prepaint, detects that width transition, and restores
    /// uniform hints before the sibling foundation scrollbar reads the native range.
    pub(crate) fn begin_frame(&mut self) {
        self.frame_start = self.use_clock;
    }

    pub(crate) fn maintain_height_hints(&mut self) {
        // All viewport and overdraw rows have been requested by list prepaint.
        self.trim_batches();
        let width = self.state.viewport_bounds().size.width;
        if width <= px(0.) || self.hinted_viewport_width == Some(width) {
            return;
        }

        self.state
            .clone()
            .with_uniform_item_height(self.estimated_item_height);
        self.hinted_viewport_width = Some(width);
    }

    pub(crate) fn render_item(
        &mut self,
        index: usize,
        resources: &ResourceStore,
        list_key: &ResourceKey,
    ) -> AnyElement {
        if index >= self.item_count {
            return div()
                .child("List item index is outside item_count.")
                .into_any_element();
        }

        let batch_size = self.batch_size.max(1);
        let start = (index / batch_size) * batch_size;
        let start_u32 = start as u32;
        self.use_clock = self.use_clock.wrapping_add(1).max(1);
        self.telemetry.rendered_rows += 1;
        if self.last_batch != Some(start_u32) {
            self.telemetry.batch_crossings += 1;
            self.last_batch = Some(start_u32);
        }
        if !self.batches.contains_key(&start_u32) {
            let loaded = {
                let _stage = crate::trace::span(crate::trace::Stage::ListBatchLoad);
                self.load_batch(start_u32)
            };
            if let Err(status) = loaded {
                return div()
                    .child(format!(
                        "Managed list range render failed with status {status}."
                    ))
                    .into_any_element();
            }
        } else {
            self.telemetry.batch_cache_hits += 1;
        }

        let batch = self.batches.get_mut(&start_u32).expect("batch was loaded");
        batch.last_used = self.use_clock;
        let local = index - start;
        let row_node = &batch.snapshot.nodes[batch.snapshot.root as usize];
        let roots = batch.snapshot.children(row_node);
        let Some(root) = roots.get(local).copied() else {
            return div()
                .child("Managed list batch returned the wrong item count.")
                .into_any_element();
        };
        let row_id = batch
            .snapshot
            .ops(&batch.snapshot.nodes[root as usize])
            .iter()
            .rev()
            .find(|op| op.code == OP_LIST_ITEM_ID)
            .map(|op| op.a)
            .filter(|id| *id != 0);

        crate::materializer::materialize_snapshot_node_detached(
            root,
            &batch.snapshot,
            self.session_id,
            self.callbacks,
            resources,
            list_key,
            index,
            row_id,
        )
    }

    fn load_batch(&mut self, start: u32) -> Result<(), i32> {
        let count = self
            .batch_size
            .min(self.item_count.saturating_sub(start as usize)) as u32;
        let mut batch = CachedBatch::new();
        // A batch is decoded once. Its snapshot owns the strings after this temporary
        // interner deduplicates the borrowed payloads within the batch.
        let mut retained_strings = RetainedStrings::default();
        let callback = self
            .callbacks
            .list_render_range
            .expect("callbacks were validated before application startup");
        let mut artifact_id = 0;
        let result = with_render_output(
            |arena, root| unsafe {
                callback(
                    self.session_id,
                    self.renderer_token,
                    self.source_id,
                    start,
                    count,
                    arena,
                    root,
                    &mut artifact_id,
                )
            },
            |arena, root| {
                batch
                    .snapshot
                    .decode_into(arena, root, &mut retained_strings, &mut self.scratch)
            },
        );
        // The output borrow has ended. A lease can now safely invoke managed release,
        // including when native decoding rejected a successfully published artifact.
        if artifact_id != 0 {
            batch.lease = Some(ArtifactLease {
                session_id: self.session_id,
                source_id: self.source_id,
                artifact_id,
                release: self
                    .callbacks
                    .release_artifact
                    .expect("callbacks were validated before application startup"),
                status: result.as_ref().err().copied().unwrap_or(0),
            });
        }
        result?;
        if batch.lease.is_none() {
            return Err(-64);
        }
        let root_node = &batch.snapshot.nodes[batch.snapshot.root as usize];
        if batch.snapshot.children(root_node).len() != count as usize {
            batch.lease.as_mut().unwrap().status = -63;
            return Err(-63);
        }
        batch.last_used = self.use_clock;
        let accept = self
            .callbacks
            .accept_artifact
            .expect("callbacks were validated before application startup");
        let status = unsafe { accept(self.session_id, self.source_id, artifact_id) };
        if status != 0 {
            return Err(status);
        }
        self.batches.insert(start, batch);
        self.telemetry.batch_loads += 1;
        Ok(())
    }

    fn trim_batches(&mut self) {
        const MAX_BATCHES: usize = 4;
        if self.batches.len() <= MAX_BATCHES {
            return;
        }
        // Select the four newest idle batches once. Repeatedly finding the oldest batch
        // rescans the entire map for each eviction after a large viewport contracts.
        let mut newest = [(0u32, 0u64); MAX_BATCHES];
        let mut idle_count = 0;
        for (&key, batch) in &self.batches {
            if batch.last_used > self.frame_start {
                continue;
            }
            if idle_count < MAX_BATCHES {
                newest[idle_count] = (key, batch.last_used);
            } else {
                let oldest = newest.iter_mut().min_by_key(|entry| entry.1).unwrap();
                if batch.last_used > oldest.1 {
                    *oldest = (key, batch.last_used);
                }
            }
            idle_count += 1;
        }
        if idle_count <= MAX_BATCHES {
            return;
        }
        let before = self.batches.len();
        self.batches.retain(|key, batch| {
            batch.last_used > self.frame_start || newest.iter().any(|entry| entry.0 == *key)
        });
        self.telemetry.batch_evictions += (before - self.batches.len()) as u64;
    }

    /// Reads the monotonic telemetry counters for diagnostics aggregation.
    pub(crate) fn telemetry(&self) -> ListTelemetry {
        self.telemetry
    }

    /// Discards every cached row batch. Used when a table's column table changes, which
    /// changes the layout of all rows at once.
    pub(crate) fn invalidate_all_batches(&mut self) {
        self.clear_batches();
    }

    fn invalidate_artifacts(&mut self, keys: &[crate::abi::NativeArtifactKey]) -> bool {
        let before = self.batches.len();
        self.batches.retain(|start, batch| {
            let remove = batch.lease.as_ref().is_some_and(|lease| {
                keys.iter()
                    .any(|key| key.source == self.source_id && key.artifact == lease.artifact_id)
            });
            if remove {
                let count = self
                    .batch_size
                    .min(self.item_count.saturating_sub(*start as usize));
                self.state
                    .remeasure_items(*start as usize..*start as usize + count);
            }
            !remove
        });
        let removed = before - self.batches.len();
        self.telemetry.batch_invalidations += removed as u64;
        if removed != 0 {
            self.last_batch = None;
        }
        removed != 0
    }
}

struct CachedBatch {
    lease: Option<ArtifactLease>,
    snapshot: ValidatedSnapshot,
    last_used: u64,
}

impl CachedBatch {
    fn new() -> Self {
        Self {
            lease: None,
            snapshot: ValidatedSnapshot::default(),
            last_used: 0,
        }
    }
}

fn next_source_id() -> u64 {
    static NEXT_SOURCE_ID: AtomicU64 = AtomicU64::new(1);
    NEXT_SOURCE_ID
        .try_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
        .expect("native datasource identity space exhausted")
}

struct ArtifactLease {
    session_id: u64,
    source_id: u64,
    artifact_id: u64,
    release: crate::abi::ManagedReleaseArtifactFn,
    status: i32,
}

impl Drop for ArtifactLease {
    fn drop(&mut self) {
        // Release runs only framework cleanup. The managed callback is idempotent and
        // admits cleanup after faults and while root acceptance is pending.
        unsafe {
            (self.release)(
                self.session_id,
                self.source_id,
                self.artifact_id,
                self.status,
            );
        }
    }
}

pub(crate) fn resource_key(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<ResourceKey> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    Some(ResourceKey::new(owner, node.data.clone()))
}

/// Packs one column's numeric record into an OP_TABLE_COLUMN payload: width f32 bits in the
/// low word, unit in bits 32..34, alignment in bits 34..36. Bits 36+ must be zero. Mirrored by
/// the managed `PackTableColumn`; kept for native tests of the record layout.
#[cfg(test)]
pub(crate) const fn pack_table_column(width_bits: u32, unit: u32, alignment: u32) -> u64 {
    width_bits as u64 | ((unit as u64) << 32) | ((alignment as u64) << 34)
}

fn unpack_table_column(record: u64) -> Option<(f32, bool, u32)> {
    if record >> 36 != 0 {
        return None;
    }
    let unit = ((record >> 32) & 0b11) as u32;
    let alignment = ((record >> 34) & 0b11) as u32;
    if unit > 1 || alignment > 2 {
        return None;
    }
    let width = f32::from_bits((record & 0xFFFF_FFFF) as u32);
    if !width.is_finite() || width <= 0.0 || (unit == 1 && width > 1.0) {
        return None;
    }
    Some((width, unit == 1, alignment))
}

/// Table node data is the row-engine key followed by one NUL-separated key/header string pair
/// per column; the numeric width/unit/alignment records arrive as one OP_TABLE_COLUMN op per
/// column in the same order. Malformed combinations return `None`; the materializer falls back
/// to an error element.
pub(crate) fn parse_table_spec(
    data: &str,
    records: &[u64],
) -> Option<(SharedString, Vec<TableColumnSpec>)> {
    let strings: Vec<&str> = data.split('\0').collect();
    let key = strings.first()?;
    if key.is_empty() || strings.len() - 1 != records.len() * 2 {
        return None;
    }
    let mut columns = Vec::with_capacity(records.len());
    for (index, record) in records.iter().enumerate() {
        let column_key = strings[1 + index * 2];
        let header = strings[2 + index * 2];
        if column_key.is_empty() {
            return None;
        }
        let (width, width_is_fraction, alignment) = unpack_table_column(*record)?;
        columns.push(TableColumnSpec {
            key: shared(column_key),
            header: shared(header),
            width: px(width),
            width_is_fraction,
            alignment,
        });
    }
    Some((shared(key), columns))
}

pub(crate) fn table_key(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<ResourceKey> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    let key = node.data.split('\0').next()?;
    if key.is_empty() {
        return None;
    }
    Some(ResourceKey::new(owner, shared(key)))
}

pub(crate) fn table_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<(ResourceKey, Rc<TableSpec>)> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 || node.data.is_empty() {
        return None;
    }
    let records: Vec<u64> = snapshot
        .ops(node)
        .iter()
        .filter(|op| op.code == OP_TABLE_COLUMN)
        .map(|op| op.a)
        .collect();
    let (key, columns) = parse_table_spec(&node.data, &records)?;
    Some((ResourceKey::new(owner, key), Rc::new(TableSpec { columns })))
}

pub(crate) fn input_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<InputConfiguration> {
    let owner = last_u32(snapshot, node, OP_RESOURCE_OWNER)?;
    if owner == 0 {
        return None;
    }

    let mut fields = node.data.split('\0');
    let key = fields.next()?;
    let initial_value = fields.next()?;
    let placeholder = fields.next()?;
    if key.is_empty() || fields.next().is_some() {
        return None;
    }

    Some(InputConfiguration {
        key: ResourceKey::new(owner, shared(key)),
        initial_value: shared(initial_value),
        placeholder: shared(placeholder),
        disabled: last_u32(snapshot, node, OP_INPUT_DISABLED).is_some_and(|value| value != 0),
        read_only: last_u32(snapshot, node, OP_INPUT_READ_ONLY).is_some_and(|value| value != 0),
        password: last_u32(snapshot, node, OP_INPUT_PASSWORD).is_some_and(|value| value != 0),
        bindings: InputBindings {
            changed: last_callback(snapshot, node, OP_INPUT_ON_CHANGED),
            submitted: last_callback(snapshot, node, OP_INPUT_ON_SUBMITTED),
            focus_changed: last_callback(snapshot, node, OP_INPUT_ON_FOCUS_CHANGED),
        },
    })
}

pub(crate) fn slider_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<SliderConfiguration> {
    let key = resource_key(snapshot, node)?;
    let min = last_op_bits_f32(snapshot, node, OP_SLIDER_MIN).unwrap_or(0.0);
    let max = last_op_bits_f32(snapshot, node, OP_SLIDER_MAX).unwrap_or(100.0);
    let step = last_op_bits_f32(snapshot, node, OP_SLIDER_STEP).unwrap_or(1.0);
    if !min.is_finite() || !max.is_finite() || min >= max || !step.is_finite() || step <= 0.0 {
        return None;
    }

    let axis = match last_u32(snapshot, node, OP_SLIDER_AXIS).unwrap_or(0) {
        0 => gpui::Axis::Horizontal,
        1 => gpui::Axis::Vertical,
        _ => return None,
    };
    let logarithmic = match last_u32(snapshot, node, OP_SLIDER_SCALE).unwrap_or(0) {
        0 => false,
        1 if min > 0.0 => true,
        _ => return None,
    };
    let single = last_op_bits_f32(snapshot, node, OP_SLIDER_VALUE);
    let range_start = last_op_bits_f32(snapshot, node, OP_SLIDER_RANGE_START);
    let range_end = last_op_bits_f32(snapshot, node, OP_SLIDER_RANGE_END);
    let initial_value = match (single, range_start, range_end) {
        (Some(value), None, None) => Some(SliderValue::Single(value)),
        (None, Some(start), Some(end)) if start <= end => Some(SliderValue::Range(start, end)),
        (None, None, None) => None,
        _ => return None,
    };

    Some(SliderConfiguration {
        key,
        min,
        max,
        step,
        initial_value,
        axis,
        disabled: last_u32(snapshot, node, OP_SLIDER_DISABLED).unwrap_or(0) != 0,
        logarithmic,
        bindings: SliderBindings {
            changed: last_callback(snapshot, node, OP_SLIDER_ON_CHANGED),
            released: last_callback(snapshot, node, OP_SLIDER_ON_RELEASED),
        },
    })
}

pub(crate) fn list_configuration(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
) -> Option<ListConfiguration> {
    let item_count = last_u32(snapshot, node, OP_LIST_ITEM_COUNT)? as usize;
    let renderer = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_RENDERER)?
        .a;
    let batch_size = last_u32(snapshot, node, OP_LIST_BATCH_SIZE)
        .unwrap_or(48)
        .clamp(1, 512) as usize;
    let overdraw = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_OVERDRAW_PX)
        .map_or(px(240.), |op| px(f32::from_bits(op.a as u32)));
    let alignment = match last_u32(snapshot, node, OP_LIST_ALIGNMENT).unwrap_or(0) {
        1 => ListAlignment::Bottom,
        _ => ListAlignment::Top,
    };
    let estimated_item_height = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_ESTIMATED_ITEM_HEIGHT_PX)
        .map_or(px(40.), |op| px(f32::from_bits(op.a as u32)));
    let content_revision = snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == OP_LIST_CONTENT_REVISION)
        .map(|op| op.a);
    let scrollbar_gutter = last_u32(snapshot, node, OP_SCROLLBAR_GUTTER).is_some_and(|v| v != 0);
    let scrollbar_width = last_op_bits_f32(snapshot, node, OP_SCROLLBAR_WIDTH)
        .unwrap_or(DEFAULT_SCROLLBAR_WIDTH.into());
    let scrollbar = ScrollbarMetrics::new(px(scrollbar_width), scrollbar_gutter);
    Some(ListConfiguration {
        item_count,
        renderer_token: renderer,
        batch_size,
        overdraw,
        alignment,
        estimated_item_height,
        content_revision,
        scrollbar,
    })
}

/// Reads an f32-valued op as bits. Returns `None` when the op is absent.
fn last_op_bits_f32(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
    code: u16,
) -> Option<f32> {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map(|op| f32::from_bits(op.a as u32))
}

fn last_u32(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
    code: u16,
) -> Option<u32> {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map(|op| op.a as u32)
}

fn last_callback(
    snapshot: &ValidatedSnapshot,
    node: &crate::snapshot::SnapshotNode,
    code: u16,
) -> u64 {
    snapshot
        .ops(node)
        .iter()
        .rev()
        .find(|op| op.code == code)
        .map_or(0, |op| op.a)
}

fn shared(value: &str) -> SharedString {
    SharedString::new(Arc::<str>::from(value))
}

#[cfg(test)]
mod tests {
    mod measurements;
    use super::*;

    #[derive(Default)]
    struct ArtifactCapture {
        nodes: Vec<crate::abi::NodeRecord>,
        children: Vec<crate::abi::ChildRecord>,
        ops: Vec<crate::abi::OpRecord>,
        clickable: bool,
        clicked: Vec<u64>,
        click_status: i32,
        next_id: u64,
        requests: Vec<(u64, u64)>,
        accepts: Vec<(u64, u64)>,
        releases: Vec<(u64, u64, i32)>,
        failure_mode: u8,
    }

    thread_local! {
        static ARTIFACTS: RefCell<ArtifactCapture> = RefCell::default();
    }

    unsafe extern "C" fn publish_test_range(
        _: u64,
        _: u64,
        source: u64,
        _: u32,
        count: u32,
        arena: *mut crate::abi::RenderArena,
        root: *mut u32,
        artifact: *mut u64,
    ) -> i32 {
        use crate::{
            abi::{ChildRecord, NodeRecord},
            semantic::{COMPONENT_DIV, COMPONENT_TEXT},
        };
        ARTIFACTS.with(|capture| {
            let mut capture = capture.borrow_mut();
            if capture.failure_mode == 3 {
                return -106;
            }
            let rows = if capture.failure_mode == 2 { 0 } else { count };
            capture.nodes.clear();
            let component = if capture.failure_mode == 1 {
                u16::MAX
            } else {
                COMPONENT_DIV
            };
            capture.nodes.push(NodeRecord {
                component,
                ..Default::default()
            });
            capture.children.clear();
            for index in 0..rows {
                capture.nodes.push(NodeRecord {
                    component: COMPONENT_TEXT,
                    ..Default::default()
                });
                capture.children.push(ChildRecord {
                    parent: 0,
                    child: index + 1,
                });
            }
            capture.next_id += 1;
            let id = capture.next_id;
            capture.ops.clear();
            if capture.clickable {
                use crate::semantic::{OP_HEIGHT_PX, OP_ON_CLICK, OP_WIDTH_PX, ValueKind};
                for index in 0..rows {
                    capture.nodes[index as usize + 1].component = crate::semantic::COMPONENT_BUTTON;
                    capture.nodes[index as usize + 1].data_length = 3;
                    capture.ops.extend([
                        crate::abi::OpRecord {
                            node: index + 1,
                            code: OP_WIDTH_PX,
                            value_kind: ValueKind::F32 as u16,
                            a: 200f32.to_bits() as u64,
                            b: 0,
                        },
                        crate::abi::OpRecord {
                            node: index + 1,
                            code: OP_ON_CLICK,
                            value_kind: ValueKind::Callback as u16,
                            a: id,
                            b: 0,
                        },
                        crate::abi::OpRecord {
                            node: index + 1,
                            code: OP_HEIGHT_PX,
                            value_kind: ValueKind::F32 as u16,
                            a: 30f32.to_bits() as u64,
                            b: 0,
                        },
                    ]);
                }
            }
            capture.requests.push((source, id));
            unsafe {
                if capture.clickable {
                    (*arena).utf8 = b"row".as_ptr().cast_mut();
                    (*arena).utf8_length = 3;
                    (*arena).utf8_capacity = 3;
                }
                (*arena).ops = capture.ops.as_mut_ptr();
                (*arena).op_length = capture.ops.len() as i32;
                (*arena).op_capacity = capture.ops.capacity() as i32;
                (*arena).nodes = capture.nodes.as_mut_ptr();
                (*arena).node_length = capture.nodes.len() as i32;
                (*arena).node_capacity = capture.nodes.capacity() as i32;
                (*arena).children = capture.children.as_mut_ptr();
                (*arena).child_length = capture.children.len() as i32;
                (*arena).child_capacity = capture.children.capacity() as i32;
                (*arena).generation = 1;
                *root = 0;
                *artifact = id;
            }
            0
        })
    }

    unsafe extern "C" fn release_test_artifact(
        _: u64,
        source: u64,
        artifact: u64,
        status: i32,
    ) -> i32 {
        ARTIFACTS.with(|capture| {
            capture
                .borrow_mut()
                .releases
                .push((source, artifact, status))
        });
        0
    }

    fn artifact_callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            click: Some(click_test_row),
            accept_artifact: Some(accept_test_artifact),
            list_render_range: Some(publish_test_range),
            release_artifact: Some(release_test_artifact),
            ..callbacks()
        }
    }

    unsafe extern "C" fn click_test_row(
        _: u64,
        token: u64,
        _: u64,
        _: *const crate::abi::NativeClickEvent,
    ) -> i32 {
        ARTIFACTS.with(|capture| {
            let mut capture = capture.borrow_mut();
            if !capture
                .releases
                .iter()
                .any(|(_, artifact, _)| *artifact == token)
            {
                capture.clicked.push(token);
            }
            capture.click_status
        })
    }

    struct VisibleRows {
        store: Rc<ResourceStore>,
        resource: Rc<RefCell<ManagedListResource>>,
    }

    impl gpui::Render for VisibleRows {
        fn render(&mut self, _: &mut Window, _: &mut Context<Self>) -> impl IntoElement {
            use gpui::Styled;
            self.resource.borrow_mut().begin_frame();
            let key = ResourceKey::new(1, "rows".into());
            let state = self.resource.borrow().state.clone();
            let rows_resource = self.resource.clone();
            let store = self.store.clone();
            let rows = div().flex().flex_col().w(px(200.)).h(px(240.)).child(
                gpui::list(state, move |index, _, _| {
                    rows_resource.borrow_mut().render_item(index, &store, &key)
                })
                .size_full(),
            );
            let resource = self.resource.clone();
            rows.child(gpui::canvas(
                move |_, _, _| resource.borrow_mut().trim_batches(),
                |_, _, _, _| {},
            ))
        }
    }

    #[gpui::test]
    fn displayed_rows_keep_artifacts_after_prepaint(cx: &mut gpui::TestAppContext) {
        cx.update(gpui_base::init);
        ARTIFACTS.with(|capture| {
            *capture.borrow_mut() = ArtifactCapture {
                clickable: true,
                ..Default::default()
            }
        });
        let store = Rc::new(ResourceStore::new(1, artifact_callbacks(), theme()));
        let mut config = configuration(Some(1));
        config.item_count = 8;
        config.batch_size = 1;
        config.overdraw = px(0.);
        config.estimated_item_height = px(30.);
        let resource = Rc::new(RefCell::new(ManagedListResource::new(
            1,
            artifact_callbacks(),
            &config,
            1,
        )));
        let (_, cx) = cx.add_window_view(|_, _| VisibleRows {
            store,
            resource: resource.clone(),
        });
        cx.update(|window, cx| {
            window.draw(cx).clear(cx);
        });
        ARTIFACTS.with(|capture| {
            assert_eq!(
                resource.borrow().batches.len(),
                8,
                "requests={:?}, releases={:?}",
                capture.borrow().requests,
                capture.borrow().releases
            )
        });
        ARTIFACTS.with(|capture| assert!(capture.borrow().releases.is_empty()));
        cx.simulate_click(point(px(10.), px(15.)), Default::default());
        cx.simulate_click(point(px(10.), px(225.)), Default::default());
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().clicked, [1, 8]));
        // Once no frame needs these batches, only four idle batches remain cached.
        resource.borrow_mut().begin_frame();
        resource.borrow_mut().trim_batches();
        assert_eq!(resource.borrow().batches.len(), 4);
        resource.borrow_mut().clear_batches();
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 8));
    }

    unsafe extern "C" fn accept_test_artifact(_: u64, source: u64, artifact: u64) -> i32 {
        ARTIFACTS.with(|capture| {
            let mut capture = capture.borrow_mut();
            capture.accepts.push((source, artifact));
            // Acceptance can reuse the managed output: native decoding must already be done.
            capture.nodes.clear();
            capture.children.clear();
            if capture.failure_mode == 4 { -109 } else { 0 }
        })
    }

    #[test]
    fn artifact_acceptance_follows_decode_and_rejection_releases_the_batch() {
        for mode in 0..=4 {
            ARTIFACTS.with(|capture| {
                *capture.borrow_mut() = ArtifactCapture {
                    failure_mode: mode,
                    ..Default::default()
                }
            });
            let mut resource = artifact_resource();
            assert_eq!(resource.load_batch(0).is_ok(), mode == 0);
            if mode == 0 {
                assert_eq!(resource.batches[&0].snapshot.nodes.len(), 2);
            }
            ARTIFACTS.with(|capture| {
                let capture = capture.borrow();
                assert_eq!(capture.accepts.len(), usize::from(mode == 0 || mode == 4));
                if mode == 4 {
                    assert_eq!(capture.releases.len(), 1);
                }
            });
        }
    }

    #[test]
    fn cached_snapshots_survive_reused_scratch_growth_and_failed_batches() {
        ARTIFACTS.with(|capture| {
            *capture.borrow_mut() = ArtifactCapture {
                clickable: true,
                ..Default::default()
            };
        });
        let mut config = configuration(Some(1));
        config.item_count = 2_000;
        let mut resource = ManagedListResource::new(1, artifact_callbacks(), &config, 1);
        resource.load_batch(0).unwrap();
        let first_id = resource.batches[&0].lease.as_ref().unwrap().artifact_id;

        ARTIFACTS.with(|capture| capture.borrow_mut().clickable = false);
        resource.batch_size = 1;
        resource.load_batch(48).unwrap();
        ARTIFACTS.with(|capture| capture.borrow_mut().failure_mode = 2);
        assert_eq!(resource.load_batch(49), Err(-63));
        ARTIFACTS.with(|capture| {
            let mut capture = capture.borrow_mut();
            capture.failure_mode = 0;
            capture.clickable = true;
        });
        resource.batch_size = 512;
        resource.load_batch(512).unwrap();

        let first = &resource.batches[&0].snapshot;
        assert_eq!(first.children(&first.nodes[0]).len(), 48);
        assert!(first.nodes[1..].iter().all(|node| {
            node.component == crate::semantic::COMPONENT_BUTTON
                && node.data.as_ref() == "row"
                && first
                    .ops(node)
                    .iter()
                    .any(|op| op.code == crate::semantic::OP_ON_CLICK && op.a == first_id)
        }));
        let second = &resource.batches[&48].snapshot;
        assert_eq!(second.nodes.len(), 2);
        assert_eq!(second.nodes[1].component, crate::semantic::COMPONENT_TEXT);
        assert!(second.ops(&second.nodes[1]).is_empty());
        assert_eq!(resource.batches[&512].snapshot.nodes.len(), 513);
        assert!(
            resource.invalidate_artifacts(&[crate::abi::NativeArtifactKey {
                source: resource.source_id,
                artifact: first_id,
            }])
        );
        assert_eq!(batch_keys(&resource), vec![48, 512]);
        ARTIFACTS.with(|capture| {
            assert_eq!(
                capture.borrow().releases,
                vec![
                    (resource.source_id, 3, -63),
                    (resource.source_id, first_id, 0)
                ]
            );
        });
    }

    #[test]
    fn reactive_invalidation_evicts_only_matching_source_and_artifact() {
        ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
        let mut resource = artifact_resource();
        resource.load_batch(0).unwrap();
        resource.load_batch(1).unwrap();
        let key = crate::abi::NativeArtifactKey {
            source: resource.source_id,
            artifact: 1,
        };
        assert!(
            !resource.invalidate_artifacts(&[crate::abi::NativeArtifactKey {
                source: key.source + 1,
                ..key
            }])
        );
        assert!(resource.invalidate_artifacts(&[key]));
        assert!(resource.batches.contains_key(&1));
        resource.load_batch(0).unwrap();
        assert!(!resource.invalidate_artifacts(&[key]));
        assert_eq!(resource.batches.len(), 2);
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases, vec![(key.source, 1, 0)]));
    }

    fn artifact_resource() -> ManagedListResource {
        let mut config = configuration(Some(1));
        config.batch_size = 1;
        ManagedListResource::new(1, artifact_callbacks(), &config, 1)
    }

    #[test]
    fn native_cache_eviction_and_invalidation_release_only_their_artifacts() {
        ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
        let mut resource = artifact_resource();
        for start in 0..5 {
            resource.use_clock += 1;
            resource.load_batch(start).unwrap();
            resource.begin_frame();
            resource.trim_batches();
        }
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            assert_eq!(capture.requests.len(), 5);
            assert_eq!(capture.releases, vec![(resource.source_id, 1, 0)]);
        });
        resource.invalidate_batches_intersecting(2, 1);
        ARTIFACTS.with(|capture| {
            assert_eq!(
                capture.borrow().releases,
                vec![(resource.source_id, 1, 0), (resource.source_id, 3, 0)]
            );
        });
        resource.clear_batches();
        drop(resource);
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            assert_eq!(capture.releases.len(), 5);
            assert_eq!(
                capture
                    .releases
                    .iter()
                    .map(|(_, id, _)| *id)
                    .collect::<HashSet<_>>()
                    .len(),
                5
            );
        });
    }

    #[test]
    fn sources_using_one_renderer_release_independently() {
        ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
        let mut first = artifact_resource();
        let mut second = artifact_resource();
        assert_eq!(first.renderer_token, second.renderer_token);
        assert_ne!(first.source_id, second.source_id);
        first.load_batch(0).unwrap();
        second.load_batch(0).unwrap();
        let first_source = first.source_id;
        drop(first);
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases, vec![(first_source, 1, 0)]));
        assert_eq!(second.batches.len(), 1);
        drop(second);
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 2));
    }

    #[test]
    fn declaration_removal_releases_artifacts_even_when_a_frame_retains_the_engine() {
        ARTIFACTS.with(|capture| *capture.borrow_mut() = ArtifactCapture::default());
        let store = ResourceStore::new(1, artifact_callbacks(), theme());
        let key = ResourceKey::new(1, "list".into());
        let engine = store.list_resource(&key, &configuration(Some(1)), 1);
        engine.borrow_mut().load_batch(0).unwrap();
        store.retain_snapshot(&ValidatedSnapshot::default());
        assert!(engine.borrow().batches.is_empty());
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 1));
        drop(engine);
        ARTIFACTS.with(|capture| assert_eq!(capture.borrow().releases.len(), 1));
    }

    #[test]
    fn failed_native_decode_releases_publication_after_the_borrow_ends() {
        for mode in 1..=3 {
            ARTIFACTS.with(|capture| {
                *capture.borrow_mut() = ArtifactCapture {
                    failure_mode: mode,
                    ..Default::default()
                }
            });
            let mut resource = artifact_resource();
            assert!(resource.load_batch(0).is_err());
            assert!(resource.batches.is_empty());
            ARTIFACTS.with(|capture| {
                let capture = capture.borrow();
                if mode == 3 {
                    assert!(capture.releases.is_empty());
                } else {
                    assert_eq!(capture.releases.len(), 1);
                    assert_ne!(capture.releases[0].2, 0);
                }
            });
        }
    }

    fn theme() -> SharedTheme {
        Rc::new(RefCell::new(crate::theme::NativeTheme::default()))
    }

    fn callbacks() -> ManagedCallbacks {
        ManagedCallbacks {
            struct_size: 0,
            render: None,
            render_completed: None,
            release_artifact: None,
            accept_artifact: None,
            click: None,
            list_render_range: None,
            dynamic_frame: None,
            control_event: None,
            application_started: None,
            window_closed: None,
            menu_action: None,
        }
    }

    fn configuration(content_revision: Option<u64>) -> ListConfiguration {
        ListConfiguration {
            item_count: 100,
            renderer_token: 1,
            batch_size: 48,
            overdraw: px(240.),
            alignment: ListAlignment::Top,
            estimated_item_height: px(40.),
            content_revision,
            scrollbar: ScrollbarMetrics::new(DEFAULT_SCROLLBAR_WIDTH, false),
        }
    }

    #[test]
    fn estimated_height_hints_cover_the_full_unmeasured_range() {
        let mut config = configuration(Some(1));
        config.item_count = 20_000;
        let resource = ManagedListResource::new(1, callbacks(), &config, 1);

        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(800_000.));
    }

    #[test]
    fn estimated_height_hints_drive_native_pixel_offset_mapping() {
        let resource = ManagedListResource::new(1, callbacks(), &configuration(Some(1)), 1);

        resource.state.scroll_to(ListOffset {
            item_ix: 50,
            offset_in_item: px(20.),
        });

        assert_eq!(
            resource.state.scroll_px_offset_for_scrollbar().y,
            px(-2_020.)
        );
    }

    #[test]
    fn structural_insertions_receive_estimated_height_hints() {
        let mut resource = ManagedListResource::new(1, callbacks(), &configuration(Some(1)), 1);
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 50, 5, ""));
        resource.commit_pending_commands(105);

        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(4_200.));
    }

    #[test]
    fn offscreen_refresh_preserves_full_scrollbar_range_after_configuration() {
        let mut config = configuration(Some(1));
        config.item_count = 20_000;
        let mut resource = ManagedListResource::new(1, callbacks(), &config, 1);
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 10_000, 1_000, ""));
        resource.configure(&config, 2);
        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(800_000.));
        resource.scroll_to_item(19_000);
        assert_eq!(
            resource.state.scroll_px_offset_for_scrollbar().y,
            px(-760_000.)
        );
    }

    #[test]
    fn mixed_changes_wait_for_acceptance_and_reconcile_with_content_revision() {
        for changed_revision in [false, true] {
            let mut config = configuration(Some(1));
            config.item_count = 200;
            let mut resource = ManagedListResource::new(1, callbacks(), &config, 1);
            for start in [0, 48, 96, 144] {
                resource.batches.insert(start, CachedBatch::new());
            }
            resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 2, ""));
            resource.apply_command(&command(COMMAND_LIST_SPLICE, 150, (3_u64 << 32) | 8, ""));
            resource.apply_command(&command(COMMAND_LIST_REFRESH, 201, 4, ""));
            resource.configure(&config, 1);
            assert_eq!(resource.item_count, 200);
            assert_eq!(batch_keys(&resource), vec![0, 48, 96, 144]);
            config.item_count = 205;
            if changed_revision {
                config.content_revision = Some(2);
            }
            resource.configure(&config, 2);
            assert_eq!(resource.item_count, 205);
            assert_eq!(resource.state.max_offset_for_scrollbar().y, px(8_200.));
            assert!(resource.pending_commands.is_empty());
            assert_eq!(
                batch_keys(&resource),
                if changed_revision {
                    vec![]
                } else {
                    vec![0, 96]
                }
            );
        }
    }

    #[test]
    fn invalid_mixed_hints_reset_to_the_accepted_shape() {
        let mut config = configuration(Some(1));
        let mut resource = ManagedListResource::new(1, callbacks(), &config, 1);
        resource.batches.insert(0, CachedBatch::new());
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 90, (11_u64 << 32) | 1, ""));
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 0, 1, ""));
        config.item_count = 90;
        resource.configure(&config, 2);
        assert_eq!(resource.item_count, 90);
        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(3_600.));
        assert!(resource.batches.is_empty());
        assert!(resource.pending_commands.is_empty());
    }

    #[test]
    fn trimming_keeps_every_displayed_batch_and_the_four_newest_idle_batches() {
        for tied in [false, true] {
            let mut resource = resource_with_batches(&[]);
            resource.frame_start = 100;
            for key in 0..40 {
                let mut batch = CachedBatch::new();
                batch.last_used = if key >= 32 {
                    101
                } else if tied {
                    1
                } else {
                    key as u64
                };
                resource.batches.insert(key, batch);
            }
            resource.trim_batches();
            assert_eq!(resource.batches.len(), 12);
            assert!((32..40).all(|key| resource.batches.contains_key(&key)));
            if !tied {
                assert_eq!(batch_keys(&resource), (28..40).collect::<Vec<_>>());
            }
            assert_eq!(resource.telemetry.batch_evictions, 28);
            resource.trim_batches();
            assert_eq!(resource.telemetry.batch_evictions, 28);
        }
    }

    #[test]
    fn changing_estimated_height_rebuilds_native_height_hints() {
        let mut resource = ManagedListResource::new(1, callbacks(), &configuration(Some(1)), 1);
        let mut changed = configuration(Some(1));
        changed.estimated_item_height = px(52.);

        resource.configure(&changed, 2);

        assert_eq!(resource.state.max_offset_for_scrollbar().y, px(5_200.));
    }

    #[test]
    fn explicit_content_revision_preserves_batches_across_unrelated_snapshots() {
        let revision = (1_u64 << 40) | 7;
        let mut resource =
            ManagedListResource::new(1, callbacks(), &configuration(Some(revision)), 1);
        resource.batches.insert(0, CachedBatch::new());

        resource.configure(&configuration(Some(revision)), 2);
        assert_eq!(resource.batches.len(), 1);

        resource.configure(&configuration(Some(revision + 1)), 3);
        assert!(resource.batches.is_empty());
    }

    #[test]
    fn implicit_content_revision_remains_conservative() {
        let mut resource = ManagedListResource::new(1, callbacks(), &configuration(None), 1);
        resource.batches.insert(0, CachedBatch::new());

        resource.configure(&configuration(None), 2);
        assert!(resource.batches.is_empty());
    }

    fn command(command: u16, a: u64, b: u64, data: &str) -> ResourceCommand {
        ResourceCommand {
            key: ResourceKey::new(7, shared("list")),
            resource_kind: RESOURCE_LIST,
            command,
            a,
            b,
            data: shared(data),
        }
    }

    fn resource_with_batches(keys: &[u32]) -> ManagedListResource {
        let mut resource = ManagedListResource::new(1, callbacks(), &configuration(None), 1);
        for &key in keys {
            resource.batches.insert(key, CachedBatch::new());
        }
        resource
    }

    fn batch_keys(resource: &ManagedListResource) -> Vec<u32> {
        let mut keys: Vec<_> = resource.batches.keys().copied().collect();
        keys.sort_unstable();
        keys
    }

    #[test]
    fn refresh_invalidates_only_intersecting_batches() {
        let mut resource = resource_with_batches(&[0, 48, 96, 144]);
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
        resource.commit_pending_commands(100);

        assert_eq!(batch_keys(&resource), vec![0, 96, 144]);
        let telemetry = resource.telemetry();
        assert_eq!(telemetry.batch_invalidations, 1);
        assert_eq!(telemetry.full_invalidations, 0);
    }

    #[test]
    fn refresh_spanning_multiple_batches_invalidates_each_one() {
        let mut resource = resource_with_batches(&[0, 48, 96, 144]);
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 40, 20, ""));
        resource.commit_pending_commands(100);

        // [40, 60) touches batch 0 ([0, 48)) and batch 48 ([48, 96)).
        assert_eq!(batch_keys(&resource), vec![96, 144]);
        assert_eq!(resource.telemetry().batch_invalidations, 2);
    }

    #[test]
    fn splice_preserves_batches_entirely_before_start() {
        let mut resource = resource_with_batches(&[0, 48, 96, 144]);
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 90, (10_u64 << 32) | 10, ""));
        resource.commit_pending_commands(100);

        // The suffix at or after batch 48 contains index 90 and shifts; batch 0 is untouched.
        assert_eq!(batch_keys(&resource), vec![0]);
        assert_eq!(resource.telemetry().batch_invalidations, 3);
    }

    #[test]
    fn splice_at_head_invalidates_every_cached_batch() {
        let mut resource = resource_with_batches(&[0, 48, 96]);
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 0, (5_u64 << 32) | 2, ""));
        resource.commit_pending_commands(97);

        assert!(resource.batches.is_empty());
        assert_eq!(resource.telemetry().batch_invalidations, 3);
    }

    #[test]
    fn splice_suffix_start_snaps_to_batch_boundary() {
        let mut resource = resource_with_batches(&[0, 48, 96]);
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 60, (1_u64 << 32) | 1, ""));
        resource.commit_pending_commands(100);

        // Batch 48 covers [48, 96), which contains index 60, so it is stale; batch 0 survives.
        assert_eq!(batch_keys(&resource), vec![0]);
        assert_eq!(resource.telemetry().batch_invalidations, 2);
    }

    #[test]
    fn reset_command_clears_every_batch() {
        let mut resource = resource_with_batches(&[0, 48, 96]);
        resource.apply_command(&command(12, 50, 0, ""));
        resource.commit_pending_commands(50);

        assert!(resource.batches.is_empty());
        let telemetry = resource.telemetry();
        assert_eq!(telemetry.full_invalidations, 1);
        assert_eq!(telemetry.batch_invalidations, 0);
    }

    #[test]
    fn multi_range_refresh_invalidates_exactly_the_intersecting_batches() {
        let mut resource = resource_with_batches(&[0, 48, 96, 144]);
        resource.item_count = 200;
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 150, 3, ""));
        resource.commit_pending_commands(200);

        assert_eq!(batch_keys(&resource), vec![0, 96]);
        assert_eq!(resource.telemetry().batch_invalidations, 2);
    }

    #[test]
    fn multi_range_refresh_after_splice_uses_post_splice_indices() {
        let mut resource = resource_with_batches(&[0, 48, 96, 144]);
        resource.apply_command(&command(COMMAND_LIST_SPLICE, 40, (2_u64 << 32) | 2, ""));
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 38, 1, ""));
        resource.commit_pending_commands(100);

        // The splice at 40 has batch boundary 0, so every cached batch is already stale; the
        // following refresh finds nothing left to invalidate and must not underflow or panic.
        assert!(resource.batches.is_empty());
        assert_eq!(resource.telemetry().batch_invalidations, 4);
    }

    #[test]
    fn hints_disagreeing_with_declared_count_fall_back_to_full_reset() {
        let mut resource = resource_with_batches(&[0, 48, 96]);
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 10, 5, ""));
        resource.commit_pending_commands(101);

        assert!(resource.batches.is_empty());
        assert_eq!(resource.item_count, 101);
        assert_eq!(resource.telemetry().full_invalidations, 1);
    }

    #[test]
    fn refresh_zero_count_is_a_no_op() {
        let mut resource = resource_with_batches(&[0, 48]);
        resource.apply_command(&command(13, 10, 0, ""));
        resource.commit_pending_commands(100);

        assert_eq!(batch_keys(&resource), vec![0, 48]);
        let telemetry = resource.telemetry();
        assert_eq!(telemetry.batch_invalidations, 0);
        assert_eq!(telemetry.full_invalidations, 0);
    }

    #[test]
    fn refresh_zero_count_after_real_range_preserves_earlier_invalidation() {
        let mut resource = resource_with_batches(&[0, 48, 96]);
        resource.apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
        resource.apply_command(&command(13, 10, 0, ""));
        resource.commit_pending_commands(100);

        assert_eq!(batch_keys(&resource), vec![0, 96]);
        assert_eq!(resource.telemetry().batch_invalidations, 1);
    }

    #[test]
    fn parse_table_spec_reads_string_pairs_and_packed_records() {
        let data = "rows\0name\0Name\0size\0Size";
        let records = [
            pack_table_column(120.0f32.to_bits(), 0, 0),
            pack_table_column(0.3f32.to_bits(), 1, 2),
        ];
        let (key, columns) = parse_table_spec(data, &records).expect("valid table data");
        assert_eq!(key, shared("rows"));
        assert_eq!(columns.len(), 2);
        assert_eq!(columns[0].key, shared("name"));
        assert_eq!(columns[0].header, shared("Name"));
        assert_eq!(columns[0].width, px(120.));
        assert!(!columns[0].width_is_fraction);
        assert_eq!(columns[0].alignment, 0);
        assert_eq!(columns[1].width, px(0.3));
        assert!(columns[1].width_is_fraction);
        assert_eq!(columns[1].alignment, 2);

        // A key-only blob is a headerless table with no columns.
        let (key, columns) = parse_table_spec("rows", &[]).expect("valid key-only table data");
        assert_eq!(key, shared("rows"));
        assert!(columns.is_empty());
    }

    #[test]
    fn parse_table_spec_rejects_malformed_specs() {
        let good = pack_table_column(120.0f32.to_bits(), 0, 0);
        let bad_unit = pack_table_column(120.0f32.to_bits(), 2, 0);
        let bad_alignment = pack_table_column(120.0f32.to_bits(), 0, 3);
        let bad_width = pack_table_column((-4.0f32).to_bits(), 0, 0);
        let bad_fraction = pack_table_column(1.4f32.to_bits(), 1, 0);
        let noncanonical = pack_table_column(120.0f32.to_bits(), 0, 0) | (1 << 36);

        // String count must be exactly two per record.
        assert_eq!(parse_table_spec("", &[good]), None);
        assert_eq!(parse_table_spec("\0name\0Name", &[good]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name\0extra", &[good]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name\0x", &[good, good]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name\0x\0y\0z", &[good]), None);
        // An empty column key is rejected.
        assert_eq!(parse_table_spec("rows\0\0Name", &[good]), None);
        // Numeric records must unpack cleanly.
        assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_unit]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_alignment]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_width]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name", &[bad_fraction]), None);
        assert_eq!(parse_table_spec("rows\0name\0Name", &[noncanonical]), None);
    }

    #[test]
    fn unpack_table_column_round_trips_packed_records() {
        for (width, unit, alignment) in [
            (8.0f32, 0u32, 0u32),
            (120.0, 0, 2),
            (0.5, 1, 1),
            (1.0, 1, 2),
        ] {
            let record = pack_table_column(width.to_bits(), unit, alignment);
            let unpacked = unpack_table_column(record).expect("record round-trips");
            assert_eq!(unpacked.0, width);
            assert_eq!(unpacked.1, unit == 1);
            assert_eq!(unpacked.2, alignment);
        }
    }

    #[test]
    fn binding_a_changed_table_spec_invalidates_row_batches() {
        let store = ResourceStore::new(1, callbacks(), theme());
        let configuration = configuration(Some(1));
        let engine = store.list_resource(&ResourceKey::new(7, shared("rows")), &configuration, 1);

        let spec = TableSpec {
            columns: vec![TableColumnSpec {
                key: shared("name"),
                header: shared("Name"),
                width: px(120.),
                width_is_fraction: false,
                alignment: 0,
            }],
        };
        store.bind_table_spec(
            &ResourceKey::new(7, shared("rows")),
            Rc::new(spec.clone()),
            &engine,
        );
        engine.borrow_mut().batches.insert(0, CachedBatch::new());
        assert_eq!(engine.borrow().batches.len(), 1);

        // Rebinding the identical spec is a no-op.
        store.bind_table_spec(
            &ResourceKey::new(7, shared("rows")),
            Rc::new(spec.clone()),
            &engine,
        );
        assert_eq!(engine.borrow().batches.len(), 1);

        engine.borrow_mut().batches.insert(48, CachedBatch::new());
        engine
            .borrow_mut()
            .apply_command(&command(COMMAND_LIST_REFRESH, 50, 1, ""));
        engine
            .borrow_mut()
            .apply_command(&command(COMMAND_LIST_SPLICE, 90, (1_u64 << 32) | 1, ""));
        let retained = store.list_resource(&ResourceKey::new(7, shared("rows")), &configuration, 2);
        assert!(Rc::ptr_eq(&engine, &retained));
        assert_eq!(batch_keys(&engine.borrow()), vec![0]);

        // Column changes still invalidate rows preserved by targeted refresh/splice hints.
        let changed = TableSpec {
            columns: vec![
                spec.columns[0].clone(),
                TableColumnSpec {
                    key: shared("size"),
                    header: shared("Size"),
                    width: px(80.),
                    width_is_fraction: false,
                    alignment: 2,
                },
            ],
        };
        store.bind_table_spec(
            &ResourceKey::new(7, shared("rows")),
            Rc::new(changed),
            &engine,
        );
        assert!(engine.borrow().batches.is_empty());
        assert_eq!(
            store
                .table_spec(&ResourceKey::new(7, shared("rows")))
                .unwrap()
                .columns
                .len(),
            2
        );
    }

    #[test]
    fn ambient_render_change_invalidates_retained_list_and_table_rows() {
        let store = ResourceStore::new(1, callbacks(), theme());
        let list = store.list_resource(
            &ResourceKey::new(7, shared("list")),
            &configuration(Some(1)),
            1,
        );
        let table = store.list_resource(
            &ResourceKey::new(7, shared("table")),
            &configuration(Some(1)),
            1,
        );
        list.borrow_mut().batches.insert(0, CachedBatch::new());
        table.borrow_mut().batches.insert(0, CachedBatch::new());

        store.invalidate_managed_rendered_rows();

        assert!(list.borrow().batches.is_empty());
        assert!(table.borrow().batches.is_empty());
        assert_eq!(list.borrow().telemetry().full_invalidations, 1);
        assert_eq!(table.borrow().telemetry().full_invalidations, 1);
    }

    #[test]
    fn click_refresh_flow_preserves_scroll_across_configure_and_rebind() {
        let key = ResourceKey::new(7, shared("grid"));
        let store = ResourceStore::new(1, callbacks(), theme());
        let mut config = configuration(None);
        config.item_count = 5_000;
        config.batch_size = 64;

        // First materialization: the table declares columns and the row engine is created.
        let engine = store.list_resource(&key, &config, 1);
        let spec = TableSpec {
            columns: vec![TableColumnSpec {
                key: shared("name"),
                header: shared("Name"),
                width: px(120.),
                width_is_fraction: false,
                alignment: 0,
            }],
        };
        store.bind_table_spec(&key, Rc::new(spec), &engine);

        // The user scrolls down to item 1000.
        engine.borrow().state.scroll_to(ListOffset {
            item_ix: 1000,
            offset_in_item: px(0.),
        });
        assert_eq!(engine.borrow().state.logical_scroll_top().item_ix, 1000);

        // Click: RefreshRanges queues two Refresh commands, then invalidates the view.
        engine.borrow_mut().apply_command(&command(13, 40, 1, ""));
        engine.borrow_mut().apply_command(&command(13, 41, 1, ""));

        // Next frame: the snapshot revision advanced and the table re-materialized.
        let engine = store.list_resource(&key, &config, 2);
        let spec = TableSpec {
            columns: vec![TableColumnSpec {
                key: shared("name"),
                header: shared("Name"),
                width: px(120.),
                width_is_fraction: false,
                alignment: 0,
            }],
        };
        store.bind_table_spec(&key, Rc::new(spec), &engine);

        let telemetry = engine.borrow().telemetry();
        assert_eq!(telemetry.full_invalidations, 0);
        assert_eq!(engine.borrow().state.logical_scroll_top().item_ix, 1000);
    }

    /// Regression test: a managed re-render must not drop a table's row engine. Table nodes
    /// were skipped by the retain adapter guard, so the first snapshot commit after a table
    /// appeared evicted the engine (fresh ListState = scroll jumped to the top).
    #[test]
    fn retain_snapshot_keeps_table_row_engines_across_renders() {
        use crate::{
            abi::{NodeRecord, OpRecord, RenderArena},
            semantic::{COMPONENT_TABLE, ValueKind},
        };

        let data = b"grid\0name\x1FName\x1F120\x1F0\x1F0";
        let mut node = NodeRecord {
            component: COMPONENT_TABLE,
            data_offset: 0,
            data_length: data.len() as u32,
            ..Default::default()
        };
        let mut operation = OpRecord {
            node: 0,
            code: OP_RESOURCE_OWNER,
            value_kind: ValueKind::U32 as u16,
            a: 4,
            ..Default::default()
        };
        let mut utf8 = data.to_vec();
        let arena = RenderArena {
            nodes: &mut node,
            node_length: 1,
            node_capacity: 1,
            ops: &mut operation,
            op_length: 1,
            op_capacity: 1,
            children: std::ptr::null_mut(),
            child_length: 0,
            child_capacity: 0,
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
        let mut retained_strings = RetainedStrings::default();
        let mut scratch = SnapshotScratch::default();
        snapshot
            .decode_into(&arena, 0, &mut retained_strings, &mut scratch)
            .expect("table snapshot decodes");

        let key = ResourceKey::new(4, shared("grid"));
        let store = ResourceStore::new(1, callbacks(), theme());
        let engine = store.list_resource(&key, &configuration(None), 1);
        store.retain_snapshot(&snapshot);
        assert!(Rc::ptr_eq(
            &store.lists.borrow().get(&key).expect("engine retained"),
            &engine
        ));

        // A later re-render retains the same snapshot shape; the engine must survive again.
        store.retain_snapshot(&snapshot);
        assert!(store.lists.borrow().contains_key(&key));
    }
}
