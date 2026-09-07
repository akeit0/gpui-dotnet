//! Opt-in current-thread Rust allocator counters. Never compiled into the shipped library.

use std::{
    alloc::{GlobalAlloc, Layout, System},
    cell::Cell,
};

#[derive(Clone, Copy, Default, Debug)]
pub(crate) struct Counts {
    pub(crate) allocations: u64,
    pub(crate) reallocations: u64,
    pub(crate) requested_bytes: u64,
}

thread_local! {
    static ACTIVE: Cell<Option<Counts>> = const { Cell::new(None) };
}

struct CountingAllocator;

#[global_allocator]
static ALLOCATOR: CountingAllocator = CountingAllocator;

fn record(bytes: usize, reallocation: bool) {
    // TLS access and integer updates allocate nothing. During thread teardown, stop counting.
    let _ = ACTIVE.try_with(|active| {
        if let Some(mut counts) = active.get() {
            if reallocation {
                counts.reallocations = counts.reallocations.wrapping_add(1);
            } else {
                counts.allocations = counts.allocations.wrapping_add(1);
            }
            counts.requested_bytes = counts.requested_bytes.wrapping_add(bytes as u64);
            active.set(Some(counts));
        }
    });
}

unsafe impl GlobalAlloc for CountingAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let result = unsafe { System.alloc(layout) };
        if !result.is_null() {
            record(layout.size(), false);
        }
        result
    }

    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        let result = unsafe { System.alloc_zeroed(layout) };
        if !result.is_null() {
            record(layout.size(), false);
        }
        result
    }

    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        let result = unsafe { System.realloc(ptr, layout, new_size) };
        if !result.is_null() {
            record(new_size, true);
        }
        result
    }

    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        unsafe { System.dealloc(ptr, layout) };
    }
}

pub(crate) fn count(operation: impl FnOnce()) -> Counts {
    struct Reset;
    impl Drop for Reset {
        fn drop(&mut self) {
            ACTIVE.with(|active| active.set(None));
        }
    }
    ACTIVE.with(|active| {
        assert!(active.get().is_none(), "allocation measurement cannot nest");
        active.set(Some(Counts::default()));
    });
    let reset = Reset;
    operation();
    let counts = ACTIVE.with(|active| active.get().unwrap());
    drop(reset);
    counts
}

#[test]
fn counter_tracks_successful_allocations_and_full_reallocation_requests() {
    let counts = count(|| unsafe {
        let layout = Layout::from_size_align(32, 8).unwrap();
        let ptr = std::alloc::alloc(layout);
        assert!(!ptr.is_null());
        let ptr = std::alloc::realloc(ptr, layout, 64);
        assert!(!ptr.is_null());
        std::alloc::dealloc(ptr, Layout::from_size_align(64, 8).unwrap());
        let ptr = std::alloc::alloc_zeroed(layout);
        assert!(!ptr.is_null());
        std::alloc::dealloc(ptr, layout);
    });
    assert_eq!(counts.allocations, 2);
    assert_eq!(counts.reallocations, 1);
    assert_eq!(counts.requested_bytes, 128);
    assert_eq!(count(|| {}).requested_bytes, 0);
}

#[test]
fn counter_restores_disabled_state_after_unwind() {
    let _ = std::panic::catch_unwind(|| count(|| panic!("measurement failed")));
    assert_eq!(count(|| {}).requested_bytes, 0);
}
