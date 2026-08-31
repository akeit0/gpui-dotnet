use std::{
    cell::{Cell, RefCell},
    collections::{HashMap, HashSet},
    rc::Rc,
    sync::Arc,
};

use gpui::{
    AnyElement, AppContext, Context, Entity, IntoElement, ListAlignment, ListOffset, ListState,
    ParentElement, Pixels, Point, ScrollHandle, SharedString, Window, div, point, px,
};

use crate::{
    abi::{ManagedCallbacks, NativeResourceCommand},
    app_host::ManagedView,
    arena::OwnedRenderArena,
    input::{InputBindings, InputInitialState, ManagedInput},
    semantic::{
        NativeAdapter, OP_INPUT_DISABLED, OP_INPUT_ON_CHANGED, OP_INPUT_ON_FOCUS_CHANGED,
        OP_INPUT_ON_SUBMITTED, OP_INPUT_PASSWORD, OP_INPUT_READ_ONLY, OP_LIST_ALIGNMENT,
        OP_LIST_BATCH_SIZE, OP_LIST_ESTIMATED_ITEM_HEIGHT_PX, OP_LIST_ITEM_COUNT,
        OP_LIST_OVERDRAW_PX, OP_LIST_RENDERER, OP_RESOURCE_OWNER, component_metadata,
    },
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
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
    scrolls: RefCell<HashMap<ResourceKey, Rc<ManagedScrollResource>>>,
    lists: RefCell<HashMap<ResourceKey, Rc<RefCell<ManagedListResource>>>>,
    inputs: RefCell<HashMap<ResourceKey, Entity<ManagedInput>>>,
    pending: RefCell<HashMap<(u16, ResourceKey), Vec<ResourceCommand>>>,
    active_scratch: RefCell<HashSet<(u16, ResourceKey)>>,
}

impl ResourceStore {
    pub(crate) fn new(session_id: u64, callbacks: ManagedCallbacks) -> Self {
        Self {
            session_id,
            callbacks,
            scrolls: RefCell::new(HashMap::new()),
            lists: RefCell::new(HashMap::new()),
            inputs: RefCell::new(HashMap::new()),
            pending: RefCell::new(HashMap::new()),
            active_scratch: RefCell::new(HashSet::new()),
        }
    }

    pub(crate) fn scroll_resource(&self, key: &ResourceKey) -> Rc<ManagedScrollResource> {
        if let Some(resource) = self.scrolls.borrow().get(key) {
            return resource.clone();
        }

        let resource = Rc::new(ManagedScrollResource::new());
        self.scrolls
            .borrow_mut()
            .insert(key.clone(), resource.clone());
        self.apply_pending(1, key);
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
        self.apply_pending(2, key);
        resource
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
            .remove(&(3, configuration.key.clone()))
            .unwrap_or_default();
        for command in pending {
            resource.update(cx, |input, cx| input.apply_command(&command, window, cx));
        }
        resource
    }

    pub(crate) fn dispatch(&self, command: ResourceCommand) -> bool {
        let applied = match command.resource_kind {
            1 => self.apply_scroll_command(&command),
            2 => self.apply_list_command(&command),
            3 => false,
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
                1 => self.apply_scroll_command(&command),
                2 => self.apply_list_command(&command),
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
            1 => {
                let x = f32::from_bits(command.a as u32);
                let y = f32::from_bits(command.b as u32);
                if x.is_finite() && y.is_finite() {
                    handle.set_offset(point(px(-x), px(-y)));
                }
            }
            2 => handle.set_offset(point(px(0.), px(0.))),
            3 => handle.scroll_to_bottom(),
            _ => {}
        }
        true
    }

    fn apply_list_command(&self, command: &ResourceCommand) -> bool {
        let Some(resource) = self.lists.borrow().get(&command.key).cloned() else {
            // Structural commands only preserve measurements. If the resource does not exist yet,
            // there are no measurements to preserve and the next managed snapshot will construct
            // ListState directly at the authoritative item count. Keep only imperative scrolling.
            return matches!(command.command, 11..=13);
        };
        resource.borrow_mut().apply_command(command);
        true
    }

    pub(crate) fn retain_snapshot(&self, snapshot: &ValidatedSnapshot) {
        let mut active = self.active_scratch.borrow_mut();
        active.clear();
        for node in &snapshot.nodes {
            let Some(metadata) = component_metadata(node.component) else {
                continue;
            };
            if !matches!(
                metadata.adapter,
                NativeAdapter::Scroll | NativeAdapter::List | NativeAdapter::Input
            ) {
                continue;
            }
            let active_resource = match metadata.adapter {
                NativeAdapter::Scroll => resource_key(snapshot, node).map(|key| (1, key)),
                NativeAdapter::List => resource_key(snapshot, node).map(|key| (2, key)),
                NativeAdapter::Input => {
                    input_configuration(snapshot, node).map(|configuration| (3, configuration.key))
                }
                _ => None,
            };
            if let Some(resource) = active_resource {
                active.insert(resource);
            }
        }
        self.scrolls
            .borrow_mut()
            .retain(|key, _| active.contains(&(1, key.clone())));
        self.lists
            .borrow_mut()
            .retain(|key, _| active.contains(&(2, key.clone())));
        self.inputs
            .borrow_mut()
            .retain(|key, _| active.contains(&(3, key.clone())));
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

pub(crate) struct ListConfiguration {
    pub(crate) item_count: usize,
    pub(crate) renderer_token: u64,
    pub(crate) batch_size: usize,
    pub(crate) overdraw: Pixels,
    pub(crate) alignment: ListAlignment,
    pub(crate) estimated_item_height: Pixels,
}

#[derive(Clone, Copy, Debug)]
pub(crate) enum ScrollbarDrag {
    Vertical { pointer_offset: Pixels },
    Horizontal { pointer_offset: Pixels },
}

#[derive(Default)]
pub(crate) struct ScrollInteraction {
    pub(crate) remaining: Cell<Point<Pixels>>,
    pub(crate) animating: Cell<bool>,
    pub(crate) drag: Cell<Option<ScrollbarDrag>>,
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

pub(crate) struct ManagedListResource {
    session_id: u64,
    callbacks: ManagedCallbacks,
    pub(crate) state: ListState,
    pub(crate) interaction: Rc<ScrollInteraction>,
    pub(crate) item_count: usize,
    renderer_token: u64,
    batch_size: usize,
    overdraw: Pixels,
    alignment: ListAlignment,
    snapshot_revision: u64,
    batches: HashMap<u32, CachedBatch>,
    pending_commands: Vec<ResourceCommand>,
    use_clock: u64,
}

impl ManagedListResource {
    fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
    ) -> Self {
        Self {
            session_id,
            callbacks,
            state: ListState::new(
                configuration.item_count,
                configuration.alignment,
                configuration.overdraw,
            ),
            interaction: Rc::new(ScrollInteraction::default()),
            item_count: configuration.item_count,
            renderer_token: configuration.renderer_token,
            batch_size: configuration.batch_size,
            overdraw: configuration.overdraw,
            alignment: configuration.alignment,
            snapshot_revision,
            batches: HashMap::new(),
            pending_commands: Vec::new(),
            use_clock: 0,
        }
    }

    fn configure(&mut self, configuration: &ListConfiguration, snapshot_revision: u64) {
        let revision_changed = self.snapshot_revision != snapshot_revision;
        let layout_changed =
            self.alignment != configuration.alignment || self.overdraw != configuration.overdraw;

        if layout_changed {
            // Rebuilding ListState already discards all measurements, so structural hints that
            // were waiting for this managed commit no longer provide any additional value.
            self.state = ListState::new(
                configuration.item_count,
                configuration.alignment,
                configuration.overdraw,
            );
            self.item_count = configuration.item_count;
            self.alignment = configuration.alignment;
            self.overdraw = configuration.overdraw;
            self.pending_commands.clear();
            self.batches.clear();
        } else if revision_changed && !self.pending_commands.is_empty() {
            self.commit_pending_commands(configuration.item_count);
        } else if self.item_count != configuration.item_count {
            // A normal declarative count change without a ListController splice hint still has to
            // be correct; it simply cannot preserve the old per-item measurements precisely.
            self.state.reset(configuration.item_count);
            self.item_count = configuration.item_count;
            self.batches.clear();
        }

        if self.renderer_token != configuration.renderer_token
            || self.batch_size != configuration.batch_size
        {
            self.renderer_token = configuration.renderer_token;
            self.batch_size = configuration.batch_size;
            self.batches.clear();
        }
        if revision_changed {
            self.snapshot_revision = snapshot_revision;
            // Any successful managed snapshot may reflect changed row data even when the
            // renderer token and item count stay the same. Preserve GPUI's retained list state
            // but invalidate managed row snapshots so visible batches are refreshed lazily.
            self.batches.clear();
        }
    }

    fn apply_command(&mut self, command: &ResourceCommand) {
        match command.command {
            10 if self.pending_commands.is_empty() => {
                let index = command.a as usize;
                if index < self.item_count {
                    self.scroll_to_item(index);
                }
            }
            10..=13 => {
                // Structural list commands are measurement-preservation hints. Applying them
                // immediately can race a frame that still materializes the previous managed
                // snapshot. Queue them until snapshot_revision advances, then commit the whole
                // command sequence against the new declarative item_count.
                self.pending_commands.push(command.clone());
            }
            _ => {}
        }
    }

    fn commit_pending_commands(&mut self, declared_item_count: usize) {
        let mut expected_count = self.item_count;
        let mut valid = true;
        for command in &self.pending_commands {
            match command.command {
                10 => {}
                11 => {
                    let start = command.a as usize;
                    let removed = (command.b >> 32) as usize;
                    let inserted = command.b as u32 as usize;
                    if start > expected_count || removed > expected_count.saturating_sub(start) {
                        valid = false;
                        break;
                    }
                    expected_count = expected_count - removed + inserted;
                }
                12 => expected_count = command.a as usize,
                13 => {
                    let start = command.a as usize;
                    let count = command.b as usize;
                    if start > expected_count || count > expected_count.saturating_sub(start) {
                        valid = false;
                        break;
                    }
                }
                _ => {}
            }
        }

        if !valid || expected_count != declared_item_count {
            // Managed state is authoritative. If the hints do not describe the snapshot that was
            // actually committed, fall back to a full native list reset rather than risking
            // measurement/index corruption.
            self.state.reset(declared_item_count);
            self.item_count = declared_item_count;
            self.pending_commands.clear();
            self.batches.clear();
            return;
        }

        let mut current_count = self.item_count;
        let pending_commands = std::mem::take(&mut self.pending_commands);
        for command in pending_commands {
            match command.command {
                10 => {
                    let index = command.a as usize;
                    if index < current_count {
                        self.scroll_to_item(index);
                    }
                }
                11 => {
                    let start = command.a as usize;
                    let removed = (command.b >> 32) as usize;
                    let inserted = command.b as u32 as usize;
                    self.state.splice(start..start + removed, inserted);
                    current_count = current_count - removed + inserted;
                }
                12 => {
                    current_count = command.a as usize;
                    self.state.reset(current_count);
                }
                13 => {
                    let start = command.a as usize;
                    let count = command.b as usize;
                    self.state.splice(start..start + count, count);
                }
                _ => {}
            }
        }
        self.item_count = declared_item_count;
        self.batches.clear();
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
        if !self.batches.contains_key(&start_u32) {
            if let Err(status) = self.load_batch(start_u32) {
                return div()
                    .child(format!(
                        "Managed list range render failed with status {status}."
                    ))
                    .into_any_element();
            }
            self.trim_batches();
        }

        let batch = self.batches.get_mut(&start_u32).expect("batch was loaded");
        batch.last_used = self.use_clock;
        let local = index - start;
        let roots = batch
            .snapshot
            .children(&batch.snapshot.nodes[batch.snapshot.root as usize]);
        let Some(root) = roots.get(local).copied() else {
            return div()
                .child("Managed list batch returned the wrong item count.")
                .into_any_element();
        };

        crate::materializer::materialize_snapshot_node_detached(
            root,
            &batch.snapshot,
            self.session_id,
            self.callbacks,
            resources,
            list_key,
            index,
        )
    }

    fn load_batch(&mut self, start: u32) -> Result<(), i32> {
        let count = self
            .batch_size
            .min(self.item_count.saturating_sub(start as usize)) as u32;
        let mut batch = CachedBatch::new();
        let callback = self
            .callbacks
            .list_render_range
            .expect("callbacks were validated before application startup");
        let mut root = 0u32;
        let status = batch
            .arena
            .render_with_growth_retry(|arena| unsafe {
                callback(
                    self.session_id,
                    self.renderer_token,
                    start,
                    count,
                    arena,
                    &mut root,
                )
            })
            .unwrap_or_else(|status| status);
        if status != 0 {
            return Err(status);
        }
        batch.snapshot.decode_into(
            batch.arena.as_native(),
            root,
            &mut batch.retained_strings,
            &mut batch.scratch,
        )?;
        let root_node = &batch.snapshot.nodes[batch.snapshot.root as usize];
        if batch.snapshot.children(root_node).len() != count as usize {
            return Err(-63);
        }
        batch.last_used = self.use_clock;
        self.batches.insert(start, batch);
        Ok(())
    }

    fn trim_batches(&mut self) {
        const MAX_BATCHES: usize = 4;
        while self.batches.len() > MAX_BATCHES {
            let Some((&oldest, _)) = self.batches.iter().min_by_key(|(_, batch)| batch.last_used)
            else {
                break;
            };
            self.batches.remove(&oldest);
        }
    }
}

struct CachedBatch {
    arena: OwnedRenderArena,
    retained_strings: RetainedStrings,
    snapshot: ValidatedSnapshot,
    scratch: SnapshotScratch,
    last_used: u64,
}

impl CachedBatch {
    fn new() -> Self {
        Self {
            arena: OwnedRenderArena::new(),
            retained_strings: RetainedStrings::default(),
            snapshot: ValidatedSnapshot::default(),
            scratch: SnapshotScratch::default(),
            last_used: 0,
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
    Some(ListConfiguration {
        item_count,
        renderer_token: renderer,
        batch_size,
        overdraw,
        alignment,
        estimated_item_height,
    })
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
