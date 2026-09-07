//! Fixtures for opt-in, isolated native preparation measurements. No GPUI window is opened.

use crate::{
    abi::{ChildRecord, NodeRecord, OpRecord, RenderArena},
    semantic::operation_metadata,
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
};

#[derive(Default)]
pub(crate) struct WorkloadArena {
    nodes: Vec<NodeRecord>,
    ops: Vec<OpRecord>,
    children: Vec<ChildRecord>,
}

impl WorkloadArena {
    pub(crate) fn node(&mut self, component: u16, parent: Option<u32>) -> u32 {
        let id = self.nodes.len() as u32;
        self.nodes.push(NodeRecord {
            component,
            ..Default::default()
        });
        if let Some(parent) = parent {
            self.children.push(ChildRecord { parent, child: id });
        }
        id
    }

    pub(crate) fn op(&mut self, node: u32, code: u16, a: u64) {
        self.ops.push(OpRecord {
            node,
            code,
            a,
            value_kind: operation_metadata(code).unwrap().value_kind as u16,
            b: 0,
        });
    }

    pub(crate) fn point(&mut self, node: u32, code: u16, x: f32, y: f32) {
        self.op(
            node,
            code,
            x.to_bits() as u64 | ((y.to_bits() as u64) << 32),
        );
    }

    pub(crate) fn decode(&mut self) -> ValidatedSnapshot {
        let arena = RenderArena {
            nodes: self.nodes.as_mut_ptr(),
            node_length: self.nodes.len() as i32,
            node_capacity: self.nodes.len() as i32,
            ops: self.ops.as_mut_ptr(),
            op_length: self.ops.len() as i32,
            op_capacity: self.ops.len() as i32,
            children: self.children.as_mut_ptr(),
            child_length: self.children.len() as i32,
            child_capacity: self.children.len() as i32,
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
        snapshot
    }
}

pub(crate) fn measure(label: &str, iterations: usize, mut operation: impl FnMut()) {
    for _ in 0..4 {
        for _ in 0..iterations {
            operation();
        }
    }
    let mut samples = [0.0f64; 5];
    for sample in &mut samples {
        let start = std::time::Instant::now();
        for _ in 0..iterations {
            operation();
        }
        *sample = start.elapsed().as_secs_f64() * 1_000_000.0 / iterations as f64;
    }
    samples.sort_by(f64::total_cmp);
    println!(
        "{label}: median_us={:.3} min_us={:.3} max_us={:.3}",
        samples[2], samples[0], samples[4]
    );
}
