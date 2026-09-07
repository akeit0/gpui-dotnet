use super::*;
use std::{hint::black_box, time::Instant};

fn prepare_capture() {
    ARTIFACTS.with(|capture| {
        *capture.borrow_mut() = ArtifactCapture {
            clickable: true,
            requests: Vec::with_capacity(4096),
            accepts: Vec::with_capacity(4096),
            releases: Vec::with_capacity(4096),
            ..Default::default()
        };
    });
}

fn report(label: &str, samples: &mut [f64]) {
    samples.sort_by(f64::total_cmp);
    println!("{label}: median={:.2} us/op", samples[samples.len() / 2]);
}

fn measure(label: &str, mut operation: impl FnMut()) {
    let mut samples = Vec::new();
    for batch in 0..9 {
        let start = Instant::now();
        for _ in 0..64 {
            operation();
        }
        let elapsed = start.elapsed().as_secs_f64() * 1e6 / 64.;
        if batch >= 4 {
            samples.push(elapsed);
        }
    }
    report(label, &mut samples);
}

#[test]
#[ignore = "native timing probe; run explicitly in Release with --nocapture"]
fn native_workload_measurements() {
    for rows in [48, 512] {
        prepare_capture();
        // A zero descriptor is a valid empty output slot for the managed callback.
        let mut arena: crate::abi::RenderArena = unsafe { std::mem::zeroed() };
        let mut root = 0;
        let mut artifact = 0;
        assert_eq!(
            unsafe { publish_test_range(1, 1, 1, 0, rows, &mut arena, &mut root, &mut artifact) },
            0
        );
        let mut snapshot = ValidatedSnapshot::default();
        let mut strings = RetainedStrings::default();
        let mut scratch = SnapshotScratch::default();
        measure(&format!("decode-{rows}-rows-warm"), || {
            snapshot
                .decode_into(black_box(&arena), root, &mut strings, &mut scratch)
                .unwrap();
            black_box(&snapshot);
        });
        println!(
            "decode-{rows}-rows: retained-buffer-capacity={} bytes",
            snapshot.buffer_capacity_bytes() + scratch.buffer_capacity_bytes()
        );

        prepare_capture();
        let mut config = configuration(Some(1));
        config.batch_size = rows as usize;
        config.item_count = rows as usize;
        let mut resource = ManagedListResource::new(1, artifact_callbacks(), &config, 1);
        measure(&format!("load-release-{rows}-rows"), || {
            resource.load_batch(0).unwrap();
            resource.clear_batches();
        });
        println!(
            "load-release-{rows}-rows: empty-cache-scratch-capacity={} bytes",
            resource.scratch.buffer_capacity_bytes()
        );
        ARTIFACTS.with(|capture| {
            let capture = capture.borrow();
            assert_eq!(capture.requests.len(), capture.accepts.len());
            assert_eq!(capture.requests.len(), capture.releases.len());
        });
    }

    for count in [16, 128, 512] {
        let mut samples = Vec::new();
        let mut retained_before = 0;
        let mut retained_after = 0;
        for iteration in 0..9 {
            prepare_capture();
            let mut config = configuration(Some(1));
            config.item_count = count * 48;
            let mut resource = ManagedListResource::new(1, artifact_callbacks(), &config, 1);
            for batch in 0..count {
                resource.use_clock += 1;
                resource.load_batch((batch * 48) as u32).unwrap();
            }
            let buffers = |resource: &ManagedListResource| {
                resource
                    .batches
                    .values()
                    .map(|batch| batch.snapshot.buffer_capacity_bytes())
                    .sum::<usize>()
                    + resource.scratch.buffer_capacity_bytes()
            };
            retained_before = buffers(&resource);
            resource.begin_frame();
            let start = Instant::now();
            resource.trim_batches();
            let elapsed = start.elapsed().as_secs_f64() * 1e6;
            retained_after = buffers(&resource);
            assert_eq!(resource.batches.len(), 4);
            assert_eq!(resource.telemetry.batch_evictions, (count - 4) as u64);
            if iteration >= 4 {
                samples.push(elapsed);
            }
        }
        report(&format!("trim-{count}-idle-batches"), &mut samples);
        println!(
            "trim-{count}-idle-batches: retained-buffer-capacity={retained_before}->{retained_after} bytes"
        );
    }
}
