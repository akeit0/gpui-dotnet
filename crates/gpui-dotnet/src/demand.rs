//! Shared publication and lease ownership for demand-rendered semantic snapshots.
//! Request encoding, output shape, cache policy, and measurement belong to adapters.

use std::sync::atomic::{AtomicU64, Ordering};

use crate::{
    abi::{ManagedCallbacks, RenderArena},
    arena::with_render_output,
    snapshot::{RetainedStrings, SnapshotScratch, ValidatedSnapshot},
};

pub(crate) fn next_source_id() -> u64 {
    static NEXT_SOURCE_ID: AtomicU64 = AtomicU64::new(1);
    NEXT_SOURCE_ID
        .try_update(Ordering::Relaxed, Ordering::Relaxed, |id| id.checked_add(1))
        .expect("native demand source identity space exhausted")
}

pub(crate) struct ArtifactLease {
    session_id: u64,
    source_id: u64,
    pub(crate) artifact_id: u64,
    release: crate::abi::ManagedReleaseArtifactFn,
    status: i32,
}

impl Drop for ArtifactLease {
    fn drop(&mut self) {
        // Framework cleanup remains admissible after faults and during root acceptance.
        unsafe {
            (self.release)(
                self.session_id,
                self.source_id,
                self.artifact_id,
                self.status,
            );
        }
    }
}

pub(crate) fn load_artifact(
    session_id: u64,
    source_id: u64,
    callbacks: ManagedCallbacks,
    scratch: &mut SnapshotScratch,
    render: impl FnOnce(*mut RenderArena, *mut u32, *mut u64) -> i32,
    validate_shape: impl FnOnce(&ValidatedSnapshot) -> Result<(), i32>,
) -> Result<(ValidatedSnapshot, ArtifactLease), i32> {
    let mut snapshot = ValidatedSnapshot::default();
    let mut strings = RetainedStrings::default();
    let mut artifact_id = 0;
    let result = with_render_output(
        |arena, root| render(arena, root, &mut artifact_id),
        |arena, root| snapshot.decode_into(arena, root, &mut strings, scratch),
    );
    // Only after the output borrow ends may any acceptance/release callback run.
    let mut lease = (artifact_id != 0).then(|| ArtifactLease {
        session_id,
        source_id,
        artifact_id,
        release: callbacks
            .release_artifact
            .expect("validated managed callbacks"),
        status: result.as_ref().err().copied().unwrap_or(0),
    });
    result?;
    let mut lease = lease.take().ok_or(-64)?;
    if let Err(status) = validate_shape(&snapshot) {
        lease.status = status;
        return Err(status);
    }
    let accept = callbacks
        .accept_artifact
        .expect("validated managed callbacks");
    let status = unsafe { accept(session_id, source_id, artifact_id) };
    if status != 0 {
        return Err(status);
    }
    Ok((snapshot, lease))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::{Cell, RefCell};

    thread_local! {
        static EVENTS: RefCell<Vec<(&'static str, i32)>> = const { RefCell::new(Vec::new()) };
        static ACCEPT_STATUS: Cell<i32> = const { Cell::new(0) };
    }

    unsafe extern "C" fn accept(_: u64, _: u64, _: u64) -> i32 {
        EVENTS.with(|events| events.borrow_mut().push(("accept", 0)));
        ACCEPT_STATUS.get()
    }

    unsafe extern "C" fn release(_: u64, _: u64, _: u64, status: i32) -> i32 {
        EVENTS.with(|events| events.borrow_mut().push(("release", status)));
        0
    }

    #[test]
    fn arbitrary_snapshot_shape_shares_publication_acceptance_and_release() {
        for mode in ["success", "decode", "shape", "accept", "render", "missing"] {
            EVENTS.with(|events| events.borrow_mut().clear());
            ACCEPT_STATUS.set(if mode == "accept" { -109 } else { 0 });
            let mut callbacks: ManagedCallbacks = unsafe { std::mem::zeroed() };
            callbacks.accept_artifact = Some(accept);
            callbacks.release_artifact = Some(release);
            let mut node = crate::abi::NodeRecord {
                component: crate::semantic::COMPONENT_TEXT,
                data_length: 4,
                ..Default::default()
            };
            let mut text = *b"card";
            let mut scratch = SnapshotScratch::default();
            let result = load_artifact(
                1,
                next_source_id(),
                callbacks,
                &mut scratch,
                |arena, root, artifact| unsafe {
                    EVENTS.with(|events| events.borrow_mut().push(("render", 0)));
                    if mode == "render" {
                        return -106;
                    }
                    (*arena).nodes = &mut node;
                    (*arena).node_length = 1;
                    (*arena).node_capacity = 1;
                    (*arena).utf8 = text.as_mut_ptr();
                    (*arena).utf8_length = 4;
                    (*arena).utf8_capacity = 4;
                    (*arena).generation = 1;
                    (*arena).flags = u32::from(mode == "decode");
                    *root = 0;
                    *artifact = u64::from(mode != "missing");
                    0
                },
                |snapshot| {
                    EVENTS.with(|events| events.borrow_mut().push(("shape", 0)));
                    assert_eq!(snapshot.nodes.len(), 1);
                    assert_eq!(snapshot.nodes[0].data.as_ref(), "card");
                    if mode == "shape" { Err(-61) } else { Ok(()) }
                },
            );
            if mode == "success" {
                let (snapshot, lease) = result.unwrap();
                text.fill(b'x');
                assert_eq!(snapshot.nodes[0].data.as_ref(), "card");
                EVENTS.with(|events| {
                    assert_eq!(
                        *events.borrow(),
                        [("render", 0), ("shape", 0), ("accept", 0)]
                    )
                });
                drop(lease);
            } else {
                let expected = match mode {
                    "decode" => -40,
                    "shape" => -61,
                    "accept" => -109,
                    "render" => -106,
                    _ => -64,
                };
                assert_eq!(result.err(), Some(expected));
            }
            EVENTS.with(|events| {
                let events = events.borrow();
                let releases: Vec<_> = events.iter().filter(|event| event.0 == "release").collect();
                if matches!(mode, "render" | "missing") {
                    assert!(releases.is_empty());
                } else {
                    assert_eq!(
                        releases,
                        vec![&(
                            "release",
                            match mode {
                                "decode" => -40,
                                "shape" => -61,
                                _ => 0,
                            }
                        )]
                    );
                }
                assert_eq!(
                    events.iter().filter(|event| event.0 == "accept").count(),
                    usize::from(matches!(mode, "success" | "accept"))
                );
            });
        }
    }
}
