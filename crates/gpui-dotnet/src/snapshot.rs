use std::{collections::HashSet, slice, sync::Arc};

use gpui::SharedString;

use crate::{
    abi::{NodeRecord, OpRecord, RenderArena},
    semantic::{
        COMPONENT_CONTEXT_MENU, COMPONENT_DOCK_AREA, COMPONENT_DOCK_PANEL, COMPONENT_DOCK_REGION,
        COMPONENT_DOCK_SPLIT, COMPONENT_DOCK_TABS, COMPONENT_DRAWING, COMPONENT_DYNAMIC,
        COMPONENT_INPUT, COMPONENT_LIST, COMPONENT_NATIVE_EXTENSION, COMPONENT_OVERLAY,
        COMPONENT_PATH, COMPONENT_POPOVER_MENU, COMPONENT_SLIDER, COMPONENT_TABLE,
        COMPONENT_TOOLTIP, DataKind, OP_DOCK_ACTIVE_INDEX, OP_DOCK_REGION_SIDE,
        OP_DRAWING_VIEW_BOX_SIZE, OP_PATH_ARC_RADII, OP_RESOURCE_OWNER, ValueKind, allows_payload,
        component_metadata, operation_metadata, payload_error,
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
}

impl ValidatedSnapshot {
    pub fn decode_into(
        &mut self,
        arena: &RenderArena,
        root: u32,
        retained_strings: &mut RetainedStrings,
        scratch: &mut SnapshotScratch,
    ) -> Result<(), i32> {
        validate_with_scratch(arena, root, scratch)?;

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
        self.op_data.resize(self.ops.len(), None);
        for (index, op) in self.ops.iter().enumerate() {
            let is_data = operation_metadata(op.code)
                .is_some_and(|metadata| metadata.value_kind == ValueKind::Data);
            if !is_data {
                continue;
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
}

impl SnapshotScratch {
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

pub fn validate(arena: &RenderArena, root: u32) -> Result<(), i32> {
    validate_with_scratch(arena, root, &mut SnapshotScratch::default())
}

/// Virtualized lists and tables share one row-engine namespace per owner view, so a List and a
/// Table (or two of either) must not declare the same `(owner, key)`. Slider resources use a
/// separate kind namespace and are checked independently. The count of retained nodes per
/// snapshot is tiny, so the O(K²) pairwise comparison over reusable scratch memory is cheaper
/// than hashing strings.
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
    }

    let mut count = 0usize;
    for index in 0..nodes.len() {
        let node = &nodes[index];
        // A table's row-engine key is the first NUL-separated field of its data blob; a list's
        // key is the whole payload. The kind keeps Slider's separate resource namespace apart.
        let (kind, key_length) = match node.component {
            COMPONENT_LIST => (2, node.data_length),
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
        for previous in &owners[..count] {
            if previous.0 == owner
                && previous.3 == kind
                && previous.2 == key_length
                && utf8[previous.1 as usize..previous.1 as usize + key_length as usize]
                    == utf8
                        [node.data_offset as usize..node.data_offset as usize + key_length as usize]
            {
                return Err(-56);
            }
        }
        owners[count] = (owner, node.data_offset, key_length, kind);
        count += 1;
    }
    Ok(())
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
        let payload_error = payload_error(op.code, op.a);
        if payload_error != 0 {
            return Err(payload_error);
        }
    }

    scratch.prepare_nodes(node_len, child_len);
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
    for (index, node) in nodes.iter().enumerate() {
        if node.component != COMPONENT_DOCK_TABS {
            continue;
        }
        let active_index = ops
            .iter()
            .rev()
            .find(|operation| {
                operation.node as usize == index && operation.code == OP_DOCK_ACTIVE_INDEX
            })
            .map_or(0, |operation| operation.a as usize);
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
        for edge in children.iter().filter(|edge| edge.parent as usize == index) {
            if nodes[edge.child as usize].component != COMPONENT_DOCK_REGION {
                center_count += 1;
                continue;
            }
            let side = ops
                .iter()
                .rev()
                .find(|operation| {
                    operation.node == edge.child && operation.code == OP_DOCK_REGION_SIDE
                })
                .map_or(0, |operation| operation.a as u32);
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

    for left in 0..nodes.len() {
        if nodes[left].component != COMPONENT_DOCK_PANEL {
            continue;
        }
        let left_area = dock_area_ancestor(left, nodes, &scratch.parents);
        let left_id = dock_panel_id(&nodes[left], utf8);
        for right in left + 1..nodes.len() {
            if nodes[right].component == COMPONENT_DOCK_PANEL
                && dock_area_ancestor(right, nodes, &scratch.parents) == left_area
                && dock_panel_id(&nodes[right], utf8) == left_id
            {
                return Err(-61);
            }
        }
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

    scratch.pending.push(root);
    while let Some(parent) = scratch.pending.pop() {
        let visited = &mut scratch.visited[parent as usize];
        if *visited != 0 {
            return Err(-12);
        }
        *visited = 1;
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

    Ok(())
}

fn dock_area_ancestor(index: usize, nodes: &[NodeRecord], parents: &[u32]) -> Option<u32> {
    let mut current = index;
    loop {
        let parent = parents[current];
        if parent == u32::MAX {
            return None;
        }
        if nodes[parent as usize].component == COMPONENT_DOCK_AREA {
            return Some(parent);
        }
        current = parent as usize;
    }
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
        unsafe { slice::from_raw_parts(pointer, len) }
    }
}

#[cfg(test)]
mod tests {
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
}
