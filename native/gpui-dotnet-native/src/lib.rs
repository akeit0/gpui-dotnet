mod abi;
mod app_host;
mod arena;
mod components;
mod input;
mod materializer;
mod resources;
mod scrolling;
#[path = "semantic.g.rs"]
mod semantic;
mod snapshot;
mod tooltip;

use std::{mem::size_of, panic::AssertUnwindSafe, ptr};

use abi::{ABI_VERSION, GpuiDotnetApiV4, ManagedCallbacks, NativeResourceCommand, RenderArena};
use semantic::SCHEMA_HASH;

static API_V4: GpuiDotnetApiV4 = GpuiDotnetApiV4 {
    struct_size: GpuiDotnetApiV4::struct_size(),
    abi_version: ABI_VERSION,
    schema_hash: SCHEMA_HASH,
    validate_render: Some(validate_render),
    run_application: Some(run_application),
    notify_view: Some(notify_view),
    dispatch_command: Some(dispatch_command),
};

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV4 {
    if requested_version != ABI_VERSION {
        return ptr::null();
    }
    &API_V4
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

unsafe extern "C" fn dispatch_command(view_id: u64, command: *const NativeResourceCommand) -> i32 {
    let Some(command) = (unsafe { command.as_ref() }) else {
        return -50;
    };
    if command.reserved != 0
        || command.owner_view == 0
        || command.key_length <= 0
        || command.key.is_null()
        || command.data_length < 0
        || (command.data_length != 0 && command.data.is_null())
    {
        return -51;
    }
    let valid_command = match command.resource_kind {
        1 => matches!(command.command, 1..=3),
        2 => matches!(command.command, 10..=13),
        3 => matches!(command.command, 20..=23),
        _ => false,
    };
    if !valid_command {
        return -53;
    }
    let payload_valid = match (command.resource_kind, command.command) {
        (1, 1) if command.data_length == 0 => {
            if command.a >> 32 != 0 || command.b >> 32 != 0 {
                false
            } else {
                let x = f32::from_bits(command.a as u32);
                let y = f32::from_bits(command.b as u32);
                x.is_finite() && y.is_finite() && x >= 0.0 && y >= 0.0
            }
        }
        (1, 2 | 3) => command.data_length == 0 && command.a == 0 && command.b == 0,
        (2, 10) => command.data_length == 0 && command.a >> 32 == 0 && command.b == 0,
        (2, 11) => command.data_length == 0 && command.a >> 32 == 0,
        (2, 12) => command.data_length == 0 && command.a >> 32 == 0 && command.b == 0,
        (2, 13) => command.data_length == 0 && command.a >> 32 == 0 && command.b >> 32 == 0,
        (3, 20 | 21 | 23) => command.data_length == 0 && command.a == 0 && command.b == 0,
        (3, 22) => command.a == 0 && command.b == 0,
        _ => false,
    };
    if !payload_valid {
        return -54;
    }
    let key = unsafe { std::slice::from_raw_parts(command.key, command.key_length as usize) };
    let Ok(key) = std::str::from_utf8(key) else {
        return -52;
    };
    let data = if command.data_length == 0 {
        ""
    } else {
        let bytes =
            unsafe { std::slice::from_raw_parts(command.data, command.data_length as usize) };
        let Ok(data) = std::str::from_utf8(bytes) else {
            return -55;
        };
        data
    };
    app_host::dispatch_command(
        view_id,
        resources::ResourceCommand::from_abi(command, key, data),
    )
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
        || callbacks.list_render_range.is_none()
        || callbacks.control_event.is_none()
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
