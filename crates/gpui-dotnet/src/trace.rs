//! Coarse per-stage performance trace for the native frame pipeline.
//!
//! Enabled with `GPUI_DOTNET_TRACE=1`. When disabled, a span costs one atomic load and the
//! per-frame report is skipped entirely. When enabled, each frame reports the wall time spent
//! in every instrumented stage plus cumulative list cache telemetry, on stderr.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::time::Instant;

#[derive(Clone, Copy)]
pub(crate) enum Stage {
    /// The managed render callback (root tree + retained child fragments), including its
    /// growth-retry attempts.
    ManagedRender = 0,
    /// Snapshot validation + decode on the native side.
    SnapshotDecode = 1,
    /// Resource retention bookkeeping after a snapshot commit.
    Retain = 2,
    /// Materializing the GPUI element tree from the committed snapshot.
    Materialize = 3,
    /// Loading one list batch through the reverse-FFI renderer.
    ListBatchLoad = 4,
}

const STAGE_COUNT: usize = 5;
const STAGE_NAMES: [&str; STAGE_COUNT] = [
    "managed_render",
    "decode",
    "retain",
    "materialize",
    "batch_load",
];

struct Accum {
    nanos: AtomicU64,
    count: AtomicU64,
}

const ZERO_ACCUM: Accum = Accum {
    nanos: AtomicU64::new(0),
    count: AtomicU64::new(0),
};

static STAGE_NANOS: [AtomicU64; STAGE_COUNT] = [
    ZERO_ACCUM.nanos,
    ZERO_ACCUM.nanos,
    ZERO_ACCUM.nanos,
    ZERO_ACCUM.nanos,
    ZERO_ACCUM.nanos,
];
static STAGE_COUNTS: [AtomicU64; STAGE_COUNT] = [
    ZERO_ACCUM.count,
    ZERO_ACCUM.count,
    ZERO_ACCUM.count,
    ZERO_ACCUM.count,
    ZERO_ACCUM.count,
];

static ENABLED: AtomicBool = AtomicBool::new(false);
static FRAMES: AtomicU64 = AtomicU64::new(0);

pub(crate) fn enabled() -> bool {
    ENABLED.load(Ordering::Relaxed)
}

/// Reads `GPUI_DOTNET_TRACE` once at application startup. Trace output goes to stderr and is
/// purely diagnostic; it never affects frame results.
pub(crate) fn init_from_env() {
    let on = std::env::var("GPUI_DOTNET_TRACE")
        .map(|value| value == "1" || value.eq_ignore_ascii_case("true"))
        .unwrap_or(false);
    ENABLED.store(on, Ordering::Relaxed);
}

#[cfg(test)]
pub(crate) fn set_enabled_for_tests(on: bool) {
    ENABLED.store(on, Ordering::Relaxed);
}

/// Times its enclosing scope into a stage accumulator when tracing is enabled.
pub(crate) struct Span {
    stage: usize,
    start: Option<Instant>,
}

impl Span {
    #[inline]
    pub(crate) fn new(stage: Stage) -> Self {
        Self {
            stage: stage as usize,
            start: enabled().then(Instant::now),
        }
    }
}

/// Convenience constructor so call sites read `let _stage = trace::span(Stage::X);`.
#[inline]
pub(crate) fn span(stage: Stage) -> Span {
    Span::new(stage)
}

impl Drop for Span {
    fn drop(&mut self) {
        if let Some(start) = self.start.take() {
            let elapsed = start.elapsed().as_nanos() as u64;
            STAGE_NANOS[self.stage].fetch_add(elapsed, Ordering::Relaxed);
            STAGE_COUNTS[self.stage].fetch_add(1, Ordering::Relaxed);
        }
    }
}

/// Reports and resets the stage accumulators for the frame that just finished. `extra` carries
/// caller-provided cumulative diagnostics (list cache telemetry) appended to the line.
pub(crate) fn end_frame(extra: &[(&'static str, u64)]) {
    if !enabled() {
        return;
    }
    let frame = FRAMES.fetch_add(1, Ordering::Relaxed) + 1;
    let mut line = format!("[gpui] frame {frame}:");
    for index in 0..STAGE_COUNT {
        let nanos = STAGE_NANOS[index].swap(0, Ordering::Relaxed);
        let count = STAGE_COUNTS[index].swap(0, Ordering::Relaxed);
        if count != 0 {
            line.push_str(&format!(
                " {}={:.3}ms({count})",
                STAGE_NAMES[index],
                nanos as f64 / 1e6
            ));
        }
    }
    for (label, value) in extra {
        line.push_str(&format!(" {label}={value}"));
    }
    eprintln!("{line}");
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    static TEST_LOCK: Mutex<()> = Mutex::new(());

    fn reset_trace_state() {
        for index in 0..STAGE_COUNT {
            STAGE_NANOS[index].store(0, Ordering::Relaxed);
            STAGE_COUNTS[index].store(0, Ordering::Relaxed);
        }
        FRAMES.store(0, Ordering::Relaxed);
    }

    #[test]
    fn spans_accumulate_only_when_enabled() {
        let _lock = TEST_LOCK.lock().unwrap();
        reset_trace_state();
        set_enabled_for_tests(false);
        {
            let _span = Span::new(Stage::Materialize);
        }
        assert_eq!(
            STAGE_COUNTS[Stage::Materialize as usize].load(Ordering::Relaxed),
            0
        );

        set_enabled_for_tests(true);
        {
            let _span = Span::new(Stage::Materialize);
        }
        assert_eq!(
            STAGE_COUNTS[Stage::Materialize as usize].load(Ordering::Relaxed),
            1
        );

        // Drain so other tests observe a clean slate.
        for index in 0..STAGE_COUNT {
            STAGE_NANOS[index].swap(0, Ordering::Relaxed);
            STAGE_COUNTS[index].swap(0, Ordering::Relaxed);
        }
        set_enabled_for_tests(false);
    }

    #[test]
    fn end_frame_resets_and_reports_stages() {
        let _lock = TEST_LOCK.lock().unwrap();
        reset_trace_state();
        set_enabled_for_tests(true);
        {
            let _span = Span::new(Stage::ListBatchLoad);
        }
        end_frame(&[("rows", 7)]);
        assert_eq!(
            STAGE_COUNTS[Stage::ListBatchLoad as usize].load(Ordering::Relaxed),
            0
        );
        set_enabled_for_tests(false);
    }

    #[test]
    fn disabled_end_frame_is_a_no_op() {
        let _lock = TEST_LOCK.lock().unwrap();
        reset_trace_state();
        set_enabled_for_tests(false);
        end_frame(&[]);
        assert_eq!(FRAMES.load(Ordering::Relaxed), 0);
    }
}
