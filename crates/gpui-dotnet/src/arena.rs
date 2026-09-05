use crate::abi::RenderArena;

/// Executes managed rendering once and synchronously consumes its borrowed output.
///
/// Managed code owns all four allocations. `consume` must copy/decode everything it
/// needs into native-owned state before returning; it must not call managed code,
/// retain arena pointers, or close the session. Rust must never free these buffers.
/// A descriptor lives on this stack, never in a root or cached row batch.
pub(crate) fn with_render_output(
    render: impl FnOnce(*mut RenderArena, *mut u32) -> i32,
    consume: impl FnOnce(&RenderArena, u32) -> Result<(), i32>,
) -> Result<(), i32> {
    let mut arena = RenderArena {
        nodes: std::ptr::null_mut(),
        node_length: 0,
        node_capacity: 0,
        ops: std::ptr::null_mut(),
        op_length: 0,
        op_capacity: 0,
        children: std::ptr::null_mut(),
        child_length: 0,
        child_capacity: 0,
        utf8: std::ptr::null_mut(),
        utf8_length: 0,
        utf8_capacity: 0,
        generation: 0,
        flags: 0,
        required_node_capacity: 0,
        required_op_capacity: 0,
        required_child_capacity: 0,
        required_utf8_capacity: 0,
    };
    let mut root = 0;
    let status = render(&mut arena, &mut root);
    if status != 0 {
        // Status 1 is an error too. ABI 4 has no capacity-retry status.
        return Err(status);
    }
    if arena.generation == 0
        || arena.flags != 0
        || arena.required_node_capacity != 0
        || arena.required_op_capacity != 0
        || arena.required_child_capacity != 0
        || arena.required_utf8_capacity != 0
    {
        return Err(-40);
    }
    consume(&arena, root)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::abi::NodeRecord;

    #[test]
    fn successful_output_is_consumed_once_without_transferring_ownership() {
        let mut nodes = vec![NodeRecord::default(); 4096];
        let mut calls = 0;
        let mut consumed = 0;
        let result = with_render_output(
            |arena, root| {
                calls += 1;
                unsafe {
                    (*arena).nodes = nodes.as_mut_ptr();
                    (*arena).node_length = nodes.len() as i32;
                    (*arena).node_capacity = nodes.len() as i32;
                    (*arena).generation = 1;
                    *root = 123;
                }
                0
            },
            |arena, root| {
                consumed += 1;
                assert_eq!(root, 123);
                assert_eq!(arena.node_length, 4096);
                Ok(())
            },
        );
        assert_eq!(result, Ok(()));
        assert_eq!((calls, consumed), (1, 1));
        nodes[0].component = 7;
        assert_eq!(nodes[0].component, 7);
    }

    #[test]
    fn old_growth_status_is_not_retried_or_consumed() {
        let mut calls = 0;
        let result = with_render_output(
            |_, _| {
                calls += 1;
                1
            },
            |_, _| panic!("Failed output must not be consumed"),
        );
        assert_eq!(result, Err(1));
        assert_eq!(calls, 1);
    }

    #[test]
    fn reserved_growth_fields_are_rejected() {
        let result = with_render_output(
            |arena, _| {
                unsafe {
                    (*arena).generation = 1;
                    (*arena).required_utf8_capacity = 1;
                }
                0
            },
            |_, _| panic!("Malformed output must not be consumed"),
        );
        assert_eq!(result, Err(-40));
    }

    #[test]
    fn decode_failure_does_not_reinvoke_render() {
        let mut calls = 0;
        let result = with_render_output(
            |arena, _| {
                calls += 1;
                unsafe { (*arena).generation = 1 };
                0
            },
            |_, _| Err(-3),
        );
        assert_eq!(result, Err(-3));
        assert_eq!(calls, 1);
    }

    #[test]
    fn decoded_snapshot_survives_reuse_of_managed_output_storage() {
        use crate::semantic::COMPONENT_TEXT;
        use crate::snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot};

        let mut nodes = [NodeRecord {
            component: COMPONENT_TEXT,
            data_length: 6,
            ..NodeRecord::default()
        }];
        let mut utf8 = *b"before";
        let mut snapshot = ValidatedSnapshot::default();
        let mut strings = RetainedStrings::default();
        let mut scratch = SnapshotScratch::default();
        with_render_output(
            |arena, _| {
                unsafe {
                    (*arena).nodes = nodes.as_mut_ptr();
                    (*arena).node_length = 1;
                    (*arena).node_capacity = 1;
                    (*arena).utf8 = utf8.as_mut_ptr();
                    (*arena).utf8_length = 6;
                    (*arena).utf8_capacity = 6;
                    (*arena).generation = 1;
                }
                0
            },
            |arena, root| snapshot.decode_into(arena, root, &mut strings, &mut scratch),
        )
        .unwrap();
        utf8.fill(b'x');
        nodes[0].data_length = 0;
        assert_eq!(nodes[0].data_length, 0);
        assert_eq!(snapshot.nodes[0].data.as_ref(), "before");
        assert_eq!(snapshot.nodes[0].component, COMPONENT_TEXT);
    }
}
