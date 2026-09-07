use std::{cell::RefCell, rc::Rc};

use crate::abi::OpRecord;

#[derive(Default)]
pub(crate) struct DrawingCommandPool {
    buffers: Vec<Vec<OpRecord>>,
    retained_bytes: usize,
}

/// A canvas exclusively owns its commands until prepaint consumes them or the canvas is dropped.
/// Only then may another canvas overwrite the buffer, even across decoded snapshot replacement.
pub(crate) struct DrawingCommands {
    operations: Vec<OpRecord>,
    pool: Rc<RefCell<DrawingCommandPool>>,
}

impl DrawingCommandPool {
    const MAX_BYTES: usize = 4 * 1024 * 1024;
    const MAX_BUFFERS: usize = 256;

    pub(crate) fn acquire(pool: Rc<RefCell<Self>>, count: usize) -> DrawingCommands {
        let mut available = pool.borrow_mut();
        let mut operations = if count == 0 {
            Vec::new()
        } else if let Some(index) = available
            .buffers
            .iter()
            .position(|buffer| buffer.capacity() >= count)
            .or_else(|| available.buffers.len().checked_sub(1))
        {
            let buffer = available.buffers.swap_remove(index);
            available.retained_bytes -= buffer.capacity() * size_of::<OpRecord>();
            buffer
        } else {
            Vec::with_capacity(count)
        };
        drop(available);
        operations.reserve(count);
        DrawingCommands { operations, pool }
    }

    #[cfg(test)]
    pub(crate) fn retained_bytes(&self) -> usize {
        self.retained_bytes
    }
}

impl DrawingCommands {
    pub(crate) fn extend_from_slice(&mut self, operations: &[OpRecord]) {
        self.operations.extend_from_slice(operations);
    }

    pub(crate) fn paths(&self) -> impl Iterator<Item = &[OpRecord]> {
        // Validation guarantees unique child IDs. Empty paths have no operations or paint.
        self.operations.chunk_by(|a, b| a.node == b.node)
    }
}

impl Drop for DrawingCommands {
    fn drop(&mut self) {
        let bytes = self.operations.capacity() * size_of::<OpRecord>();
        let mut pool = self.pool.borrow_mut();
        if bytes != 0
            && pool.buffers.len() < DrawingCommandPool::MAX_BUFFERS
            && bytes <= DrawingCommandPool::MAX_BYTES.saturating_sub(pool.retained_bytes)
        {
            self.operations.clear();
            pool.buffers.push(std::mem::take(&mut self.operations));
            pool.retained_bytes += bytes;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_released_buffers_are_reused_and_no_commands_or_owners_are_retained() {
        let pool = Rc::new(RefCell::new(DrawingCommandPool::default()));
        let weak = Rc::downgrade(&pool);
        let mut first = DrawingCommandPool::acquire(pool.clone(), 8);
        first.extend_from_slice(&[OpRecord {
            node: 7,
            ..Default::default()
        }]);
        let address = first.operations.as_ptr();
        let mut second = DrawingCommandPool::acquire(pool.clone(), 8);
        assert_ne!(address, second.operations.as_ptr());
        second.extend_from_slice(&[OpRecord {
            node: 9,
            ..Default::default()
        }]);
        drop(first);
        assert_eq!(pool.borrow().retained_bytes, 8 * size_of::<OpRecord>());
        let mut reused = DrawingCommandPool::acquire(pool.clone(), 4);
        assert_eq!(address, reused.operations.as_ptr());
        assert!(reused.operations.is_empty());
        assert_eq!(pool.borrow().retained_bytes, 0);
        reused.extend_from_slice(&[OpRecord {
            node: 11,
            ..Default::default()
        }]);
        drop(reused);
        let grown = DrawingCommandPool::acquire(pool.clone(), 32);
        assert!(grown.operations.capacity() >= 32);
        assert!(grown.operations.is_empty());
        assert!(pool.borrow().buffers.is_empty());
        assert_eq!(pool.borrow().retained_bytes, 0);
        assert_eq!(second.operations[0].node, 9);
        drop(pool);
        drop(second);
        assert!(weak.upgrade().is_some());
        drop(grown);
        assert!(weak.upgrade().is_none());
    }

    #[test]
    fn pool_bounds_free_capacity_and_falls_back_for_oversized_or_excess_buffers() {
        let pool = Rc::new(RefCell::new(DrawingCommandPool::default()));
        drop(DrawingCommandPool::acquire(pool.clone(), 0));
        assert!(pool.borrow().buffers.is_empty());
        let oversized = DrawingCommandPool::acquire(
            pool.clone(),
            DrawingCommandPool::MAX_BYTES / size_of::<OpRecord>() + 1,
        );
        drop(oversized);
        assert!(pool.borrow().buffers.is_empty());
        let count = DrawingCommandPool::MAX_BYTES / 2 / size_of::<OpRecord>() + 1;
        let first = DrawingCommandPool::acquire(pool.clone(), count);
        let second = DrawingCommandPool::acquire(pool.clone(), count);
        drop(first);
        drop(second);
        assert_eq!(pool.borrow().buffers.len(), 1);
        assert!(pool.borrow().retained_bytes <= DrawingCommandPool::MAX_BYTES);

        let pool = Rc::new(RefCell::new(DrawingCommandPool::default()));
        let held: Vec<_> = (0..=DrawingCommandPool::MAX_BUFFERS)
            .map(|_| DrawingCommandPool::acquire(pool.clone(), 1))
            .collect();
        drop(held);
        assert_eq!(pool.borrow().buffers.len(), DrawingCommandPool::MAX_BUFFERS);
    }

    #[test]
    fn decoded_replacements_reuse_free_buffers_but_preserve_older_frame_commands() {
        use crate::{native_workloads::WorkloadArena, semantic::*};
        let mut arena = WorkloadArena::default();
        let root = arena.node(COMPONENT_DRAWING, None);
        let path = arena.node(COMPONENT_PATH, Some(root));
        arena.op(path, OP_PATH_FILL_RGBA, 7);
        let mut snapshot = arena.decode();
        let old = snapshot.drawing_commands(&snapshot.nodes[0]);
        let pool = Rc::downgrade(&old.pool);
        arena.op(path, OP_PATH_FILL_RGBA, 9);
        arena.decode_into(&mut snapshot).unwrap();
        let current = snapshot.drawing_commands(&snapshot.nodes[0]);
        assert!(Rc::ptr_eq(&old.pool, &current.pool));
        assert_eq!(old.operations.len(), 1);
        assert_eq!(old.operations[0].a, 7);
        assert_eq!(current.operations.len(), 2);
        assert_eq!(current.operations[1].a, 9);
        let address = current.operations.as_ptr();
        drop(current);
        arena.decode_into(&mut snapshot).unwrap();
        let reused = snapshot.drawing_commands(&snapshot.nodes[0]);
        assert_eq!(address, reused.operations.as_ptr());
        drop(reused);
        let mut no_drawing = WorkloadArena::default();
        no_drawing.node(COMPONENT_DIV, None);
        no_drawing.decode_into(&mut snapshot).unwrap();
        assert!(pool.upgrade().is_some());
        drop(old);
        assert!(pool.upgrade().is_none());
    }
}
