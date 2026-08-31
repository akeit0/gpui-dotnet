mod abi;
mod app_host;
mod arena;
mod components;
mod context_menu;
mod input;
mod materializer;
mod popover_menu;
mod resources;
mod scrolling;
#[path = "semantic.g.rs"]
mod semantic;
mod snapshot;
mod tooltip;
mod trace;

use std::{mem::size_of, panic::AssertUnwindSafe, ptr};

use abi::{
    ABI_VERSION, GpuiDotnetApiV5, ManagedCallbacks, NativeApplicationCommand,
    NativeResourceCommand, RenderArena,
};
use semantic::SCHEMA_HASH;

static API_V5: GpuiDotnetApiV5 = GpuiDotnetApiV5 {
    struct_size: GpuiDotnetApiV5::struct_size(),
    abi_version: ABI_VERSION,
    schema_hash: SCHEMA_HASH,
    validate_render: Some(validate_render),
    run_application: Some(run_application),
    notify_view: Some(notify_view),
    dispatch_command: Some(dispatch_command),
    dispatch_application_command: Some(dispatch_application_command),
};

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV5 {
    if requested_version != ABI_VERSION {
        return ptr::null();
    }
    &API_V5
}

unsafe extern "C" fn validate_render(arena: *const RenderArena, root: u32) -> i32 {
    // FFI entrypoints must never unwind across the C boundary; an unexpected panic in any
    // validation path degrades to a status code the caller already knows how to handle.
    std::panic::catch_unwind(AssertUnwindSafe(|| validate_render_inner(arena, root))).unwrap_or(-99)
}

fn validate_render_inner(arena: *const RenderArena, root: u32) -> i32 {
    let Some(arena) = (unsafe { arena.as_ref() }) else {
        return -1;
    };
    snapshot::validate(arena, root).map_or_else(|status| status, |()| 0)
}

unsafe extern "C" fn notify_view(view_id: u64) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| app_host::notify(view_id))).unwrap_or(-99)
}

unsafe extern "C" fn dispatch_command(view_id: u64, command: *const NativeResourceCommand) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| unsafe {
        dispatch_command_inner(view_id, command)
    }))
    .unwrap_or(-99)
}

unsafe fn dispatch_command_inner(view_id: u64, command: *const NativeResourceCommand) -> i32 {
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

unsafe extern "C" fn dispatch_application_command(
    application_id: u64,
    command: *const NativeApplicationCommand,
) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| unsafe {
        dispatch_application_command_inner(application_id, command)
    }))
    .unwrap_or(-99)
}

unsafe fn dispatch_application_command_inner(
    application_id: u64,
    command: *const NativeApplicationCommand,
) -> i32 {
    let Some(command) = (unsafe { command.as_ref() }) else {
        return -60;
    };
    if application_id == 0
        || command.window_id == 0
        || command.reserved != 0
        || command.reserved2 != 0
        || command.title_length < 0
        || (command.title_length != 0 && command.title.is_null())
    {
        return -61;
    }

    let no_title = command.title_length == 0;
    let no_position = command.left == 0.0 && command.top == 0.0;
    let no_size = command.width == 0.0 && command.height == 0.0;
    let title_bar_style = (command.flags >> 2) & 0b11;
    let size_valid = command.width.is_finite()
        && command.height.is_finite()
        && command.width > 0.0
        && command.height > 0.0;
    let payload_valid = match command.command {
        1 => {
            command.flags & !0b1111 == 0
                && title_bar_style <= 2
                && !no_title
                && size_valid
                && if command.flags & 1 != 0 {
                    command.left.is_finite() && command.top.is_finite()
                } else {
                    no_position
                }
        }
        2 | 3 | 6 | 7 => command.flags == 0 && no_title && no_position && no_size,
        4 => command.flags == 0 && !no_title && no_position && no_size,
        5 => command.flags == 0 && no_title && no_position && size_valid,
        _ => false,
    };
    if !payload_valid {
        return -62;
    }

    let title = if no_title {
        None
    } else {
        let bytes =
            unsafe { std::slice::from_raw_parts(command.title, command.title_length as usize) };
        let Ok(title) = std::str::from_utf8(bytes) else {
            return -63;
        };
        Some(title.to_owned())
    };

    let message = match command.command {
        1 => app_host::ApplicationCommand::Open {
            window_id: command.window_id,
            title: title.expect("validated open title"),
            left: (command.flags & 1 != 0).then_some(command.left),
            top: (command.flags & 1 != 0).then_some(command.top),
            width: command.width,
            height: command.height,
            activate: command.flags & 2 != 0,
            title_bar_style: match title_bar_style {
                0 => app_host::WindowTitleBarStyle::System,
                1 => app_host::WindowTitleBarStyle::Custom,
                2 => app_host::WindowTitleBarStyle::Hidden,
                _ => unreachable!("title-bar style was validated"),
            },
        },
        2 => app_host::ApplicationCommand::Close(command.window_id),
        3 => app_host::ApplicationCommand::Activate(command.window_id),
        6 => app_host::ApplicationCommand::Minimize(command.window_id),
        7 => app_host::ApplicationCommand::ToggleMaximize(command.window_id),
        4 => app_host::ApplicationCommand::SetTitle {
            window_id: command.window_id,
            title: title.expect("validated title update"),
        },
        5 => app_host::ApplicationCommand::Resize {
            window_id: command.window_id,
            width: command.width,
            height: command.height,
        },
        _ => unreachable!("command kind was validated"),
    };
    app_host::dispatch_application_command(application_id, message)
}

unsafe extern "C" fn run_application(
    application_id: u64,
    callbacks: *const ManagedCallbacks,
) -> i32 {
    if application_id == 0 || callbacks.is_null() {
        return -20;
    }

    // Read the declared size before trusting the full structure: a caller that only supplies a
    // prefix must not make us read past its allocation.
    let declared_size = unsafe { callbacks.cast::<u32>().read() };
    if declared_size < size_of::<ManagedCallbacks>() as u32 {
        return -21;
    }

    let callbacks = unsafe { *callbacks };
    if callbacks.render.is_none()
        || callbacks.click.is_none()
        || callbacks.list_render_range.is_none()
        || callbacks.control_event.is_none()
        || callbacks.application_started.is_none()
        || callbacks.window_closed.is_none()
    {
        return -21;
    }

    std::panic::catch_unwind(AssertUnwindSafe(|| {
        app_host::run(application_id, callbacks)
    }))
    .unwrap_or(-99)
}
