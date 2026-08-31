use crate::abi::{
    ARENA_FLAG_NATIVE_OWNED, ChildRecord, NodeRecord, OpRecord, RENDER_GROW_REQUIRED, RenderArena,
};

const NODE_CAPACITY: usize = 256;
const OP_CAPACITY: usize = 2 * 1024;
const CHILD_CAPACITY: usize = 512;
const UTF8_CAPACITY: usize = 16 * 1024;

/// Owns the writable buffers lent to a managed render callback.
///
/// Pointers remain stable for one managed render callback. When managed code
/// requests more capacity, Rust replaces the affected buffer and retries the render.
/// Managed code must not resize or retain pointers after the callback.
pub struct OwnedRenderArena {
    native: RenderArena,
    _nodes: Box<[NodeRecord]>,
    _ops: Box<[OpRecord]>,
    _children: Box<[ChildRecord]>,
    _utf8: Box<[u8]>,
}

impl OwnedRenderArena {
    pub fn new() -> Self {
        let mut nodes = vec![NodeRecord::default(); NODE_CAPACITY].into_boxed_slice();
        let mut ops = vec![OpRecord::default(); OP_CAPACITY].into_boxed_slice();
        let mut children = vec![ChildRecord::default(); CHILD_CAPACITY].into_boxed_slice();
        let mut utf8 = vec![0; UTF8_CAPACITY].into_boxed_slice();

        let native = RenderArena {
            nodes: nodes.as_mut_ptr(),
            node_length: 0,
            node_capacity: NODE_CAPACITY as i32,
            ops: ops.as_mut_ptr(),
            op_length: 0,
            op_capacity: OP_CAPACITY as i32,
            children: children.as_mut_ptr(),
            child_length: 0,
            child_capacity: CHILD_CAPACITY as i32,
            utf8: utf8.as_mut_ptr(),
            utf8_length: 0,
            utf8_capacity: UTF8_CAPACITY as i32,
            generation: 1,
            flags: ARENA_FLAG_NATIVE_OWNED,
            required_node_capacity: 0,
            required_op_capacity: 0,
            required_child_capacity: 0,
            required_utf8_capacity: 0,
        };

        Self {
            native,
            _nodes: nodes,
            _ops: ops,
            _children: children,
            _utf8: utf8,
        }
    }

    pub fn begin_render(&mut self) -> *mut RenderArena {
        self.native.node_length = 0;
        self.native.op_length = 0;
        self.native.child_length = 0;
        self.native.utf8_length = 0;
        self.native.generation = self.native.generation.wrapping_add(1).max(1);
        self.native.required_node_capacity = 0;
        self.native.required_op_capacity = 0;
        self.native.required_child_capacity = 0;
        self.native.required_utf8_capacity = 0;
        &mut self.native
    }

    pub fn as_native(&self) -> &RenderArena {
        &self.native
    }

    pub fn grow_requested(&mut self) -> Result<bool, i32> {
        let mut grew = false;
        grew |= grow_buffer(
            &mut self._nodes,
            self.native.required_node_capacity,
            &mut self.native.nodes,
            &mut self.native.node_capacity,
        )?;
        grew |= grow_buffer(
            &mut self._ops,
            self.native.required_op_capacity,
            &mut self.native.ops,
            &mut self.native.op_capacity,
        )?;
        grew |= grow_buffer(
            &mut self._children,
            self.native.required_child_capacity,
            &mut self.native.children,
            &mut self.native.child_capacity,
        )?;
        grew |= grow_buffer(
            &mut self._utf8,
            self.native.required_utf8_capacity,
            &mut self.native.utf8,
            &mut self.native.utf8_capacity,
        )?;
        Ok(grew)
    }

    pub fn render_with_growth_retry(
        &mut self,
        mut render: impl FnMut(*mut RenderArena) -> i32,
    ) -> Result<i32, i32> {
        loop {
            let status = render(self.begin_render());
            if status != RENDER_GROW_REQUIRED {
                return Ok(status);
            }
            if !self.grow_requested()? {
                return Err(-40);
            }
        }
    }
}

fn grow_buffer<T: Default + Clone>(
    buffer: &mut Box<[T]>,
    required_capacity: i32,
    native_pointer: &mut *mut T,
    native_capacity: &mut i32,
) -> Result<bool, i32> {
    if required_capacity <= *native_capacity {
        return Ok(false);
    }

    let required = usize::try_from(required_capacity).map_err(|_| -40)?;
    let mut capacity = buffer.len().max(1);
    while capacity < required {
        capacity = capacity.checked_mul(2).ok_or(-41)?;
    }
    if capacity > i32::MAX as usize {
        return Err(-41);
    }

    let mut grown = Vec::new();
    grown.try_reserve_exact(capacity).map_err(|_| -42)?;
    grown.resize(capacity, T::default());
    *buffer = grown.into_boxed_slice();
    *native_pointer = buffer.as_mut_ptr();
    *native_capacity = capacity as i32;
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn requested_growth_is_geometric_and_retained() {
        let mut arena = OwnedRenderArena::new();
        let initial_nodes = arena.native.node_capacity;
        let initial_utf8 = arena.native.utf8_capacity;

        arena.native.required_node_capacity = initial_nodes + 1;
        arena.native.required_utf8_capacity = initial_utf8 * 3;
        assert_eq!(arena.grow_requested(), Ok(true));
        assert_eq!(arena.native.node_capacity, initial_nodes * 2);
        assert_eq!(arena.native.utf8_capacity, initial_utf8 * 4);

        let retained_nodes = arena.native.node_capacity;
        let retained_utf8 = arena.native.utf8_capacity;
        let _ = arena.begin_render();
        assert_eq!(arena.native.node_capacity, retained_nodes);
        assert_eq!(arena.native.utf8_capacity, retained_utf8);
        assert_eq!(arena.grow_requested(), Ok(false));
    }

    #[test]
    fn render_retries_after_managed_growth_request() {
        let mut arena = OwnedRenderArena::new();
        let initial_capacity = arena.native.node_capacity;
        let mut calls = 0;

        let status = arena.render_with_growth_retry(|native| {
            calls += 1;
            let native = unsafe { &mut *native };
            if calls == 1 {
                native.required_node_capacity = initial_capacity + 1;
                RENDER_GROW_REQUIRED
            } else {
                assert_eq!(native.node_capacity, initial_capacity * 2);
                0
            }
        });

        assert_eq!(status, Ok(0));
        assert_eq!(calls, 2);
    }
}
