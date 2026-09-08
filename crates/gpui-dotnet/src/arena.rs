use crate::abi::RenderArena;

/// Acknowledges a published root only after native decoding and resource reconciliation.
/// No borrowed arena data may be accessed by the completion callback.
pub(crate) fn with_root_render_output(
    render: impl FnOnce(*mut RenderArena, *mut u32, *mut u64) -> i32,
    consume: impl FnOnce(&RenderArena, u32, u64) -> Result<(), i32>,
    complete: impl FnOnce(u64, i32) -> i32,
) -> Result<(), i32> {
    let revision = std::cell::Cell::new(0);
    let published = std::cell::Cell::new(false);
    let result = with_render_output(
        |arena, root| {
            let mut value = 0;
            let status = render(arena, root, &mut value);
            revision.set(value);
            published.set(status == 0);
            status
        },
        |arena, root| {
            if revision.get() == 0 {
                return Err(-40);
            }
            consume(arena, root, revision.get())
        },
    );
    if !published.get() {
        return result;
    }
    let status = complete(revision.get(), result.as_ref().err().copied().unwrap_or(0));
    result.and(if status == 0 { Ok(()) } else { Err(status) })
}

/// Executes managed rendering once and synchronously consumes its borrowed output.
///
/// Managed code owns all four allocations. `consume` must copy/decode everything it
/// needs into native-owned state before returning; it must not call managed code,
/// retain arena pointers, or close the session. Rust must never free these buffers.
/// A descriptor lives on this stack, never in a root or cached item batch.
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
    fn root_acceptance_follows_decode_and_can_reuse_managed_storage() {
        let storage = std::cell::RefCell::new(vec![NodeRecord::default()]);
        let decoded = std::cell::Cell::new(false);
        let result = with_root_render_output(
            |arena, _, revision| {
                unsafe {
                    (*arena).generation = 1;
                    (*arena).nodes = storage.borrow_mut().as_mut_ptr();
                    (*arena).node_length = 1;
                    (*arena).node_capacity = 1;
                    *revision = 7;
                }
                0
            },
            |arena, _, revision| {
                assert_eq!(revision, 7);
                assert_eq!(arena.node_length, 1);
                decoded.set(true);
                Ok(())
            },
            |revision, status| {
                assert!(decoded.get());
                assert_eq!((revision, status), (7, 0));
                storage.borrow_mut().clear();
                0
            },
        );
        assert_eq!(result, Ok(()));
        assert!(storage.borrow().is_empty());
    }

    #[test]
    fn failed_publication_is_not_acknowledged() {
        assert_eq!(
            with_root_render_output(
                |_, _, _| -101,
                |_, _, _| panic!("failed publication cannot be decoded"),
                |_, _| panic!("failed publication has no acknowledgement"),
            ),
            Err(-101)
        );
    }

    #[test]
    fn root_decode_failure_is_acknowledged_once() {
        let mut completions = 0;
        assert_eq!(
            with_root_render_output(
                |arena, _, revision| {
                    unsafe {
                        (*arena).generation = 1;
                        *revision = 3;
                    }
                    0
                },
                |_, _, _| Err(-8),
                |revision, status| {
                    assert_eq!((revision, status), (3, -8));
                    completions += 1;
                    -103
                },
            ),
            Err(-8)
        );
        assert_eq!(completions, 1);
    }

    #[test]
    fn invalid_descriptor_and_zero_revision_are_rejected_and_acknowledged() {
        for (generation, revision) in [(0, 1), (1, 0)] {
            assert_eq!(
                with_root_render_output(
                    |arena, _, output_revision| {
                        unsafe {
                            (*arena).generation = generation;
                            *output_revision = revision;
                        }
                        0
                    },
                    |_, _, _| panic!("invalid output cannot be decoded"),
                    |value, status| {
                        assert_eq!((value, status), (revision, -40));
                        0
                    },
                ),
                Err(-40)
            );
        }
    }

    #[test]
    fn managed_acceptance_failure_rejects_the_native_snapshot() {
        assert_eq!(
            with_root_render_output(
                |arena, _, revision| {
                    unsafe {
                        (*arena).generation = 1;
                        *revision = 1;
                    }
                    0
                },
                |_, _, _| Ok(()),
                |_, _| -103,
            ),
            Err(-103)
        );
    }

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
        nodes[0].component = crate::semantic::COMPONENT_DIVIDER;
        assert_eq!(nodes[0].component, crate::semantic::COMPONENT_DIVIDER);
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
