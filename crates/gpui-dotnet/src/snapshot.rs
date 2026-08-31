use std::{collections::HashSet, slice, sync::Arc};

use gpui::SharedString;

use crate::{
    abi::{OpRecord, RenderArena},
    semantic::{
        COMPONENT_CONTEXT_MENU, COMPONENT_INPUT, COMPONENT_OVERLAY, COMPONENT_POPOVER_MENU,
        COMPONENT_TOOLTIP, DataKind, OP_CHECKED, OP_CONTEXT_MENU_MARGIN_PX, OP_IMAGE_GRAYSCALE,
        OP_IMAGE_OBJECT_FIT, OP_INPUT_DISABLED, OP_INPUT_PASSWORD, OP_INPUT_READ_ONLY,
        OP_LIST_ALIGNMENT, OP_LIST_BATCH_SIZE, OP_LIST_ESTIMATED_ITEM_HEIGHT_PX,
        OP_LIST_OVERDRAW_PX, OP_ON_CLICK, OP_OVERLAY_DISMISS_ON_BACKDROP,
        OP_OVERLAY_DISMISS_ON_ESCAPE, OP_OVERLAY_MARGIN_PX, OP_OVERLAY_MODAL,
        OP_OVERLAY_ON_DISMISS, OP_OVERLAY_PLACEMENT, OP_POPOVER_MENU_MARGIN_PX, OP_RESOURCE_OWNER,
        OP_SCROLL_AXIS, OP_SHOW_SCROLLBAR, OP_SMOOTH_SCROLL, OP_TOOLTIP_ALIGNMENT,
        OP_TOOLTIP_GAP_PX, OP_TOOLTIP_MARGIN_PX, OP_TOOLTIP_PLACEMENT, OP_WINDOW_CONTROL_AREA,
        ValueKind, component_metadata, operation_metadata,
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
        if op.b != 0 && !matches!(op.code, OP_ON_CLICK | OP_OVERLAY_ON_DISMISS) {
            return Err(-23);
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
        if metadata.value_kind == ValueKind::Callback && op.a == 0 {
            return Err(-19);
        }
        if op.code == OP_CHECKED && op.a > 1 {
            return Err(-21);
        }
        if op.code == OP_RESOURCE_OWNER && op.a == 0 {
            return Err(-25);
        }
        if op.code == OP_SCROLL_AXIS && op.a > 2 {
            return Err(-26);
        }
        if op.code == OP_WINDOW_CONTROL_AREA && op.a > 3 {
            return Err(-39);
        }
        if matches!(
            op.code,
            OP_SMOOTH_SCROLL
                | OP_SHOW_SCROLLBAR
                | OP_INPUT_DISABLED
                | OP_INPUT_READ_ONLY
                | OP_INPUT_PASSWORD
                | OP_OVERLAY_MODAL
                | OP_OVERLAY_DISMISS_ON_BACKDROP
                | OP_OVERLAY_DISMISS_ON_ESCAPE
        ) && op.a > 1
        {
            return Err(-21);
        }
        if op.code == OP_IMAGE_GRAYSCALE && op.a > 1 {
            return Err(-21);
        }
        if op.code == OP_IMAGE_OBJECT_FIT && op.a > 4 {
            return Err(-30);
        }
        if op.code == OP_LIST_BATCH_SIZE && !(1..=512).contains(&(op.a as u32)) {
            return Err(-27);
        }
        if op.code == OP_LIST_ALIGNMENT && op.a > 1 {
            return Err(-28);
        }
        if op.code == OP_LIST_OVERDRAW_PX && f32::from_bits(op.a as u32) < 0.0 {
            return Err(-29);
        }
        if op.code == OP_LIST_ESTIMATED_ITEM_HEIGHT_PX && f32::from_bits(op.a as u32) <= 0.0 {
            return Err(-29);
        }
        if op.code == OP_OVERLAY_PLACEMENT && op.a > 8 {
            return Err(-33);
        }
        if op.code == OP_OVERLAY_MARGIN_PX && f32::from_bits(op.a as u32) < 0.0 {
            return Err(-34);
        }
        if op.code == OP_TOOLTIP_PLACEMENT && op.a > 4 {
            return Err(-35);
        }
        if op.code == OP_TOOLTIP_ALIGNMENT && op.a > 2 {
            return Err(-36);
        }
        if matches!(op.code, OP_TOOLTIP_GAP_PX | OP_TOOLTIP_MARGIN_PX)
            && f32::from_bits(op.a as u32) < 0.0
        {
            return Err(-37);
        }
        if matches!(
            op.code,
            OP_CONTEXT_MENU_MARGIN_PX | OP_POPOVER_MENU_MARGIN_PX
        ) && f32::from_bits(op.a as u32) < 0.0
        {
            return Err(-40);
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

    if scratch.parents[root as usize] != u32::MAX {
        return Err(-11);
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

    Ok(())
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
            COMPONENT_CHECKBOX, COMPONENT_CONTEXT_MENU, COMPONENT_DIV, COMPONENT_IMAGE,
            COMPONENT_INPUT, COMPONENT_OVERLAY, COMPONENT_POPOVER_MENU, COMPONENT_TEXT,
            COMPONENT_TOOLTIP, OP_CHECKED, OP_GAP_PX, OP_IMAGE_OBJECT_FIT, OP_INPUT_DISABLED,
            OP_LIST_ITEM_ID, OP_ON_CLICK, OP_OVERLAY_PLACEMENT, OP_PADDING_PX,
            OP_TOOLTIP_PLACEMENT, OP_WINDOW_CONTROL_AREA,
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
    fn retains_string_capacity_and_reuses_consecutive_values() {
        let mut strings = RetainedStrings::default();
        strings.begin_snapshot();
        let first = strings.intern("abc");
        strings.begin_snapshot();
        let second = strings.intern("abc");
        assert_eq!(first.as_str().as_ptr(), second.as_str().as_ptr());

        strings.begin_snapshot();
        let _replacement = strings.intern("replacement");
        strings.begin_snapshot();
        let after_eviction = strings.intern("abc");
        assert_ne!(first.as_str().as_ptr(), after_eviction.as_str().as_ptr());
    }
}
