//! Coarse per-stage performance trace for the native frame pipeline.
//!
//! Enabled with `GPUI_DOTNET_TRACE=1`. When disabled, a span costs one atomic load and the
//! per-frame report is skipped entirely. When enabled, each frame reports the wall time spent
//! in every instrumented stage plus cumulative list cache telemetry, on stderr.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::time::Instant;

#[derive(Clone, Copy)]
pub(crate) enum Stage {
    /// The single managed render callback, including retained child fragments.
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

struct Trace {
    enabled: AtomicBool,
    stages: [Accum; STAGE_COUNT],
    frames: AtomicU64,
}

impl Trace {
    const fn new() -> Self {
        Self {
            enabled: AtomicBool::new(false),
            stages: [const {
                Accum {
                    nanos: AtomicU64::new(0),
                    count: AtomicU64::new(0),
                }
            }; STAGE_COUNT],
            frames: AtomicU64::new(0),
        }
    }

    #[inline]
    fn span(&self, stage: Stage) -> Span<'_> {
        Span {
            accum: &self.stages[stage as usize],
            start: self.enabled.load(Ordering::Relaxed).then(Instant::now),
        }
    }

    fn end_frame(&self, extra: &[(&'static str, u64)]) -> Option<String> {
        if !self.enabled.load(Ordering::Relaxed) {
            return None;
        }
        let frame = self.frames.fetch_add(1, Ordering::Relaxed) + 1;
        let mut line = format!("[gpui] frame {frame}:");
        for (accum, name) in self.stages.iter().zip(STAGE_NAMES) {
            let nanos = accum.nanos.swap(0, Ordering::Relaxed);
            let count = accum.count.swap(0, Ordering::Relaxed);
            if count != 0 {
                line.push_str(&format!(" {name}={:.3}ms({count})", nanos as f64 / 1e6));
            }
        }
        for (label, value) in extra {
            line.push_str(&format!(" {label}={value}"));
        }
        Some(line)
    }
}

static TRACE: Trace = Trace::new();

pub(crate) fn enabled() -> bool {
    TRACE.enabled.load(Ordering::Relaxed)
}

/// Reads `GPUI_DOTNET_TRACE` once at application startup. Trace output goes to stderr and is
/// purely diagnostic; it never affects frame results.
pub(crate) fn init_from_env() {
    let on = std::env::var("GPUI_DOTNET_TRACE")
        .map(|value| value == "1" || value.eq_ignore_ascii_case("true"))
        .unwrap_or(false);
    TRACE.enabled.store(on, Ordering::Relaxed);
}

/// Times its enclosing scope into a stage accumulator when tracing is enabled.
pub(crate) struct Span<'a> {
    accum: &'a Accum,
    start: Option<Instant>,
}

/// Convenience constructor so call sites read `let _stage = trace::span(Stage::X);`.
#[inline]
pub(crate) fn span(stage: Stage) -> Span<'static> {
    TRACE.span(stage)
}

impl Drop for Span<'_> {
    fn drop(&mut self) {
        if let Some(start) = self.start.take() {
            let elapsed = start.elapsed().as_nanos() as u64;
            self.accum.nanos.fetch_add(elapsed, Ordering::Relaxed);
            self.accum.count.fetch_add(1, Ordering::Relaxed);
        }
    }
}

/// Reports and resets the stage accumulators for the frame that just finished. `extra` carries
/// caller-provided cumulative diagnostics (list cache telemetry) appended to the line.
pub(crate) fn end_frame(extra: &[(&'static str, u64)]) {
    if let Some(line) = TRACE.end_frame(extra) {
        eprintln!("{line}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn spans_accumulate_only_when_enabled() {
        let trace = Trace::new();
        {
            let _span = trace.span(Stage::Materialize);
        }
        assert_eq!(
            trace.stages[Stage::Materialize as usize]
                .count
                .load(Ordering::Relaxed),
            0
        );

        trace.enabled.store(true, Ordering::Relaxed);
        {
            let _span = trace.span(Stage::Materialize);
        }
        assert_eq!(
            trace.stages[Stage::Materialize as usize]
                .count
                .load(Ordering::Relaxed),
            1
        );
    }

    #[test]
    fn end_frame_resets_and_reports_stages() {
        let trace = Trace::new();
        trace.enabled.store(true, Ordering::Relaxed);
        {
            let _span = trace.span(Stage::ListBatchLoad);
        }
        let report = trace.end_frame(&[("rows", 7)]).unwrap();
        assert!(report.starts_with("[gpui] frame 1: batch_load="));
        assert!(report.ends_with("ms(1) rows=7"));
        assert_eq!(
            trace.stages[Stage::ListBatchLoad as usize]
                .count
                .load(Ordering::Relaxed),
            0
        );
        assert_eq!(trace.end_frame(&[]).unwrap(), "[gpui] frame 2:");
    }

    #[test]
    fn disabled_end_frame_is_a_no_op() {
        let trace = Trace::new();
        assert!(trace.end_frame(&[]).is_none());
        assert_eq!(trace.frames.load(Ordering::Relaxed), 0);
    }

    #[test]
    fn independent_traces_do_not_share_counts_or_enablement() {
        let first = Trace::new();
        let second = Trace::new();
        first.enabled.store(true, Ordering::Relaxed);
        std::thread::scope(|scope| {
            scope.spawn(|| {
                for _ in 0..100 {
                    let _span = first.span(Stage::Retain);
                }
            });
            scope.spawn(|| {
                for _ in 0..100 {
                    let _span = second.span(Stage::Retain);
                }
            });
        });
        assert_eq!(
            first.stages[Stage::Retain as usize]
                .count
                .load(Ordering::Relaxed),
            100
        );
        assert_eq!(
            second.stages[Stage::Retain as usize]
                .count
                .load(Ordering::Relaxed),
            0
        );
    }
}
