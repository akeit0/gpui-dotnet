use super::*;
use std::{hint::black_box, time::Instant};

#[derive(Default)]
struct Input {
    nodes: Vec<NodeRecord>,
    ops: Vec<OpRecord>,
    children: Vec<ChildRecord>,
    utf8: Vec<u8>,
}

impl Input {
    fn node(&mut self, component: u16, data: &str, parent: Option<u32>) -> u32 {
        let index = self.nodes.len() as u32;
        self.nodes.push(NodeRecord {
            component,
            data_offset: self.utf8.len() as u32,
            data_length: data.len() as u32,
            ..Default::default()
        });
        self.utf8.extend_from_slice(data.as_bytes());
        if let Some(parent) = parent {
            self.children.push(ChildRecord {
                parent,
                child: index,
            });
        }
        index
    }

    fn op(&mut self, node: u32, code: u16, value: u64) {
        self.ops.push(OpRecord {
            node,
            code,
            value_kind: ValueKind::U32 as u16,
            a: value,
            b: 0,
        });
    }

    fn arena(&mut self) -> RenderArena {
        let mut arena: RenderArena = unsafe { std::mem::zeroed() };
        arena.nodes = self.nodes.as_mut_ptr();
        arena.node_length = self.nodes.len() as i32;
        arena.node_capacity = self.nodes.capacity() as i32;
        arena.ops = self.ops.as_mut_ptr();
        arena.op_length = self.ops.len() as i32;
        arena.op_capacity = self.ops.capacity() as i32;
        arena.children = self.children.as_mut_ptr();
        arena.child_length = self.children.len() as i32;
        arena.child_capacity = self.children.capacity() as i32;
        arena.utf8 = self.utf8.as_mut_ptr();
        arena.utf8_length = self.utf8.len() as i32;
        arena.utf8_capacity = self.utf8.capacity() as i32;
        arena.generation = 1;
        arena
    }
}

fn resource_input(count: usize) -> Input {
    let mut input = Input::default();
    input.node(COMPONENT_DIV, "", None);
    for index in 0..count {
        let node = input.node(COMPONENT_SCROLL, &format!("項目-{index:08}"), Some(0));
        input.op(node, OP_RESOURCE_OWNER, 7);
    }
    input
}

fn dock_input(count: usize, depth: usize) -> Input {
    let mut input = Input::default();
    input.node(COMPONENT_DOCK_AREA, "dock", None);
    let mut parent = 0;
    for _ in 0..depth {
        parent = input.node(COMPONENT_DOCK_SPLIT, "", Some(parent));
    }
    let split = input.node(COMPONENT_DOCK_SPLIT, "", Some(parent));
    for index in 0..count {
        let tabs = input.node(COMPONENT_DOCK_TABS, "", Some(split));
        input.op(tabs, OP_DOCK_ACTIVE_INDEX, 0);
        let panel = input.node(
            COMPONENT_DOCK_PANEL,
            &format!("パネル-{index:08}\0Title\0"),
            Some(tabs),
        );
        input.node(COMPONENT_DIV, "", Some(panel));
    }
    input
}

#[test]
fn resource_key_sort_preserves_utf8_identity_and_final_owner() {
    let mut input = resource_input(32);
    let duplicate = "項目-00000000";
    input.nodes[32].data_offset = input.utf8.len() as u32;
    input.nodes[32].data_length = duplicate.len() as u32;
    input.utf8.extend_from_slice(duplicate.as_bytes());
    let mut scratch = SnapshotScratch::default();
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Err(-56)
    );
    input.op(32, OP_RESOURCE_OWNER, 8);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Ok(())
    );
    input.op(32, OP_RESOURCE_OWNER, 7);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Err(-56)
    );
    input.op(32, OP_RESOURCE_OWNER, 0);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Err(-25)
    );
    input.ops.truncate(input.ops.len() - 2);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Ok(())
    );
}

#[test]
fn panel_keys_use_nearest_area_independent_of_node_order() {
    let mut input = dock_input(3, 2);
    let panels: Vec<_> = input
        .nodes
        .iter()
        .enumerate()
        .filter(|(_, node)| node.component == COMPONENT_DOCK_PANEL)
        .map(|(index, _)| index)
        .collect();
    let area = input.node(COMPONENT_DOCK_AREA, "nested", Some((panels[0] + 1) as u32));
    let tabs = input.node(COMPONENT_DOCK_TABS, "", Some(area));
    let panel = input.node(
        COMPONENT_DOCK_PANEL,
        "パネル-00000000\0Nested\0",
        Some(tabs),
    );
    input.node(COMPONENT_DIV, "", Some(panel));

    // Arena order need not put a parent before its descendants.
    let last = input.nodes.len() as u32 - 1;
    input.nodes.reverse();
    for op in &mut input.ops {
        op.node = last - op.node;
    }
    for edge in &mut input.children {
        edge.parent = last - edge.parent;
        edge.child = last - edge.child;
    }
    input.children.reverse();
    let mut scratch = SnapshotScratch::default();
    assert_eq!(
        validate_with_scratch(&input.arena(), last, &mut scratch),
        Ok(())
    );
    let payload = "パネル-00000000\0Other title\0";
    let duplicate = &mut input.nodes[last as usize - panels[2]];
    duplicate.data_offset = input.utf8.len() as u32;
    duplicate.data_length = payload.len() as u32;
    input.utf8.extend_from_slice(payload.as_bytes());
    assert_eq!(
        validate_with_scratch(&input.arena(), last, &mut scratch),
        Err(-61)
    );
}

#[test]
fn dock_operation_index_uses_last_value_and_resets_between_snapshots() {
    let mut input = dock_input(1, 0);
    let tabs = input.ops[0].node;
    input.op(tabs, OP_DOCK_ACTIVE_INDEX, 99);
    input.op(tabs, OP_DOCK_ACTIVE_INDEX, 0);
    let mut regions = Vec::new();
    for id in ["left", "right"] {
        let region = input.node(COMPONENT_DOCK_REGION, "", Some(0));
        let tabs = input.node(COMPONENT_DOCK_TABS, "", Some(region));
        let panel = input.node(COMPONENT_DOCK_PANEL, &format!("{id}\0Title\0"), Some(tabs));
        input.node(COMPONENT_DIV, "", Some(panel));
        regions.push(region);
    }
    input.op(regions[1], OP_DOCK_REGION_SIDE, 0);
    input.op(regions[1], OP_DOCK_REGION_SIDE, 1);
    let mut scratch = SnapshotScratch::default();
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Ok(())
    );
    input.ops.clear();
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Err(-61)
    );
    input.op(regions[0], OP_DOCK_REGION_SIDE, 1);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Ok(())
    );
    input.op(tabs, OP_DOCK_ACTIVE_INDEX, 1);
    assert_eq!(
        validate_with_scratch(&input.arena(), 0, &mut scratch),
        Err(-61)
    );
    let mut plain = resource_input(8);
    assert_eq!(
        validate_with_scratch(&plain.arena(), 0, &mut scratch),
        Ok(())
    );
    assert!(scratch.dock.is_empty());
}

#[test]
#[ignore = "native timing probe; run explicitly in Release with --nocapture"]
fn native_workload_measurements_validation() {
    for (label, mut input) in [
        ("resources-128", resource_input(128)),
        ("resources-1024", resource_input(1024)),
        ("dock-128", dock_input(128, 0)),
        ("dock-512", dock_input(512, 0)),
        ("dock-128-depth-128", dock_input(128, 128)),
    ] {
        let arena = input.arena();
        let mut scratch = SnapshotScratch::default();
        let mut samples = Vec::new();
        for batch in 0..9 {
            let start = Instant::now();
            for _ in 0..16 {
                validate_with_scratch(black_box(&arena), 0, &mut scratch).unwrap();
            }
            let elapsed = start.elapsed().as_secs_f64() * 1e6 / 16.;
            if batch >= 4 {
                samples.push(elapsed);
            }
        }
        samples.sort_by(f64::total_cmp);
        println!(
            "validate-{label}: median={:.2} us/op; scratch-capacity={} bytes",
            samples[2],
            scratch.buffer_capacity_bytes()
        );
    }
}
