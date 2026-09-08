use std::{collections::HashMap, rc::Rc};

use gpui::{
    AnyElement, IntoElement, ListAlignment, ListOffset, ListState, ParentElement, Pixels, Point,
    div, px,
};

use super::{
    configuration::{ListConfiguration, ListOrientation},
    cursor::CollectionCursor,
    events::{ListItemEventKind, ListItemEvents},
    horizontal::HorizontalState,
};
use crate::{
    abi::ManagedCallbacks,
    demand::{ArtifactLease, load_artifact, next_source_id},
    resources::{ResourceCommand, ResourceKey, ResourceStore, ScrollInteraction},
    semantic::{
        COMMAND_LIST_REFRESH, COMMAND_LIST_RESET, COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_LIST_SPLICE,
        OP_LIST_ITEM_ID,
    },
    snapshot::{SnapshotScratch, ValidatedSnapshot},
};

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
    pub(crate) rendered_items: u64,
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
pub(crate) struct CollectionEngine {
    pub(crate) tooltip_token: u64,
    pub(crate) tooltip: crate::tooltip::TooltipConfiguration,
    pub(crate) source_id: u64,
    session_id: u64,
    callbacks: ManagedCallbacks,
    pub(crate) state: ListState,
    pub(crate) interaction: Rc<ScrollInteraction>,
    pub(crate) cursor: Rc<CollectionCursor>,
    pub(crate) item_count: usize,
    pub(crate) renderer_token: u64,
    activation_token: u64,
    selection_token: u64,
    pub(crate) batch_size: usize,
    overdraw: Pixels,
    alignment: ListAlignment,
    estimated_item_extent: Pixels,
    orientation: ListOrientation,
    pub(crate) horizontal: HorizontalState,
    hinted_viewport_width: Option<Pixels>,
    snapshot_revision: u64,
    content_revision: Option<u64>,
    pub(crate) batches: HashMap<u32, CachedBatch>,
    projection_revision: Option<u64>,
    // Batches own decoded output; validation/grouping scratch is reused serially by the engine.
    pub(crate) scratch: SnapshotScratch,
    pub(crate) pending_commands: Vec<ResourceCommand>,
    pub(crate) use_clock: u64,
    pub(crate) frame_start: u64,
    pub(crate) last_batch: Option<u32>,
    pub(crate) telemetry: ListTelemetry,
}

impl CollectionEngine {
    pub(crate) fn new(
        session_id: u64,
        callbacks: ManagedCallbacks,
        configuration: &ListConfiguration,
        snapshot_revision: u64,
    ) -> Self {
        Self {
            source_id: next_source_id(),
            tooltip_token: configuration.tooltip_token,
            tooltip: configuration.tooltip,
            session_id,
            callbacks,
            state: ListState::new(
                configuration.item_count,
                configuration.alignment,
                configuration.overdraw,
            )
            .with_uniform_item_height(configuration.estimated_item_extent),
            interaction: Rc::new(ScrollInteraction::default()),
            cursor: Rc::new(CollectionCursor::new(configuration.item_count)),
            item_count: configuration.item_count,
            renderer_token: configuration.renderer_token,
            activation_token: configuration.activation_token,
            selection_token: configuration.selection_token,
            batch_size: configuration.batch_size,
            overdraw: configuration.overdraw,
            alignment: configuration.alignment,
            estimated_item_extent: configuration.estimated_item_extent,
            orientation: configuration.orientation,
            horizontal: HorizontalState::new(
                configuration.item_count,
                configuration.estimated_item_extent,
                configuration.alignment == ListAlignment::Bottom,
            ),
            hinted_viewport_width: None,
            snapshot_revision,
            content_revision: configuration.content_revision,
            batches: HashMap::new(),
            projection_revision: configuration.projection_revision,
            scratch: SnapshotScratch::default(),
            pending_commands: Vec::new(),
            use_clock: 0,
            frame_start: 0,
            last_batch: None,
            telemetry: ListTelemetry::default(),
        }
    }

    pub(crate) fn configure(&mut self, configuration: &ListConfiguration, snapshot_revision: u64) {
        self.tooltip_token = configuration.tooltip_token;
        self.tooltip = configuration.tooltip;
        if self.activation_token != configuration.activation_token
            || self.selection_token != configuration.selection_token
        {
            self.activation_token = configuration.activation_token;
            self.selection_token = configuration.selection_token;
            self.cursor.invalidate_items();
        }
        let revision_changed = self.snapshot_revision != snapshot_revision;
        let content_changed = match (self.content_revision, configuration.content_revision) {
            (Some(previous), Some(current)) => previous != current,
            (None, None) => revision_changed,
            _ => true,
        };
        let layout_changed = self.alignment != configuration.alignment
            || self.overdraw != configuration.overdraw
            || self.estimated_item_extent != configuration.estimated_item_extent;
        let end_anchored = configuration.alignment == ListAlignment::Bottom;

        if self.orientation != configuration.orientation {
            // An axis change discards every axis-specific measurement and hint. Item indices
            // are unchanged, so the cursor survives; scroll restarts at the anchored edge.
            self.orientation = configuration.orientation;
            self.estimated_item_extent = configuration.estimated_item_extent;
            self.horizontal.reset(
                self.item_count,
                configuration.estimated_item_extent,
                end_anchored,
            );
            self.pending_commands.clear();
            self.clear_batches();
            self.cursor.invalidate_items();
        } else if self.estimated_item_extent != configuration.estimated_item_extent {
            self.estimated_item_extent = configuration.estimated_item_extent;
            if self.orientation == ListOrientation::Horizontal {
                self.horizontal
                    .rehint_estimates(configuration.estimated_item_extent);
            }
        }

        // Reconcile positional identity before a simultaneous layout rebuild discards measurements.
        if self.projection_revision != configuration.projection_revision {
            // The accepted projection supersedes hints expressed in the old index space.
            self.reset_native_state(configuration.item_count);
        } else if revision_changed && !self.pending_commands.is_empty() {
            self.commit_pending_commands(configuration.item_count);
        }
        if self.item_count != configuration.item_count {
            self.reset_native_state(configuration.item_count);
        }

        if layout_changed {
            if self.orientation == ListOrientation::Horizontal {
                // Widths are axis-independent of alignment; only the anchored edge moves.
                // The vertical ListState below is unused while horizontal.
                if self.alignment != configuration.alignment {
                    self.horizontal.set_end_anchored(end_anchored);
                }
            } else {
                // Rebuild measurements while keeping the cursor reconciled with the accepted items.
                self.state = ListState::new(
                    configuration.item_count,
                    configuration.alignment,
                    configuration.overdraw,
                )
                .with_uniform_item_height(configuration.estimated_item_extent);
            }
            self.item_count = configuration.item_count;
            self.alignment = configuration.alignment;
            self.overdraw = configuration.overdraw;
            self.estimated_item_extent = configuration.estimated_item_extent;
            self.hinted_viewport_width = None;
            self.pending_commands.clear();
            self.clear_batches();
            self.cursor.invalidate_items();
        }

        if self.renderer_token != configuration.renderer_token
            || self.batch_size != configuration.batch_size
        {
            self.renderer_token = configuration.renderer_token;
            self.batch_size = configuration.batch_size;
            self.clear_batches();
            self.cursor.invalidate_items();
        }
        if revision_changed {
            self.snapshot_revision = snapshot_revision;
        }
        if content_changed {
            self.clear_batches();
            self.cursor.invalidate_items();
        }
        self.content_revision = configuration.content_revision;
        self.projection_revision = configuration.projection_revision;
    }

    pub(crate) fn apply_command(&mut self, command: &ResourceCommand) {
        match command.command {
            COMMAND_LIST_SCROLL_TO_ITEM if self.pending_commands.is_empty() => {
                let index = command.a as usize;
                if index < self.item_count {
                    self.scroll_to_item(index);
                }
            }
            COMMAND_LIST_SCROLL_TO_ITEM
            | COMMAND_LIST_SPLICE
            | COMMAND_LIST_RESET
            | COMMAND_LIST_REFRESH => {
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

    pub(crate) fn commit_pending_commands(&mut self, declared_item_count: usize) {
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
                    if self.orientation == ListOrientation::Horizontal {
                        // Inserted widths start at the estimate; unaffected widths are kept.
                        self.horizontal.splice(start, removed, inserted);
                    } else {
                        self.state.splice(start..start + removed, inserted);
                        inserted_unmeasured_items |= inserted > 0;
                    }
                    self.cursor.splice(start, removed, inserted);
                    current_count = current_count - removed + inserted;
                }
                ListChange::Reset(count) => {
                    current_count = count;
                    self.cursor.reset(count);
                    inserted_unmeasured_items = false;
                    if self.orientation == ListOrientation::Horizontal {
                        self.horizontal.reset(
                            count,
                            self.estimated_item_extent,
                            self.alignment == ListAlignment::Bottom,
                        );
                    } else {
                        self.state
                            .reset_with_uniform_height(count, self.estimated_item_extent);
                    }
                    self.clear_batches();
                }
                ListChange::Refresh { start, count } => {
                    self.invalidate_batches_intersecting(start, count);
                    if self.orientation == ListOrientation::Horizontal {
                        self.horizontal.refresh(start, count);
                    } else {
                        self.state.remeasure_items(start..start + count);
                    }
                }
            }
        }
        if inserted_unmeasured_items {
            // GPUI's splice API does not accept a size hint for inserted items. Reapplying the
            // uniform hint fills those gaps while retaining each unaffected item's previous
            // measured height as its new hint, so the full scrollbar range remains available.
            self.state
                .clone()
                .with_uniform_item_height(self.estimated_item_extent);
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
        self.cursor.reset(declared_item_count);
        self.state
            .reset_with_uniform_height(declared_item_count, self.estimated_item_extent);
        self.horizontal.reset(
            declared_item_count,
            self.estimated_item_extent,
            self.alignment == ListAlignment::Bottom,
        );
        self.item_count = declared_item_count;
        self.pending_commands.clear();
        self.clear_batches();
    }

    pub(crate) fn clear_batches(&mut self) {
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

    pub(crate) fn invalidate_batches_intersecting(&mut self, start: usize, count: usize) {
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

    pub(crate) fn scroll_to_item(&mut self, index: usize) {
        self.interaction.remaining.set(Point::default());
        if self.orientation == ListOrientation::Horizontal {
            // Reveal the item with the minimum horizontal movement, keeping distant jumps
            // virtualized without requiring preceding variable-width items to be measured.
            self.horizontal.reveal(index);
            return;
        }
        // Use GPUI's logical list offset directly. Unlike reveal-by-pixel operations, this does
        // not require preceding variable-height items to be measured and keeps distant jumps
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

    /// Overdraw extends along the scroll axis in either orientation.
    pub(crate) fn horizontal_overdraw(&self) -> Pixels {
        self.overdraw
    }

    /// Horizontal counterpart to [`Self::maintain_height_hints`]. Item widths are measured
    /// against the viewport height, so a height change marks every width for remeasurement
    /// (keeping current values as hints) before the scrollbar reads the range.
    pub(crate) fn maintain_width_hints(&mut self) {
        // All viewport and overdraw items have been requested by horizontal prepaint.
        self.trim_batches();
        self.horizontal
            .note_viewport_height(self.horizontal.viewport.height);
    }

    pub(crate) fn maintain_height_hints(&mut self) {
        // All viewport and overdraw items have been requested by list prepaint.
        self.trim_batches();
        let width = self.state.viewport_bounds().size.width;
        if width <= px(0.) || self.hinted_viewport_width == Some(width) {
            return;
        }

        self.state
            .clone()
            .with_uniform_item_height(self.estimated_item_extent);
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
        self.telemetry.rendered_items += 1;
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

    pub(crate) fn cached_item_events(&self, index: usize) -> Option<ListItemEvents> {
        if (self.activation_token == 0 && self.selection_token == 0) || index >= self.item_count {
            return None;
        }
        self.cached_item_identity(index)
    }

    pub(crate) fn cached_identified_item(&self, index: usize) -> Option<(u64, ListItemEvents)> {
        let start = (index / self.batch_size) * self.batch_size;
        let artifact = self
            .batches
            .get(&(start as u32))?
            .lease
            .as_ref()?
            .artifact_id;
        let events = self.cached_item_identity(index)?;
        events.item_id?;
        Some((artifact, events))
    }

    fn cached_item_identity(&self, index: usize) -> Option<ListItemEvents> {
        if index >= self.item_count {
            return None;
        }
        let start = (index / self.batch_size) * self.batch_size;
        let batch = self.batches.get(&(start as u32))?;
        let parent = &batch.snapshot.nodes[batch.snapshot.root as usize];
        let root = *batch.snapshot.children(parent).get(index - start)?;
        let item_id = batch
            .snapshot
            .ops(&batch.snapshot.nodes[root as usize])
            .iter()
            .rev()
            .find(|op| op.code == OP_LIST_ITEM_ID)
            .map(|op| op.a)
            .filter(|id| *id != 0);
        Some(ListItemEvents {
            session_id: self.session_id,
            callbacks: self.callbacks,
            activation_token: self.activation_token,
            selection_token: self.selection_token,
            index: index as u32,
            item_id,
            content_revision: self.content_revision,
        })
    }

    pub(crate) fn event_enabled(&self, kind: ListItemEventKind) -> bool {
        match kind {
            ListItemEventKind::Activation => self.activation_token != 0,
            ListItemEventKind::Selection => self.selection_token != 0,
        }
    }

    pub(crate) fn prepare_item_event(
        &mut self,
        index: usize,
        kind: ListItemEventKind,
    ) -> Result<Option<ListItemEvents>, (u64, i32)> {
        if !self.event_enabled(kind) || index >= self.item_count {
            return Ok(None);
        }
        let start = ((index / self.batch_size) * self.batch_size) as u32;
        self.use_clock = self.use_clock.wrapping_add(1).max(1);
        if !self.batches.contains_key(&start) {
            self.load_batch(start)
                .map_err(|status| (self.session_id, status))?;
        }
        self.batches
            .get_mut(&start)
            .expect("batch loaded")
            .last_used = self.use_clock;
        Ok(self.cached_item_events(index))
    }

    pub(crate) fn load_batch(&mut self, start: u32) -> Result<(), i32> {
        let count = self
            .batch_size
            .min(self.item_count.saturating_sub(start as usize)) as u32;
        let callback = self
            .callbacks
            .list_render_range
            .expect("callbacks were validated before application startup");
        let (snapshot, lease) = load_artifact(
            self.session_id,
            self.source_id,
            self.callbacks,
            &mut self.scratch,
            |arena, root, artifact_id| unsafe {
                callback(
                    self.session_id,
                    self.renderer_token,
                    self.source_id,
                    start,
                    count,
                    arena,
                    root,
                    artifact_id,
                )
            },
            |snapshot| {
                let root = &snapshot.nodes[snapshot.root as usize];
                if snapshot.children(root).len() == count as usize {
                    Ok(())
                } else {
                    Err(-63)
                }
            },
        )?;
        let batch = CachedBatch {
            snapshot,
            lease: Some(lease),
            last_used: self.use_clock,
        };
        self.batches.insert(start, batch);
        self.telemetry.batch_loads += 1;
        Ok(())
    }

    pub(crate) fn trim_batches(&mut self) {
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

    /// Discards every cached item batch. Used when a table's column table changes, which
    /// changes the layout of all items at once.
    pub(crate) fn invalidate_all_batches(&mut self) {
        self.clear_batches();
    }

    pub(crate) fn invalidate_sorted_artifacts(
        &mut self,
        keys: &[crate::abi::NativeArtifactKey],
    ) -> bool {
        let start = keys.partition_point(|key| key.source < self.source_id);
        let rest = &keys[start..];
        let count = rest.partition_point(|key| key.source == self.source_id);
        let keys = &rest[..count];
        if keys.is_empty() {
            return false;
        }
        let before = self.batches.len();
        self.batches.retain(|start, batch| {
            let remove = batch.lease.as_ref().is_some_and(|lease| {
                keys.binary_search_by_key(&lease.artifact_id, |key| key.artifact)
                    .is_ok()
            });
            if remove {
                let count = self
                    .batch_size
                    .min(self.item_count.saturating_sub(*start as usize));
                if self.orientation == ListOrientation::Horizontal {
                    self.horizontal.refresh(*start as usize, count);
                } else {
                    self.state
                        .remeasure_items(*start as usize..*start as usize + count);
                }
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
pub(crate) struct CachedBatch {
    pub(crate) lease: Option<ArtifactLease>,
    pub(crate) snapshot: ValidatedSnapshot,
    pub(crate) last_used: u64,
}

impl CachedBatch {
    #[cfg(test)]
    pub(crate) fn new() -> Self {
        Self {
            lease: None,
            snapshot: ValidatedSnapshot::default(),
            last_used: 0,
        }
    }
}
