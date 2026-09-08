use std::{
    cell::{OnceCell, RefCell},
    collections::HashSet,
    rc::Rc,
    sync::Arc,
};

use gpui::{FontFallbacks, FontFeatures, SharedString};

use crate::{
    abi::{NodeRecord, OpRecord, RenderArena},
    semantic::{
        COMPONENT_CONTEXT_MENU, COMPONENT_DOCK_AREA, COMPONENT_DOCK_PANEL, COMPONENT_DOCK_REGION,
        COMPONENT_DOCK_SPLIT, COMPONENT_DOCK_TABS, COMPONENT_DRAWING, COMPONENT_DYNAMIC,
        COMPONENT_INPUT, COMPONENT_LIST, COMPONENT_NATIVE_EXTENSION, COMPONENT_OVERLAY,
        COMPONENT_PATH, COMPONENT_POPOVER_MENU, COMPONENT_SCROLL, COMPONENT_SLIDER,
        COMPONENT_TABLE, COMPONENT_TOOLTIP, DataKind, OP_DOCK_ACTIVE_INDEX, OP_DOCK_REGION_SIDE,
        OP_DRAWING_VIEW_BOX_SIZE, OP_FONT_FALLBACKS, OP_FONT_FEATURES, OP_PATH_ARC_RADII,
        OP_RESOURCE_OWNER, OP_TABLE_COLUMN, ValueKind, allows_payload, component_metadata,
        operation_metadata, payload_error,
    },
};

#[derive(Clone)]
pub struct SnapshotNode {
    pub component: u16,
    pub data: SharedString,
    op_start: u32,
    op_len: u32,
    child_start: u32,
    child_len: u32,
}

#[derive(Default)]
pub struct ValidatedSnapshot {
    pub root: u32,
    pub nodes: Vec<SnapshotNode>,
    ops: Vec<OpRecord>,
    children: Vec<u32>,
    op_data: Vec<Option<SharedString>>,
    drawing_cache: OnceCell<Rc<RefCell<crate::drawing_cache::DrawingCache>>>,
    drawing_command_pool: OnceCell<Rc<RefCell<crate::drawing_commands::DrawingCommandPool>>>,
}

impl ValidatedSnapshot {
    pub(crate) fn drawing_commands(
        &self,
        node: &SnapshotNode,
    ) -> crate::drawing_commands::DrawingCommands {
        let children = self.children(node);
        let count = children
            .iter()
            .map(|child| self.ops(&self.nodes[*child as usize]).len())
            .sum();
        let pool = self.drawing_command_pool.get_or_init(Default::default);
        let mut commands =
            crate::drawing_commands::DrawingCommandPool::acquire(pool.clone(), count);
        for child in children {
            commands.extend_from_slice(self.ops(&self.nodes[*child as usize]));
        }
        commands
    }

    pub(crate) fn drawing_cache(&self) -> Rc<RefCell<crate::drawing_cache::DrawingCache>> {
        self.drawing_cache.get_or_init(Default::default).clone()
    }

    pub(crate) fn clear_drawing_cache(&mut self) {
        self.drawing_cache.take();
    }

    #[cfg(test)]
    pub(crate) fn drawing_cache_bytes(&self) -> usize {
        self.drawing_cache
            .get()
            .map_or(0, |cache| cache.borrow().retained_bytes())
    }

    #[cfg(test)]
    pub(crate) fn drawing_command_pool_bytes(&self) -> usize {
        self.drawing_command_pool
            .get()
            .map_or(0, |pool| pool.borrow().retained_bytes())
    }

    #[cfg(test)]
    pub(crate) fn buffer_capacity_bytes(&self) -> usize {
        self.nodes.capacity() * size_of::<SnapshotNode>()
            + self.ops.capacity() * size_of::<OpRecord>()
            + self.children.capacity() * size_of::<u32>()
            + self.op_data.capacity() * size_of::<Option<SharedString>>()
    }

    pub fn decode_into(
        &mut self,
        arena: &RenderArena,
        root: u32,
        retained_strings: &mut RetainedStrings,
        scratch: &mut SnapshotScratch,
    ) -> Result<(), i32> {
        validate_with_scratch(arena, root, scratch)?;
        // Old painted elements may retain the previous cache until their frame is released.
        // Replacement snapshots must never reuse geometry from the old description.
        self.clear_drawing_cache();

        let nodes = unsafe { slice_or_empty(arena.nodes, arena.node_length as usize) };
        let ops = unsafe { slice_or_empty(arena.ops, arena.op_length as usize) };
        let utf8 = unsafe { slice_or_empty(arena.utf8, arena.utf8_length as usize) };

        scratch.prepare_ops(nodes.len());
        for op in ops {
            scratch.op_counts[op.node as usize] += 1;
        }
        prefix_offsets(&scratch.op_counts, &mut scratch.op_offsets);
        scratch.op_cursor.clear();
        scratch
            .op_cursor
            .extend_from_slice(&scratch.op_offsets[..nodes.len()]);

        self.ops.clear();
        self.ops.resize(ops.len(), OpRecord::default());
        for op in ops {
            let node = op.node as usize;
            let destination = scratch.op_cursor[node];
            self.ops[destination] = *op;
            scratch.op_cursor[node] += 1;
        }

        self.children.clear();
        self.children.extend_from_slice(&scratch.grouped_children);

        retained_strings.begin_snapshot();
        self.op_data.clear();
        for (index, op) in self.ops.iter().enumerate() {
            let is_data = operation_metadata(op.code)
                .is_some_and(|metadata| metadata.value_kind == ValueKind::Data);
            if !is_data {
                continue;
            }
            // Most row operations are numeric or callbacks. Keep the indexed string table
            // absent unless a data operation actually needs it; subsequent decodes reuse capacity.
            if self.op_data.is_empty() {
                self.op_data.resize(self.ops.len(), None);
            }
            let start = op.a as usize;
            let end = start + op.b as usize;
            self.op_data[index] = Some(
                retained_strings.intern(
                    std::str::from_utf8(&utf8[start..end])
                        .expect("validated data payload must remain valid UTF-8"),
                ),
            );
        }
        self.nodes.clear();
        self.nodes.reserve(nodes.len());
        for (index, node) in nodes.iter().enumerate() {
            let start = node.data_offset as usize;
            let end = start + node.data_length as usize;
            let data = retained_strings.intern(
                std::str::from_utf8(&utf8[start..end])
                    .expect("validated node payload must remain valid UTF-8"),
            );

            self.nodes.push(SnapshotNode {
                component: node.component,
                data,
                op_start: scratch.op_offsets[index] as u32,
                op_len: scratch.op_counts[index] as u32,
                child_start: scratch.child_offsets[index] as u32,
                child_len: scratch.child_counts[index] as u32,
            });
        }
        self.root = root;
        // Scratch can span decoded replacements, but a description without Drawings should
        // release it. Older command captures keep their own pool handle until released.
        if self.drawing_command_pool.get().is_some()
            && !self
                .nodes
                .iter()
                .any(|node| node.component == COMPONENT_DRAWING)
        {
            self.drawing_command_pool.take();
        }
        Ok(())
    }

    #[inline]
    pub fn ops(&self, node: &SnapshotNode) -> &[OpRecord] {
        let start = node.op_start as usize;
        &self.ops[start..start + node.op_len as usize]
    }

    #[inline]
    pub fn children(&self, node: &SnapshotNode) -> &[u32] {
        let start = node.child_start as usize;
        &self.children[start..start + node.child_len as usize]
    }

    /// Returns the interned string of the last data-valued operation with the given code on
    /// the node. Data payloads are interned at decode time because the arena UTF-8 buffer is
    /// reused by subsequent renders.
    pub(crate) fn last_data_op(&self, node: &SnapshotNode, code: u16) -> Option<SharedString> {
        let ops = self.ops(node);
        if ops.is_empty() {
            return None;
        }
        let base =
            (ops.as_ptr() as usize - self.ops.as_ptr() as usize) / std::mem::size_of::<OpRecord>();
        ops.iter()
            .enumerate()
            .rev()
            .find(|(_, op)| op.code == code)
            .and_then(|(index, _)| self.op_data.get(base + index).cloned().flatten())
    }
}

/// Keeps payload allocations stable while their text remains present in consecutive snapshots.
/// Only current and previous generations are retained, preventing unbounded interning.
#[derive(Default)]
pub struct RetainedStrings {
    current: HashSet<SharedString>,
    previous: HashSet<SharedString>,
}

impl RetainedStrings {
    fn begin_snapshot(&mut self) {
        std::mem::swap(&mut self.current, &mut self.previous);
        self.current.clear();
    }

    fn intern(&mut self, value: &str) -> SharedString {
        if let Some(value) = self.current.get(value) {
            return value.clone();
        }

        if let Some(value) = self.previous.take(value) {
            self.current.insert(value.clone());
            return value;
        }

        let value = SharedString::new(Arc::<str>::from(value));
        self.current.insert(value.clone());
        value
    }
}

/// Reusable validation/grouping memory owned by the native managed-view instance. Dirty renders
/// grow these buffers to a high-water mark and then reuse them instead of allocating Vecs per node.
#[derive(Default)]
pub struct SnapshotScratch {
    parents: Vec<u32>,
    visited: Vec<u8>,
    pending: Vec<u32>,
    child_counts: Vec<usize>,
    child_offsets: Vec<usize>,
    child_cursor: Vec<usize>,
    grouped_children: Vec<u32>,
    op_counts: Vec<usize>,
    op_offsets: Vec<usize>,
    op_cursor: Vec<usize>,
    resource_keys: Vec<(u32, u32, u32, u16)>,
    dock: Vec<DockInfo>,
}

#[derive(Clone, Copy, Default)]
struct DockInfo {
    value: u32,
    area: u32,
}

impl SnapshotScratch {
    #[cfg(test)]
    pub(crate) fn buffer_capacity_bytes(&self) -> usize {
        (self.parents.capacity() + self.pending.capacity() + self.grouped_children.capacity())
            * size_of::<u32>()
            + self.visited.capacity()
            + (self.child_counts.capacity()
                + self.child_offsets.capacity()
                + self.child_cursor.capacity()
                + self.op_counts.capacity()
                + self.op_offsets.capacity()
                + self.op_cursor.capacity())
                * size_of::<usize>()
            + self.resource_keys.capacity() * size_of::<(u32, u32, u32, u16)>()
            + self.dock.capacity() * size_of::<DockInfo>()
    }

    fn prepare_nodes(&mut self, node_len: usize, child_len: usize) {
        reset_vec(&mut self.parents, node_len, u32::MAX);
        reset_vec(&mut self.visited, node_len, 0);
        reset_vec(&mut self.child_counts, node_len, 0);
        reset_vec(&mut self.child_offsets, node_len + 1, 0);
        reset_vec(&mut self.child_cursor, node_len, 0);
        reset_vec(&mut self.grouped_children, child_len, 0);
        self.pending.clear();
    }

    fn prepare_ops(&mut self, node_len: usize) {
        reset_vec(&mut self.op_counts, node_len, 0);
        reset_vec(&mut self.op_offsets, node_len + 1, 0);
        reset_vec(&mut self.op_cursor, node_len, 0);
    }
}

/// Parses a comma-separated `tag=value` OpenType feature list, as written by the managed
/// font-features API. Tags are 1-4 ASCII letters or digits; values are decimal.
pub(crate) fn parse_font_features(text: &str) -> Option<FontFeatures> {
    let mut entries = Vec::new();
    for entry in text.split(',') {
        let (tag, value) = entry.split_once('=')?;
        if tag.is_empty() || tag.len() > 4 || !tag.bytes().all(|b| b.is_ascii_alphanumeric()) {
            return None;
        }
        entries.push((tag.to_string(), value.parse::<u32>().ok()?));
    }
    if entries.is_empty() {
        return None;
    }
    Some(FontFeatures(Arc::new(entries)))
}

/// Parses a comma-separated font fallback family list. Entries must be non-empty; commas
/// inside names are rejected by the managed API so the split is total.
pub(crate) fn parse_font_fallbacks(text: &str) -> Option<FontFallbacks> {
    let families: Vec<String> = text.split(',').map(str::to_string).collect();
    if families.iter().any(String::is_empty) {
        return None;
    }
    Some(FontFallbacks::from_fonts(families))
}

pub fn validate(arena: &RenderArena, root: u32) -> Result<(), i32> {
    validate_with_scratch(arena, root, &mut SnapshotScratch::default())
}

/// Virtualized lists and tables share one row-engine namespace per owner view, so a List and a
/// Table (or two of either) must not declare the same `(owner, key)`. Slider resources use a
/// separate kind namespace and are checked independently. Sorting reusable offset records
/// bounds comparisons without allocating or retaining key strings.
fn validate_resource_key_uniqueness(
    nodes: &[NodeRecord],
    ops: &[OpRecord],
    utf8: &[u8],
    scratch: &mut SnapshotScratch,
) -> Result<(), i32> {
    // Last OP_RESOURCE_OWNER per node wins, matching the materializer's lookup.
    let owners = &mut scratch.resource_keys;
    owners.clear();
    owners.resize(nodes.len(), (0, 0, 0, 0));
    for op in ops {
        if op.code == OP_RESOURCE_OWNER {
            let node = op.node as usize;
            owners[node].0 = op.a as u32;
        }
        if op.code == crate::semantic::OP_FOCUS_TARGET {
            let entry = &mut owners[op.node as usize];
            if entry.2 != 0 {
                return Err(-67);
            }
            entry.1 = op.a as u32;
            entry.2 = op.b as u32;
            entry.3 = crate::semantic::RESOURCE_FOCUS;
        }
        if op.code == crate::semantic::OP_FOCUS_TAB_STOP && owners[op.node as usize].3 == 0 {
            owners[op.node as usize].3 = crate::semantic::RESOURCE_FOCUS;
        }
    }

    let mut count = 0usize;
    for index in 0..nodes.len() {
        let node = &nodes[index];
        if owners[index].3 == crate::semantic::RESOURCE_FOCUS {
            if owners[index].0 == 0 || owners[index].2 == 0 {
                return Err(-67);
            }
            owners[count] = owners[index];
            count += 1;
            continue;
        }
        // A table's row-engine key is the first NUL-separated field of its data blob; a list's
        // key is the whole payload. The kind keeps Slider's separate resource namespace apart.
        let (kind, key_length) = match node.component {
            COMPONENT_LIST => (2, node.data_length),
            COMPONENT_SCROLL => (1, node.data_length),
            COMPONENT_INPUT => (
                3,
                utf8[node.data_offset as usize
                    ..node.data_offset as usize + node.data_length as usize]
                    .iter()
                    .position(|byte| *byte == 0)
                    .unwrap() as u32,
            ),
            COMPONENT_NATIVE_EXTENSION => (
                6,
                utf8[node.data_offset as usize
                    ..node.data_offset as usize + node.data_length as usize]
                    .iter()
                    .enumerate()
                    .filter(|(_, byte)| **byte == 0)
                    .nth(2)
                    .unwrap()
                    .0 as u32,
            ),
            COMPONENT_TABLE => (
                2,
                utf8[node.data_offset as usize
                    ..node.data_offset as usize + node.data_length as usize]
                    .iter()
                    .position(|byte| *byte == 0)
                    .map_or(node.data_length, |position| position as u32),
            ),
            COMPONENT_SLIDER => (4, node.data_length),
            COMPONENT_DOCK_AREA => (5, node.data_length),
            _ => continue,
        };
        let owner = owners[index].0;
        if owner == 0 || key_length == 0 {
            continue;
        }
        owners[count] = (owner, node.data_offset, key_length, kind);
        count += 1;
    }
    owners.truncate(count);
    if duplicate_keys(owners, utf8) {
        return Err(-56);
    }
    Ok(())
}

// Store offsets, never borrowed pointers or allocated strings. Resource namespaces and
// Dock areas both reduce to (owner/area, kind, UTF-8 key) uniqueness after validation.
fn duplicate_keys(keys: &mut [(u32, u32, u32, u16)], utf8: &[u8]) -> bool {
    let compare = |left: &(u32, u32, u32, u16), right: &(u32, u32, u32, u16)| {
        (left.0, left.3).cmp(&(right.0, right.3)).then_with(|| {
            utf8[left.1 as usize..left.1 as usize + left.2 as usize]
                .cmp(&utf8[right.1 as usize..right.1 as usize + right.2 as usize])
        })
    };
    keys.sort_unstable_by(compare);
    keys.windows(2)
        .any(|pair| compare(&pair[0], &pair[1]).is_eq())
}

fn validate_with_scratch(
    arena: &RenderArena,
    root: u32,
    scratch: &mut SnapshotScratch,
) -> Result<(), i32> {
    if !valid_len_cap(arena.node_length, arena.node_capacity)
        || !valid_len_cap(arena.op_length, arena.op_capacity)
        || !valid_len_cap(arena.child_length, arena.child_capacity)
        || !valid_len_cap(arena.utf8_length, arena.utf8_capacity)
    {
        return Err(-2);
    }

    let node_len = arena.node_length as usize;
    let op_len = arena.op_length as usize;
    let child_len = arena.child_length as usize;
    let utf8_len = arena.utf8_length as usize;

    if root as usize >= node_len {
        return Err(-3);
    }
    if (node_len != 0 && arena.nodes.is_null())
        || (op_len != 0 && arena.ops.is_null())
        || (child_len != 0 && arena.children.is_null())
        || (utf8_len != 0 && arena.utf8.is_null())
    {
        return Err(-4);
    }

    if !crate::pointer::valid(arena.nodes, node_len)
        || !crate::pointer::valid(arena.ops, op_len)
        || !crate::pointer::valid(arena.children, child_len)
        || !crate::pointer::valid(arena.utf8, utf8_len)
    {
        return Err(-4);
    }
    let nodes = unsafe { slice_or_empty(arena.nodes, node_len) };
    let ops = unsafe { slice_or_empty(arena.ops, op_len) };
    let children = unsafe { slice_or_empty(arena.children, child_len) };
    let utf8 = unsafe { slice_or_empty(arena.utf8, utf8_len) };

    if matches!(
        nodes[root as usize].component,
        COMPONENT_DOCK_SPLIT | COMPONENT_DOCK_TABS | COMPONENT_DOCK_PANEL | COMPONENT_DOCK_REGION
    ) {
        return Err(-61);
    }

    for node in nodes {
        if node.flags != 0 {
            return Err(-22);
        }
        let end = node.data_offset as u64 + node.data_length as u64;
        if end > utf8_len as u64 {
            return Err(-5);
        }
        let payload = &utf8[node.data_offset as usize..end as usize];
        if std::str::from_utf8(payload).is_err() {
            return Err(-8);
        }
        let Some(metadata) = component_metadata(node.component) else {
            return Err(-9);
        };
        if (metadata.data_kind == DataKind::None && node.data_length != 0)
            || (metadata.data_required && node.data_length == 0)
        {
            return Err(-14);
        }
        if node.component == COMPONENT_INPUT {
            let mut separators = payload.iter().enumerate().filter(|(_, byte)| **byte == 0);
            let Some((first_separator, _)) = separators.next() else {
                return Err(-31);
            };
            if first_separator == 0 || separators.next().is_none() || separators.next().is_some() {
                return Err(-31);
            }
        }
        if node.component == COMPONENT_SLIDER && payload.contains(&0) {
            return Err(-43);
        }
        if node.component == COMPONENT_DOCK_AREA && payload.contains(&0) {
            return Err(-61);
        }
        if node.component == COMPONENT_DOCK_PANEL {
            let mut fields = payload.split(|byte| *byte == 0);
            if fields.next().is_none_or(<[u8]>::is_empty)
                || fields.next().is_none()
                || fields.next().is_none_or(|field| !field.is_empty())
                || fields.next().is_some()
            {
                return Err(-61);
            }
        }
        if node.component == COMPONENT_NATIVE_EXTENSION && !crate::extension::valid_payload(payload)
        {
            return Err(-62);
        }
    }

    for op in ops {
        if op.node as usize >= node_len {
            return Err(-6);
        }
        let Some(metadata) = operation_metadata(op.code) else {
            return Err(-15);
        };
        if op.value_kind != metadata.value_kind as u16 {
            return Err(-16);
        }
        if op.b != 0 && !allows_payload(op.code) && metadata.value_kind != ValueKind::Data {
            return Err(-23);
        }
        if metadata.value_kind == ValueKind::Data {
            let end = op.a.checked_add(op.b).unwrap_or(u64::MAX);
            if op.b == 0 {
                return Err(-63);
            }
            if end > utf8_len as u64 {
                return Err(-5);
            }
            let payload = &utf8[op.a as usize..end as usize];
            if payload.contains(&0) || std::str::from_utf8(payload).is_err() {
                return Err(-8);
            }
            if matches!(op.code, OP_FONT_FEATURES | OP_FONT_FALLBACKS) {
                let text = std::str::from_utf8(payload).unwrap_or_default();
                let valid = if op.code == OP_FONT_FEATURES {
                    parse_font_features(text).is_some()
                } else {
                    parse_font_fallbacks(text).is_some()
                };
                if !valid {
                    return Err(-64);
                }
            }
        }
        match metadata.value_kind {
            ValueKind::None if op.a != 0 => return Err(-24),
            ValueKind::F32 | ValueKind::U32 if op.a >> 32 != 0 => return Err(-24),
            _ => {}
        }
        if !metadata.applies_to(nodes[op.node as usize].component) {
            return Err(-17);
        }
        if metadata.value_kind == ValueKind::F32 && !f32::from_bits(op.a as u32).is_finite() {
            return Err(-18);
        }
        if metadata.value_kind == ValueKind::F32x2
            && (!f32::from_bits(op.a as u32).is_finite()
                || !f32::from_bits((op.a >> 32) as u32).is_finite())
        {
            return Err(-18);
        }
        if matches!(op.code, OP_DRAWING_VIEW_BOX_SIZE | OP_PATH_ARC_RADII)
            && (f32::from_bits(op.a as u32) <= 0.0 || f32::from_bits((op.a >> 32) as u32) <= 0.0)
        {
            return Err(-59);
        }
        if metadata.value_kind == ValueKind::Callback && op.a == 0 {
            return Err(-19);
        }
        let payload_error = payload_error(op.code, op.a, op.b);
        if payload_error != 0 {
            return Err(payload_error);
        }
    }

    scratch.prepare_nodes(node_len, child_len);
    let has_dock = nodes.iter().any(|node| {
        matches!(
            node.component,
            COMPONENT_DOCK_AREA
                | COMPONENT_DOCK_SPLIT
                | COMPONENT_DOCK_TABS
                | COMPONENT_DOCK_PANEL
                | COMPONENT_DOCK_REGION
        )
    });
    scratch.dock.clear();
    if has_dock {
        scratch.dock.resize(node_len, DockInfo::default());
        for op in ops {
            if matches!(op.code, OP_DOCK_ACTIVE_INDEX | OP_DOCK_REGION_SIDE) {
                scratch.dock[op.node as usize].value = op.a as u32;
            }
        }
    }
    for edge in children {
        if edge.parent as usize >= node_len || edge.child as usize >= node_len {
            return Err(-7);
        }
        if scratch.parents[edge.child as usize] != u32::MAX {
            return Err(-10);
        }
        scratch.parents[edge.child as usize] = edge.parent;

        let parent_component = nodes[edge.parent as usize].component;
        let child_component = nodes[edge.child as usize].component;
        if (parent_component == COMPONENT_DRAWING) != (child_component == COMPONENT_PATH) {
            return Err(-58);
        }
        let valid_dock_edge = match parent_component {
            COMPONENT_DOCK_AREA => matches!(
                child_component,
                COMPONENT_DOCK_SPLIT | COMPONENT_DOCK_TABS | COMPONENT_DOCK_REGION
            ),
            COMPONENT_DOCK_SPLIT | COMPONENT_DOCK_REGION => {
                matches!(child_component, COMPONENT_DOCK_SPLIT | COMPONENT_DOCK_TABS)
            }
            COMPONENT_DOCK_TABS => child_component == COMPONENT_DOCK_PANEL,
            _ => !matches!(
                child_component,
                COMPONENT_DOCK_SPLIT
                    | COMPONENT_DOCK_TABS
                    | COMPONENT_DOCK_PANEL
                    | COMPONENT_DOCK_REGION
            ),
        };
        if !valid_dock_edge {
            return Err(-61);
        }
        if !component_metadata(parent_component)
            .expect("node components were validated above")
            .allows_children
        {
            return Err(-20);
        }
        scratch.child_counts[edge.parent as usize] += 1;
    }

    if nodes.iter().enumerate().any(|(index, node)| {
        node.component == COMPONENT_OVERLAY && scratch.child_counts[index] != 1
    }) {
        return Err(-32);
    }
    if nodes
        .iter()
        .enumerate()
        .any(|(index, node)| node.component == COMPONENT_TABLE && scratch.child_counts[index] != 0)
    {
        // Reuse decode scratch only when custom headers need structural validation.
        reset_vec(&mut scratch.op_counts, node_len, 0);
        for op in ops {
            if op.code == OP_TABLE_COLUMN {
                scratch.op_counts[op.node as usize] += 1;
            }
        }
        if nodes.iter().enumerate().any(|(index, node)| {
            node.component == COMPONENT_TABLE
                && scratch.child_counts[index] != 0
                && scratch.child_counts[index] != scratch.op_counts[index]
        }) {
            return Err(-57);
        }
    }
    if nodes.iter().enumerate().any(|(index, node)| {
        node.component == COMPONENT_TOOLTIP && scratch.child_counts[index] != 2
    }) {
        return Err(-38);
    }
    if nodes.iter().enumerate().any(|(index, node)| {
        node.component == COMPONENT_CONTEXT_MENU && scratch.child_counts[index] != 2
    }) {
        return Err(-41);
    }
    if nodes.iter().enumerate().any(|(index, node)| {
        node.component == COMPONENT_POPOVER_MENU && scratch.child_counts[index] != 2
    }) {
        return Err(-42);
    }
    if nodes.iter().enumerate().any(|(index, node)| {
        node.component == COMPONENT_DYNAMIC && scratch.child_counts[index] != 1
    }) {
        return Err(-60);
    }
    if nodes.iter().enumerate().any(|(index, node)| {
        (matches!(node.component, COMPONENT_DOCK_PANEL | COMPONENT_DOCK_REGION)
            && scratch.child_counts[index] != 1)
            || (node.component == COMPONENT_DOCK_AREA
                && !(1..=4).contains(&scratch.child_counts[index]))
            || (matches!(node.component, COMPONENT_DOCK_SPLIT | COMPONENT_DOCK_TABS)
                && scratch.child_counts[index] == 0)
    }) {
        return Err(-61);
    }
    prefix_offsets(&scratch.child_counts, &mut scratch.child_offsets);
    scratch
        .child_cursor
        .copy_from_slice(&scratch.child_offsets[..node_len]);
    for edge in children {
        let parent = edge.parent as usize;
        let destination = scratch.child_cursor[parent];
        scratch.grouped_children[destination] = edge.child;
        scratch.child_cursor[parent] += 1;
    }

    for (index, node) in nodes.iter().enumerate() {
        if node.component != COMPONENT_DOCK_TABS {
            continue;
        }
        let active_index = scratch.dock[index].value as usize;
        if active_index >= scratch.child_counts[index] {
            return Err(-61);
        }
    }
    for (index, node) in nodes.iter().enumerate() {
        if node.component != COMPONENT_DOCK_AREA {
            continue;
        }
        let mut center_count = 0usize;
        let mut side_mask = 0u32;
        let start = scratch.child_offsets[index];
        let end = start + scratch.child_counts[index];
        for &child in &scratch.grouped_children[start..end] {
            if nodes[child as usize].component != COMPONENT_DOCK_REGION {
                center_count += 1;
                continue;
            }
            let side = scratch.dock[child as usize].value;
            let bit = 1u32 << side;
            if side_mask & bit != 0 {
                return Err(-61);
            }
            side_mask |= bit;
        }
        if center_count != 1 {
            return Err(-61);
        }
    }
    if nodes
        .iter()
        .enumerate()
        .any(|(index, node)| node.component == COMPONENT_PATH && scratch.parents[index] == u32::MAX)
    {
        return Err(-58);
    }

    if scratch.parents[root as usize] != u32::MAX {
        return Err(-11);
    }

    scratch.pending.push(root);
    while let Some(parent) = scratch.pending.pop() {
        let visited = &mut scratch.visited[parent as usize];
        if *visited != 0 {
            return Err(-12);
        }
        *visited = 1;
        if has_dock {
            let ancestor = scratch.parents[parent as usize];
            scratch.dock[parent as usize].area =
                if nodes[parent as usize].component == COMPONENT_DOCK_AREA {
                    parent
                } else if ancestor == u32::MAX {
                    u32::MAX
                } else {
                    scratch.dock[ancestor as usize].area
                };
        }
        let start = scratch.child_offsets[parent as usize];
        let end = start + scratch.child_counts[parent as usize];
        scratch
            .pending
            .extend_from_slice(&scratch.grouped_children[start..end]);
    }

    if scratch.visited.contains(&0) {
        return Err(-13);
    }

    validate_resource_key_uniqueness(nodes, ops, utf8, scratch)?;

    if has_dock {
        let panels = &mut scratch.resource_keys;
        panels.clear();
        for (index, node) in nodes.iter().enumerate() {
            if node.component == COMPONENT_DOCK_PANEL {
                panels.push((
                    scratch.dock[index].area,
                    node.data_offset,
                    dock_panel_id(node, utf8).len() as u32,
                    0,
                ));
            }
        }
        if duplicate_keys(panels, utf8) {
            return Err(-61);
        }
    }

    Ok(())
}

fn dock_panel_id<'a>(node: &NodeRecord, utf8: &'a [u8]) -> &'a [u8] {
    let payload =
        &utf8[node.data_offset as usize..node.data_offset as usize + node.data_length as usize];
    &payload[..payload.iter().position(|byte| *byte == 0).unwrap_or(0)]
}

fn prefix_offsets(counts: &[usize], offsets: &mut [usize]) {
    debug_assert_eq!(offsets.len(), counts.len() + 1);
    offsets[0] = 0;
    for (index, count) in counts.iter().enumerate() {
        offsets[index + 1] = offsets[index] + count;
    }
}

fn reset_vec<T: Clone>(values: &mut Vec<T>, len: usize, value: T) {
    values.clear();
    values.resize(len, value);
}

fn valid_len_cap(len: i32, cap: i32) -> bool {
    len >= 0 && cap >= 0 && len <= cap
}

unsafe fn slice_or_empty<'a, T>(pointer: *const T, len: usize) -> &'a [T] {
    if len == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(pointer, len) }
    }
}

#[cfg(test)]
mod tests {
    mod validation_workloads;
    use super::*;
    use crate::{
        abi::{ChildRecord, NodeRecord, OpRecord},
        semantic::{
            COMPONENT_CHECKBOX, COMPONENT_CONTEXT_MENU, COMPONENT_DIV, COMPONENT_DOCK_AREA,
            COMPONENT_DOCK_PANEL, COMPONENT_DOCK_TABS, COMPONENT_DRAWING, COMPONENT_IMAGE,
            COMPONENT_INPUT, COMPONENT_LIST, COMPONENT_OVERLAY, COMPONENT_PATH,
            COMPONENT_POPOVER_MENU, COMPONENT_TEXT, COMPONENT_TOOLTIP, OP_CHECKED, OP_DISABLED,
            OP_DOCK_ACTIVE_INDEX, OP_DRAWING_VIEW_BOX_SIZE, OP_GAP_PX, OP_IMAGE_OBJECT_FIT,
            OP_INPUT_DISABLED, OP_LIST_ITEM_ID, OP_ON_CLICK, OP_OVERLAY_PLACEMENT, OP_PADDING_PX,
            OP_SCROLLBAR_GUTTER, OP_SCROLLBAR_WIDTH, OP_TOOLTIP_PLACEMENT, OP_WINDOW_CONTROL_AREA,
        },
    };

    fn arena_with(node: &mut NodeRecord, operation: Option<&mut OpRecord>) -> RenderArena {
        let (ops, op_length) = operation.map_or((std::ptr::null_mut(), 0), |operation| {
            (operation as *mut OpRecord, 1)
        });

        RenderArena {
            nodes: node,
            node_length: 1,
            node_capacity: 1,
            ops,
            op_length,
            op_capacity: op_length,
            children: std::ptr::null_mut(),
            child_length: 0,
            child_capacity: 0,
            utf8: std::ptr::null_mut(),
            utf8_length: 0,
            utf8_capacity: 0,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        }
    }

    #[test]
    fn rejects_unknown_components() {
        let mut node = NodeRecord {
            component: 99,
            ..Default::default()
        };
        let arena = arena_with(&mut node, None);
        assert_eq!(validate(&arena, 0), Err(-9));
    }

    #[test]
    fn rejects_reserved_node_flags() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            flags: 1,
            ..Default::default()
        };
        let arena = arena_with(&mut node, None);
        assert_eq!(validate(&arena, 0), Err(-22));
    }

    #[test]
    fn rejects_noncanonical_operation_payloads() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_GAP_PX,
            value_kind: ValueKind::F32 as u16,
            a: (1u64 << 32) | 1.0f32.to_bits() as u64,
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-24));

        operation.a = 1.0f32.to_bits() as u64;
        operation.b = 1;
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-23));
    }

    #[test]
    fn rejects_malformed_data_operations() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut payload = b"Inter".to_vec();
        let mut operation = OpRecord {
            code: crate::semantic::OP_FONT_FAMILY,
            value_kind: ValueKind::Data as u16,
            b: payload.len() as u64,
            ..Default::default()
        };
        let arena =
            |node: &mut NodeRecord, operation: &mut OpRecord, payload: &mut Vec<u8>| RenderArena {
                nodes: node,
                node_length: 1,
                node_capacity: 1,
                ops: operation,
                op_length: 1,
                op_capacity: 1,
                children: std::ptr::null_mut(),
                child_length: 0,
                child_capacity: 0,
                utf8: payload.as_mut_ptr(),
                utf8_length: payload.len() as i32,
                utf8_capacity: payload.len() as i32,
                generation: 1,
                flags: 0,
                required_node_capacity: 0,
                required_op_capacity: 0,
                required_child_capacity: 0,
                required_utf8_capacity: 0,
            };
        assert!(validate(&arena(&mut node, &mut operation, &mut payload), 0).is_ok());

        operation.b = 0;
        assert_eq!(
            validate(&arena(&mut node, &mut operation, &mut payload), 0),
            Err(-63)
        );

        operation.b = payload.len() as u64 + 1;
        assert_eq!(
            validate(&arena(&mut node, &mut operation, &mut payload), 0),
            Err(-5)
        );

        operation.b = payload.len() as u64;
        payload[1] = 0;
        assert_eq!(
            validate(&arena(&mut node, &mut operation, &mut payload), 0),
            Err(-8)
        );
    }

    #[test]
    fn rejects_operation_value_kind_mismatches() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_GAP_PX,
            value_kind: ValueKind::None as u16,
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-16));
    }

    #[test]
    fn rejects_non_finite_coordinate_pairs() {
        let mut node = NodeRecord {
            component: COMPONENT_DRAWING,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_DRAWING_VIEW_BOX_SIZE,
            value_kind: ValueKind::F32x2 as u16,
            a: f32::NAN.to_bits() as u64 | ((1.0f32.to_bits() as u64) << 32),
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-18));
    }

    #[test]
    fn path_nodes_require_a_drawing_parent() {
        let mut node = NodeRecord {
            component: COMPONENT_PATH,
            ..Default::default()
        };
        let arena = arena_with(&mut node, None);
        assert_eq!(validate(&arena, 0), Err(-58));

        let mut nodes = [
            NodeRecord {
                component: COMPONENT_DRAWING,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut child = ChildRecord {
            parent: 0,
            child: 1,
        };
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: 2,
            node_capacity: 2,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: &mut child,
            child_length: 1,
            child_capacity: 1,
            utf8: std::ptr::null_mut(),
            utf8_length: 0,
            utf8_capacity: 0,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };
        assert_eq!(validate(&arena, 0), Err(-58));
    }

    #[test]
    fn rejects_operations_on_incompatible_components() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_ON_CLICK,
            value_kind: ValueKind::Callback as u16,
            a: 1,
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-17));
    }

    #[test]
    fn validates_scrollbar_gutter_and_width_values() {
        // Both ops require the native_state capability, so anchor them on a list node with
        // valid resource-key data.
        let mut identifier = b'k';
        let mut node = NodeRecord {
            component: COMPONENT_LIST,
            data_length: 1,
            ..Default::default()
        };
        let mut gutter = OpRecord {
            code: OP_SCROLLBAR_GUTTER,
            value_kind: ValueKind::U32 as u16,
            a: 2,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut gutter));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-21));

        let mut width = OpRecord {
            code: OP_SCROLLBAR_WIDTH,
            value_kind: ValueKind::U64 as u16,
            a: 12.0f32.to_bits() as u64,
            ..Default::default()
        };
        // F32 is the declared kind for scrollbar_width; a wrong kind is rejected.
        let mut arena = arena_with(&mut node, Some(&mut width));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-16));

        width.value_kind = ValueKind::F32 as u16;
        width.a = 8.0f32.to_bits() as u64;
        let mut arena = arena_with(&mut node, Some(&mut width));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Ok(()));

        width.a = 40.0f32.to_bits() as u64;
        let mut arena = arena_with(&mut node, Some(&mut width));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-29));
    }

    #[test]
    fn accepts_list_item_id_on_styled_components() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_LIST_ITEM_ID,
            value_kind: ValueKind::U64 as u16,
            a: (1_u64 << 40) | 42,
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Ok(()));

        // The reserved ID 0 is treated as "no ID" by the list materializer.
        operation.a = 0;
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Ok(()));

        // Value kind must still match the registry.
        operation.value_kind = ValueKind::U32 as u16;
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-16));
    }

    #[test]
    fn rejects_invalid_boolean_values() {
        let mut identifier = b'x';
        let mut node = NodeRecord {
            component: COMPONENT_CHECKBOX,
            data_length: 1,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_CHECKED,
            value_kind: ValueKind::U32 as u16,
            a: 2,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-21));

        operation.code = OP_DISABLED;
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = &mut identifier;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-21));
    }

    #[test]
    fn rejects_invalid_window_control_area() {
        let mut node = NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_WINDOW_CONTROL_AREA,
            value_kind: ValueKind::U32 as u16,
            a: 4,
            ..Default::default()
        };
        let arena = arena_with(&mut node, Some(&mut operation));
        assert_eq!(validate(&arena, 0), Err(-39));
    }

    #[test]
    fn rejects_invalid_image_fit() {
        let mut path = b'x';
        let mut node = NodeRecord {
            component: COMPONENT_IMAGE,
            data_length: 1,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_IMAGE_OBJECT_FIT,
            value_kind: ValueKind::U32 as u16,
            a: 5,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = &mut path;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;
        assert_eq!(validate(&arena, 0), Err(-30));
    }

    #[test]
    fn rejects_malformed_input_configuration() {
        let mut payload = *b"key\0missing-placeholder";
        let mut node = NodeRecord {
            component: COMPONENT_INPUT,
            data_length: payload.len() as u32,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, None);
        arena.utf8 = payload.as_mut_ptr();
        arena.utf8_length = payload.len() as i32;
        arena.utf8_capacity = payload.len() as i32;

        assert_eq!(validate(&arena, 0), Err(-31));
    }

    #[test]
    fn rejects_invalid_input_boolean() {
        let mut payload = *b"key\0\0placeholder";
        let mut node = NodeRecord {
            component: COMPONENT_INPUT,
            data_length: payload.len() as u32,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_INPUT_DISABLED,
            value_kind: ValueKind::U32 as u16,
            a: 2,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = payload.as_mut_ptr();
        arena.utf8_length = payload.len() as i32;
        arena.utf8_capacity = payload.len() as i32;

        assert_eq!(validate(&arena, 0), Err(-21));
    }

    #[test]
    fn rejects_overlay_without_exactly_one_child() {
        let mut key = b'x';
        let mut node = NodeRecord {
            component: COMPONENT_OVERLAY,
            data_length: 1,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, None);
        arena.utf8 = &mut key;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;

        assert_eq!(validate(&arena, 0), Err(-32));
    }

    #[test]
    fn rejects_invalid_overlay_placement() {
        let mut key = b'x';
        let mut node = NodeRecord {
            component: COMPONENT_OVERLAY,
            data_length: 1,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_OVERLAY_PLACEMENT,
            value_kind: ValueKind::U32 as u16,
            a: 9,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = &mut key;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;

        assert_eq!(validate(&arena, 0), Err(-33));
    }

    #[test]
    fn rejects_tooltip_without_exactly_two_children() {
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_TOOLTIP,
                data_length: 1,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut child = ChildRecord {
            parent: 0,
            child: 1,
        };
        let mut key = b'x';
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: &mut child,
            child_length: 1,
            child_capacity: 1,
            utf8: &mut key,
            utf8_length: 1,
            utf8_capacity: 1,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };

        assert_eq!(validate(&arena, 0), Err(-38));
    }

    #[test]
    fn rejects_context_menu_without_exactly_two_children() {
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_CONTEXT_MENU,
                data_length: 1,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut child = ChildRecord {
            parent: 0,
            child: 1,
        };
        let mut key = b'x';
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: &mut child,
            child_length: 1,
            child_capacity: 1,
            utf8: &mut key,
            utf8_length: 1,
            utf8_capacity: 1,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };

        assert_eq!(validate(&arena, 0), Err(-41));
    }

    #[test]
    fn rejects_popover_menu_without_exactly_two_children() {
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_POPOVER_MENU,
                data_length: 1,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut child = ChildRecord {
            parent: 0,
            child: 1,
        };
        let mut key = b'x';
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: &mut child,
            child_length: 1,
            child_capacity: 1,
            utf8: &mut key,
            utf8_length: 1,
            utf8_capacity: 1,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };

        assert_eq!(validate(&arena, 0), Err(-42));
    }

    #[test]
    fn rejects_invalid_tooltip_placement() {
        let mut key = b'x';
        let mut node = NodeRecord {
            component: COMPONENT_TOOLTIP,
            data_length: 1,
            ..Default::default()
        };
        let mut operation = OpRecord {
            code: OP_TOOLTIP_PLACEMENT,
            value_kind: ValueKind::U32 as u16,
            a: 5,
            ..Default::default()
        };
        let mut arena = arena_with(&mut node, Some(&mut operation));
        arena.utf8 = &mut key;
        arena.utf8_length = 1;
        arena.utf8_capacity = 1;

        assert_eq!(validate(&arena, 0), Err(-35));
    }

    #[test]
    fn rejects_dynamic_without_exactly_one_child() {
        let mut node = NodeRecord {
            component: COMPONENT_DYNAMIC,
            ..Default::default()
        };
        let arena = arena_with(&mut node, None);

        assert_eq!(validate(&arena, 0), Err(-60));
    }

    #[test]
    fn validates_dock_structure_and_rejects_duplicate_panel_ids() {
        let mut utf8 = b"dockeditor\0Editor\0editor\0Preview\0third\0Third\0".to_vec();
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_DOCK_AREA,
                data_length: 4,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_TABS,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_PANEL,
                data_offset: 4,
                data_length: 14,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_REGION,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_TABS,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_PANEL,
                data_offset: 18,
                data_length: 15,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_REGION,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_TABS,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DOCK_PANEL,
                data_offset: 33,
                data_length: 12,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
        ];
        let mut children = [
            ChildRecord {
                parent: 0,
                child: 1,
            },
            ChildRecord {
                parent: 1,
                child: 2,
            },
            ChildRecord {
                parent: 2,
                child: 3,
            },
            ChildRecord {
                parent: 0,
                child: 4,
            },
            ChildRecord {
                parent: 4,
                child: 5,
            },
            ChildRecord {
                parent: 5,
                child: 6,
            },
            ChildRecord {
                parent: 6,
                child: 7,
            },
            ChildRecord {
                parent: 0,
                child: 8,
            },
            ChildRecord {
                parent: 8,
                child: 9,
            },
            ChildRecord {
                parent: 9,
                child: 10,
            },
            ChildRecord {
                parent: 10,
                child: 11,
            },
        ];
        let mut arena = RenderArena {
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

        assert_eq!(validate(&arena, 1), Err(-61));
        assert_eq!(validate(&arena, 0), Err(-61));

        utf8[18] = b'p';
        assert_eq!(validate(&arena, 0), Err(-61));

        let mut operations = [
            OpRecord {
                node: 8,
                code: OP_DOCK_REGION_SIDE,
                value_kind: ValueKind::U32 as u16,
                a: 1,
                ..Default::default()
            },
            OpRecord {
                node: 1,
                code: OP_DOCK_ACTIVE_INDEX,
                value_kind: ValueKind::U32 as u16,
                a: 2,
                ..Default::default()
            },
        ];
        arena.ops = operations.as_mut_ptr();
        arena.op_length = 1;
        arena.op_capacity = 1;
        assert_eq!(validate(&arena, 0), Ok(()));

        arena.op_length = 2;
        arena.op_capacity = 2;
        assert_eq!(validate(&arena, 0), Err(-61));
    }

    /// Builds a DIV root owning the given virtualized-resource nodes; each resource node gets
    /// one OP_RESOURCE_OWNER op addressed to `owner`. `data_length` on each node selects its
    /// key slice from the shared utf8 buffer (a table's key ends at its embedded NUL).
    fn resource_collision_arena(
        nodes: &mut [NodeRecord],
        utf8: &mut [u8],
        owners: &mut [OpRecord],
    ) -> (RenderArena, Vec<ChildRecord>) {
        for (slot, op) in owners.iter_mut().enumerate() {
            op.node = slot as u32 + 1;
            op.code = OP_RESOURCE_OWNER;
            op.value_kind = ValueKind::U32 as u16;
            op.a = 4;
        }
        let mut children = Vec::new();
        for index in 1..nodes.len() {
            children.push(ChildRecord {
                parent: 0,
                child: index as u32,
            });
        }
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: owners.as_mut_ptr(),
            op_length: owners.len() as i32,
            op_capacity: owners.len() as i32,
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
        (arena, children)
    }

    #[test]
    fn disconnected_dock_cycles_fail_before_ancestor_queries() {
        for two_nodes in [false, true] {
            let mut nodes = [
                COMPONENT_DIV,
                COMPONENT_DOCK_SPLIT,
                COMPONENT_DOCK_TABS,
                COMPONENT_DOCK_PANEL,
                COMPONENT_DIV,
                COMPONENT_DOCK_SPLIT,
            ]
            .map(|component| NodeRecord {
                component,
                ..Default::default()
            });
            let mut data = *b"p\0Panel\0";
            nodes[3].data_length = data.len() as u32;
            let mut arena = arena_with(&mut nodes[0], None);
            let mut children = vec![
                ChildRecord {
                    parent: 1,
                    child: 2,
                },
                ChildRecord {
                    parent: 2,
                    child: 3,
                },
                ChildRecord {
                    parent: 3,
                    child: 4,
                },
            ];
            if two_nodes {
                children.extend([
                    ChildRecord {
                        parent: 1,
                        child: 5,
                    },
                    ChildRecord {
                        parent: 5,
                        child: 1,
                    },
                ]);
            } else {
                children.push(ChildRecord {
                    parent: 1,
                    child: 1,
                });
            }
            arena.nodes = nodes.as_mut_ptr();
            arena.node_length = if two_nodes { 6 } else { 5 };
            arena.node_capacity = 6;
            arena.children = children.as_mut_ptr();
            arena.child_length = children.len() as i32;
            arena.child_capacity = children.capacity() as i32;
            arena.utf8 = data.as_mut_ptr();
            arena.utf8_length = data.len() as i32;
            arena.utf8_capacity = data.len() as i32;
            assert_eq!(validate(&arena, 0), Err(-13));
        }
    }

    #[test]
    fn input_scroll_and_extension_keys_are_unique_per_owner() {
        for (component, data) in [
            (COMPONENT_INPUT, "key\0value\0placeholder"),
            (COMPONENT_SCROLL, "key"),
            (
                COMPONENT_NATIVE_EXTENSION,
                "editor\0document\0key\x001\x000000000000000001\0config",
            ),
        ] {
            let mut utf8 = data.as_bytes().to_vec();
            let mut nodes = [
                NodeRecord {
                    component: COMPONENT_DIV,
                    ..Default::default()
                },
                NodeRecord {
                    component,
                    data_length: utf8.len() as u32,
                    ..Default::default()
                },
                NodeRecord {
                    component,
                    data_length: utf8.len() as u32,
                    ..Default::default()
                },
            ];
            let mut owners = [OpRecord::default(); 2];
            let (arena, _children) = resource_collision_arena(&mut nodes, &mut utf8, &mut owners);
            assert_eq!(validate(&arena, 0), Err(-56));
            owners[1].a = 5;
            std::hint::black_box(&owners);
            assert_eq!(validate(&arena, 0), Ok(()));
        }
    }

    #[test]
    fn rejects_list_and_table_sharing_a_resource_key() {
        let mut utf8 = b"grid\0name\x1FName\x1F120\x1F0\x1F0".to_vec();
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_LIST,
                data_length: 4,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_TABLE,
                data_length: utf8.len() as u32,
                ..Default::default()
            },
        ];
        let mut owners = [OpRecord::default(), OpRecord::default()];
        let (arena, _children) = resource_collision_arena(&mut nodes, &mut utf8, &mut owners);
        assert_eq!(validate(&arena, 0), Err(-56));
    }

    #[test]
    fn rejects_two_lists_sharing_a_resource_key() {
        let mut utf8 = b"grid".to_vec();
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_LIST,
                data_length: 4,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_LIST,
                data_length: 4,
                ..Default::default()
            },
        ];
        let mut owners = [OpRecord::default(), OpRecord::default()];
        let (arena, _children) = resource_collision_arena(&mut nodes, &mut utf8, &mut owners);
        assert_eq!(validate(&arena, 0), Err(-56));
    }

    #[test]
    fn rejects_nodes_unreachable_from_the_root() {
        let mut nodes = [NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        }; 2];
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: 2,
            node_capacity: 2,
            ops: std::ptr::null_mut(),
            op_length: 0,
            op_capacity: 0,
            children: std::ptr::null_mut(),
            child_length: 0,
            child_capacity: 0,
            utf8: std::ptr::null_mut(),
            utf8_length: 0,
            utf8_capacity: 0,
            generation: 1,
            flags: 0,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };
        assert_eq!(validate(&arena, 0), Err(-13));
    }

    #[test]
    fn decode_groups_children_and_operations_into_flat_ranges() {
        let mut nodes = [
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_DIV,
                ..Default::default()
            },
            NodeRecord {
                component: COMPONENT_TEXT,
                ..Default::default()
            },
        ];
        let mut operations = [
            OpRecord {
                node: 2,
                code: OP_GAP_PX,
                value_kind: ValueKind::F32 as u16,
                a: 1.0f32.to_bits() as u64,
                ..Default::default()
            },
            OpRecord {
                node: 0,
                code: OP_GAP_PX,
                value_kind: ValueKind::F32 as u16,
                a: 2.0f32.to_bits() as u64,
                ..Default::default()
            },
            OpRecord {
                node: 0,
                code: OP_PADDING_PX,
                value_kind: ValueKind::F32 as u16,
                a: 3.0f32.to_bits() as u64,
                ..Default::default()
            },
        ];
        let mut children = [
            ChildRecord {
                parent: 0,
                child: 2,
            },
            ChildRecord {
                parent: 0,
                child: 1,
            },
        ];
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: operations.as_mut_ptr(),
            op_length: operations.len() as i32,
            op_capacity: operations.len() as i32,
            children: children.as_mut_ptr(),
            child_length: children.len() as i32,
            child_capacity: children.len() as i32,
            utf8: std::ptr::null_mut(),
            utf8_length: 0,
            utf8_capacity: 0,
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

        assert_eq!(snapshot.children(&snapshot.nodes[0]), &[2, 1]);
        assert_eq!(
            snapshot
                .ops(&snapshot.nodes[0])
                .iter()
                .map(|operation| operation.code)
                .collect::<Vec<_>>(),
            vec![OP_GAP_PX, OP_PADDING_PX]
        );
        assert_eq!(snapshot.ops(&snapshot.nodes[2])[0].code, OP_GAP_PX);
    }

    #[test]
    fn font_list_payloads_parse_strictly() {
        let features = parse_font_features("liga=1,smcp=0").expect("valid features");
        assert_eq!(
            features.tag_value_list(),
            &[("liga".to_string(), 1), ("smcp".to_string(), 0)]
        );
        assert!(parse_font_features("").is_none());
        assert!(parse_font_features("liga").is_none());
        assert!(parse_font_features("liga=").is_none());
        assert!(parse_font_features("toolong=1").is_none());
        assert!(parse_font_features("li ga=1").is_none());
        assert!(parse_font_features("liga=x").is_none());
        assert!(parse_font_features("liga=1,").is_none());

        let fallbacks = parse_font_fallbacks("Georgia,Arial").expect("valid fallbacks");
        assert_eq!(
            fallbacks.fallback_list(),
            &["Georgia".to_string(), "Arial".to_string()]
        );
        assert!(parse_font_fallbacks("").is_none());
        assert!(parse_font_fallbacks("Georgia,").is_none());
    }

    #[test]
    fn decodes_data_operations_into_retained_strings() {
        let mut nodes = [NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        }];
        let mut operations = [OpRecord {
            code: crate::semantic::OP_FONT_FAMILY,
            value_kind: ValueKind::Data as u16,
            a: 0,
            b: 5,
            ..Default::default()
        }];
        let mut utf8 = b"Inter".to_vec();
        let arena = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: nodes.len() as i32,
            node_capacity: nodes.len() as i32,
            ops: operations.as_mut_ptr(),
            op_length: operations.len() as i32,
            op_capacity: operations.len() as i32,
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
        snapshot
            .decode_into(
                &arena,
                0,
                &mut RetainedStrings::default(),
                &mut SnapshotScratch::default(),
            )
            .unwrap();

        assert_eq!(
            snapshot
                .last_data_op(&snapshot.nodes[0], crate::semantic::OP_FONT_FAMILY)
                .as_deref(),
            Some("Inter")
        );
        assert_eq!(snapshot.last_data_op(&snapshot.nodes[0], OP_GAP_PX), None);
    }

    #[test]
    fn retains_string_capacity_and_reuses_consecutive_values() {
        const VALUE: &str = "a retained payload longer than inline string storage";
        let mut strings = RetainedStrings::default();
        strings.begin_snapshot();
        let first = strings.intern(VALUE);
        strings.begin_snapshot();
        let second = strings.intern(VALUE);
        assert_eq!(first.as_str().as_ptr(), second.as_str().as_ptr());

        strings.begin_snapshot();
        let _replacement = strings.intern("replacement");
        strings.begin_snapshot();
        let after_eviction = strings.intern(VALUE);
        assert_ne!(first.as_str().as_ptr(), after_eviction.as_str().as_ptr());
    }

    #[test]
    fn data_operation_storage_is_lazy_and_cleared_across_decode_transitions() {
        use crate::semantic::OP_FONT_FAMILY;
        let mut nodes = [NodeRecord {
            component: COMPONENT_DIV,
            ..Default::default()
        }];
        let numeric = OpRecord {
            code: OP_GAP_PX,
            value_kind: ValueKind::F32 as u16,
            a: 4f32.to_bits() as u64,
            ..Default::default()
        };
        let font = OpRecord {
            code: OP_FONT_FAMILY,
            value_kind: ValueKind::Data as u16,
            a: 0,
            b: 5,
            ..Default::default()
        };
        let mut ops = [numeric, font, numeric, OpRecord { a: 5, b: 7, ..font }];
        let mut utf8 = b"InterGeorgia".to_vec();
        let mut arena: RenderArena = unsafe { std::mem::zeroed() };
        arena.nodes = nodes.as_mut_ptr();
        arena.node_length = 1;
        arena.node_capacity = 1;
        arena.ops = ops.as_mut_ptr();
        arena.op_length = 1;
        arena.op_capacity = ops.len() as i32;
        arena.utf8 = utf8.as_mut_ptr();
        arena.utf8_length = utf8.len() as i32;
        arena.utf8_capacity = utf8.len() as i32;
        arena.generation = 1;
        let mut snapshot = ValidatedSnapshot::default();
        let mut strings = RetainedStrings::default();
        let mut scratch = SnapshotScratch::default();

        snapshot
            .decode_into(&arena, 0, &mut strings, &mut scratch)
            .unwrap();
        assert_eq!(snapshot.op_data.capacity(), 0);
        for _ in 0..3 {
            arena.op_length = 4;
            snapshot
                .decode_into(&arena, 0, &mut strings, &mut scratch)
                .unwrap();
            let retained = snapshot
                .last_data_op(&snapshot.nodes[0], OP_FONT_FAMILY)
                .unwrap();
            assert_eq!(retained.as_ref(), "Georgia");
            // Borrowed output may be reused immediately; snapshot strings remain owned.
            utf8.fill(b'x');
            assert_eq!(retained.as_ref(), "Georgia");
            arena.op_length = 1;
            snapshot
                .decode_into(&arena, 0, &mut strings, &mut scratch)
                .unwrap();
            assert!(snapshot.op_data.is_empty());
            assert_eq!(
                snapshot.last_data_op(&snapshot.nodes[0], OP_FONT_FAMILY),
                None
            );
            utf8.copy_from_slice(b"InterGeorgia");
        }
    }
}
