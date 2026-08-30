mod abi;
mod app_host;
mod arena;
mod components;
mod materializer;
#[path = "semantic.g.rs"]
mod semantic;
mod snapshot;

use std::{mem::size_of, panic::AssertUnwindSafe, ptr};

use abi::{ABI_VERSION, GpuiDotnetApiV2, ManagedCallbacks, RenderArena};
use semantic::SCHEMA_HASH;

static API_V2: GpuiDotnetApiV2 = GpuiDotnetApiV2 {
    struct_size: GpuiDotnetApiV2::struct_size(),
    abi_version: ABI_VERSION,
    schema_hash: SCHEMA_HASH,
    validate_render: Some(validate_render),
    run_application: Some(run_application),
    notify_view: Some(notify_view),
};

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV2 {
    if requested_version != ABI_VERSION {
        return ptr::null();
    }

    &API_V2
}

unsafe extern "C" fn validate_render(arena: *const RenderArena, root: u32) -> i32 {
    let Some(arena) = (unsafe { arena.as_ref() }) else {
        return -1;
    };

    snapshot::validate(arena, root).map_or_else(|status| status, |()| 0)
}

unsafe extern "C" fn notify_view(view_id: u64) -> i32 {
    app_host::notify(view_id)
}

unsafe extern "C" fn run_application(
    view_id: u64,
    callbacks: *const ManagedCallbacks,
    title: *const u8,
    title_length: i32,
    width: f32,
    height: f32,
) -> i32 {
    if callbacks.is_null()
        || title.is_null()
        || title_length < 0
        || !width.is_finite()
        || !height.is_finite()
        || width <= 0.0
        || height <= 0.0
    {
        return -20;
    }

    let callbacks = unsafe { *callbacks };
    if callbacks.struct_size < size_of::<ManagedCallbacks>() as u32
        || callbacks.render.is_none()
        || callbacks.click.is_none()
        || callbacks.shutdown.is_none()
    {
        return -21;
    }

    let title_bytes = unsafe { std::slice::from_raw_parts(title, title_length as usize) };
    let Ok(title) = std::str::from_utf8(title_bytes) else {
        return -22;
    };

    std::panic::catch_unwind(AssertUnwindSafe(|| {
        app_host::run(view_id, callbacks, title, width, height)
    }))
    .unwrap_or(-99)
}
