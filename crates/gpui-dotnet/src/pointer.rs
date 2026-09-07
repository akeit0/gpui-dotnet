//! Structural checks only. The caller still owns allocation validity and the borrow lifetime.

pub(crate) fn valid<T>(pointer: *const T, len: usize) -> bool {
    if len == 0 {
        return true;
    }
    !pointer.is_null()
        && pointer.addr().is_multiple_of(align_of::<T>())
        && len.checked_mul(size_of::<T>()).is_some_and(|bytes| {
            bytes <= isize::MAX as usize && pointer.addr().checked_add(bytes).is_some()
        })
}

pub(crate) unsafe fn as_ref<'a, T>(pointer: *const T) -> Option<&'a T> {
    if valid(pointer, 1) {
        Some(unsafe { &*pointer })
    } else {
        None
    }
}

pub(crate) unsafe fn slice<'a, T>(pointer: *const T, len: usize) -> &'a [T] {
    assert!(valid(pointer, len), "invalid FFI buffer structure");
    if len == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(pointer, len) }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_alignment_size_and_address_overflow_without_dereferencing() {
        let values = [1u64; 2];
        assert!(valid(values.as_ptr(), 2));
        assert!(valid::<u64>(std::ptr::null(), 0));
        assert!(!valid::<u64>(std::ptr::null(), 1));
        assert!(!valid::<u64>(1usize as *const u64, 1));
        assert!(!valid(values.as_ptr(), usize::MAX));
        assert!(!valid::<u64>((usize::MAX - 7) as *const u64, 1));
    }
}
