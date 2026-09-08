use std::{
    cell::RefCell,
    collections::{HashMap, HashSet},
    rc::Rc,
};

use super::{
    configuration::{ListConfiguration, TableSpec},
    engine::CollectionEngine,
};
use crate::{
    abi::{ManagedCallbacks, NativeArtifactKey},
    resources::{ResourceCommand, ResourceKey},
    semantic::{
        COMMAND_LIST_REFRESH, COMMAND_LIST_RESET, COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_LIST_SPLICE,
        RESOURCE_LIST,
    },
};

/// Retained List/Table ownership. Tables share the List collection engine; only their column
/// metadata lives here. The registry owns *which* collections exist and their pre-declaration
/// command queue. Each engine owns *how* configuration, commands, invalidation, and
/// retirement affect its native `ListState`, batches, measurements, and cursor.
pub(crate) struct CollectionRegistry {
    engines: RefCell<HashMap<ResourceKey, Rc<RefCell<CollectionEngine>>>>,
    tables: RefCell<HashMap<ResourceKey, Rc<TableSpec>>>,
    pending: RefCell<HashMap<ResourceKey, Vec<ResourceCommand>>>,
}

impl CollectionRegistry {
    pub(crate) fn new() -> Self {
        Self {
            engines: RefCell::new(HashMap::new()),
            tables: RefCell::new(HashMap::new()),
            pending: RefCell::new(HashMap::new()),
        }
    }

    pub(crate) fn declare(
        &self,
        key: &ResourceKey,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
        session_id: u64,
        callbacks: ManagedCallbacks,
    ) -> Rc<RefCell<CollectionEngine>> {
        let existing = self.engines.borrow().get(key).cloned();
        let resource = if let Some(existing) = existing {
            existing
        } else {
            let created = Rc::new(RefCell::new(CollectionEngine::new(
                session_id,
                callbacks,
                configuration,
                snapshot_revision,
            )));
            self.engines
                .borrow_mut()
                .insert(key.clone(), created.clone());
            created
        };
        resource
            .borrow_mut()
            .configure(configuration, snapshot_revision);
        if let Some(commands) = self.pending.borrow_mut().remove(key) {
            let mut engine = resource.borrow_mut();
            for command in &commands {
                engine.apply_command(command);
            }
        }
        resource
    }

    /// Binds the column metadata declared by the current snapshot to a table's collection engine.
    /// A changed column table changes cell layout, so every cached item batch is invalidated.
    pub(crate) fn bind_table_spec(
        &self,
        key: &ResourceKey,
        spec: Rc<TableSpec>,
        row_engine: &Rc<RefCell<CollectionEngine>>,
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

    pub(crate) fn lookup(&self, key: &ResourceKey) -> Option<Rc<RefCell<CollectionEngine>>> {
        self.engines.borrow().get(key).cloned()
    }

    /// Clones the collection-engine handles for diagnostics aggregation. Safe outside the render path
    /// (frame boundaries hold no RefCell borrows).
    pub(crate) fn engines(&self) -> Vec<Rc<RefCell<CollectionEngine>>> {
        self.engines.borrow().values().cloned().collect()
    }

    pub(crate) fn engine_count(&self) -> usize {
        self.engines.borrow().len()
    }

    /// Discards retained managed item snapshots after an ambient theme or managed-code update.
    /// Tables use the same collection engines as lists, so this covers both.
    pub(crate) fn invalidate_managed_rows(&self) {
        for engine in self.engines() {
            engine.borrow_mut().invalidate_all_batches();
        }
    }

    /// Unions image hashes declared by every cached virtual-item batch into `live`. Item
    /// snapshots are authoritative for item content the same way the mounted snapshot is for
    /// mounted content, so the image cache must not evict them while they are cached.
    pub(crate) fn cached_image_hashes(&self, live: &mut HashSet<u64>) {
        for engine in self.engines() {
            engine.borrow().cached_image_hashes(live);
        }
    }

    pub(crate) fn invalidate_artifacts(&self, keys: &mut [NativeArtifactKey]) -> bool {
        if keys.is_empty() {
            return false;
        }
        // The ingress message already owns these records. Index it in place once for
        // all collection engines, without another allocation or retained artifact registry.
        keys.sort_unstable_by_key(|key| (key.source, key.artifact));
        let mut changed = false;
        for engine in self.engines() {
            changed |= engine.borrow_mut().invalidate_sorted_artifacts(keys);
        }
        changed
    }

    pub(crate) fn dispatch(&self, command: ResourceCommand) {
        if let Some(engine) = self.lookup(&command.key) {
            engine.borrow_mut().apply_command(&command);
            return;
        }
        // Structural commands only preserve measurements. If the resource does not exist yet,
        // there are no measurements to preserve and the next managed snapshot will construct
        // ListState directly at the authoritative item count. Keep only imperative scrolling.
        if matches!(
            command.command,
            COMMAND_LIST_SPLICE | COMMAND_LIST_RESET | COMMAND_LIST_REFRESH
        ) {
            return;
        }
        debug_assert_eq!(command.command, COMMAND_LIST_SCROLL_TO_ITEM);
        self.pending
            .borrow_mut()
            .entry(command.key.clone())
            .or_default()
            .push(command);
    }

    pub(crate) fn retain(&self, active: &HashSet<(u16, ResourceKey)>) {
        self.engines.borrow_mut().retain(|key, engine| {
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
        self.pending
            .borrow_mut()
            .retain(|key, _| active.contains(&(RESOURCE_LIST, key.clone())));
    }
}
