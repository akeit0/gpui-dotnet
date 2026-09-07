pub mod abi;
mod accessibility;
#[cfg(all(test, feature = "allocation-tracking"))]
mod allocation_tracking;
mod app_host;
mod arena;
mod components;
mod context_menu;
mod demand;
mod dock;
mod dock_icons;
mod dock_skin;
mod drawing_cache;
mod drawing_commands;
pub mod extension;
mod input;
mod materializer;
#[cfg(test)]
mod native_workloads;
mod overlay;
mod pointer;
mod popover_menu;
mod presence;
mod presentation;
mod resources;
mod row_menu;
mod scrolling;
#[path = "semantic.g.rs"]
mod semantic;
mod slider;
mod snapshot;
mod theme;
mod tooltip;
mod trace;

use std::{mem::size_of, panic::AssertUnwindSafe, ptr};

use abi::{
    ABI_VERSION, GpuiDotnetApiV3, ManagedCallbacks, NativeApplicationCommand,
    NativeExtensionCommand as NativeExtensionCommandAbi, NativeMenuCommand, NativeMenuRecord,
    NativeResourceCommand, NativeThemePayload, RenderArena,
};
use semantic::{
    COMMAND_DOCK_CLOSE_PANEL, COMMAND_DOCK_EXPORT_LAYOUT, COMMAND_DOCK_IMPORT_LAYOUT,
    COMMAND_DOCK_SET_REGION_OPEN, COMMAND_INPUT_BLUR, COMMAND_INPUT_FOCUS,
    COMMAND_INPUT_SELECT_ALL, COMMAND_INPUT_SET_VALUE, COMMAND_INPUT_SET_VALUE_IF_CURRENT,
    COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT, COMMAND_LIST_REFRESH, COMMAND_LIST_RESET,
    COMMAND_LIST_SCROLL_TO_ITEM, COMMAND_LIST_SPLICE, COMMAND_SCROLL_TO_BOTTOM,
    COMMAND_SCROLL_TO_OFFSET, COMMAND_SCROLL_TO_TOP, COMMAND_SLIDER_SET_VALUE, RESOURCE_DOCK,
    RESOURCE_INPUT, RESOURCE_LIST, RESOURCE_SCROLL, RESOURCE_SLIDER, SCHEMA_HASH,
};

static API_V3: GpuiDotnetApiV3 = GpuiDotnetApiV3 {
    struct_size: GpuiDotnetApiV3::struct_size(),
    abi_version: ABI_VERSION,
    schema_hash: SCHEMA_HASH,
    validate_render: Some(validate_render),
    run_application: Some(run_application),
    notify_view: Some(notify_view),
    dispatch_command: Some(dispatch_command),
    dispatch_application_command: Some(dispatch_application_command),
    dispatch_application_menu: Some(dispatch_application_menu),
    supports_extension: Some(supports_extension),
    dispatch_extension_command: Some(dispatch_extension_command),
    invalidate_artifacts: Some(invalidate_artifacts),
};

pub fn api(requested_version: u32) -> *const GpuiDotnetApiV3 {
    if requested_version != ABI_VERSION {
        return ptr::null();
    }
    &API_V3
}

unsafe extern "C" fn dispatch_extension_command(
    view_id: u64,
    command: *const NativeExtensionCommandAbi,
) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| unsafe {
        dispatch_extension_command_inner(view_id, command)
    }))
    .unwrap_or(-99)
}

unsafe fn dispatch_extension_command_inner(
    view_id: u64,
    command: *const NativeExtensionCommandAbi,
) -> i32 {
    const MAX_KEY_LENGTH: i32 = 4096;
    const MAX_PAYLOAD_LENGTH: i32 = 256 * 1024 * 1024;

    let Some(command) = (unsafe { crate::pointer::as_ref(command) }) else {
        return -83;
    };
    if view_id == 0
        || command.owner_view == 0
        || command.command == 0
        || command.schema_version == 0
        || command.schema_hash == 0
        || command.reserved != 0
        || command.extension_id_length <= 0
        || command.extension_id_length > 127
        || command.extension_id.is_null()
        || command.component_kind_length <= 0
        || command.component_kind_length > 127
        || command.component_kind.is_null()
        || command.key_length <= 0
        || command.key_length > MAX_KEY_LENGTH
        || command.key.is_null()
        || command.payload_length < 0
        || command.payload_length > MAX_PAYLOAD_LENGTH
        || (command.payload_length != 0 && command.payload.is_null())
    {
        return -83;
    }

    let extension_id_bytes = unsafe {
        crate::pointer::slice(command.extension_id, command.extension_id_length as usize)
    };
    let component_kind_bytes = unsafe {
        crate::pointer::slice(
            command.component_kind,
            command.component_kind_length as usize,
        )
    };
    let key_bytes = unsafe { crate::pointer::slice(command.key, command.key_length as usize) };
    let (Ok(extension_id), Ok(component_kind), Ok(key)) = (
        std::str::from_utf8(extension_id_bytes),
        std::str::from_utf8(component_kind_bytes),
        std::str::from_utf8(key_bytes),
    ) else {
        return -84;
    };
    if !extension::valid_identifier(extension_id)
        || !extension::valid_identifier(component_kind)
        || key.bytes().any(|byte| byte < 0x20)
    {
        return -84;
    }
    if let Err(status) =
        extension::supports(extension_id, command.schema_version, command.schema_hash)
    {
        return status;
    }

    let payload = if command.payload_length == 0 {
        std::sync::Arc::<[u8]>::from([])
    } else {
        let bytes =
            unsafe { crate::pointer::slice(command.payload, command.payload_length as usize) };
        std::sync::Arc::<[u8]>::from(bytes)
    };
    let command = extension::NativeExtensionCommand {
        resource_key: extension::resource_key(
            command.owner_view,
            extension_id,
            component_kind,
            key,
            command.schema_version,
            command.schema_hash,
        ),
        command: command.command,
        flags: command.flags,
        expected_revision: command.expected_revision,
        payload,
    };
    let provider = extension::provider(extension_id)
        .expect("supports_extension succeeded without an installed provider");
    if !provider.validate_command(&command) {
        return -85;
    }
    app_host::dispatch_extension_command(view_id, command)
}

unsafe extern "C" fn supports_extension(
    id: *const u8,
    id_length: i32,
    version: u32,
    schema_hash: u64,
) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| {
        if id_length <= 0 || id_length > 127 || id.is_null() || version == 0 || schema_hash == 0 {
            return -80;
        }
        let bytes = unsafe { crate::pointer::slice(id, id_length as usize) };
        let Ok(id) = std::str::from_utf8(bytes) else {
            return -80;
        };
        extension::supports(id, version, schema_hash).map_or_else(|status| status, |()| 0)
    }))
    .unwrap_or(-99)
}

unsafe extern "C" fn validate_render(arena: *const RenderArena, root: u32) -> i32 {
    // FFI entrypoints must never unwind across the C boundary; an unexpected panic in any
    // validation path degrades to a status code the caller already knows how to handle.
    std::panic::catch_unwind(AssertUnwindSafe(|| validate_render_inner(arena, root))).unwrap_or(-99)
}

fn validate_render_inner(arena: *const RenderArena, root: u32) -> i32 {
    let Some(arena) = (unsafe { crate::pointer::as_ref(arena) }) else {
        return -1;
    };
    snapshot::validate(arena, root).map_or_else(|status| status, |()| 0)
}

unsafe extern "C" fn notify_view(view_id: u64) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| app_host::notify(view_id))).unwrap_or(-99)
}

unsafe extern "C" fn invalidate_artifacts(
    view_id: u64,
    keys: *const abi::NativeArtifactKey,
    count: i32,
) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| {
        if count <= 0
            || keys.is_null()
            || (count as usize) > isize::MAX as usize / size_of::<abi::NativeArtifactKey>()
        {
            return -1;
        }
        let keys = unsafe { crate::pointer::slice(keys, count as usize) };
        if keys.iter().any(|key| key.source == 0 || key.artifact == 0) {
            return -2;
        }
        app_host::invalidate_artifacts(view_id, keys.to_vec())
    }))
    .unwrap_or(-99)
}

unsafe extern "C" fn dispatch_command(view_id: u64, command: *const NativeResourceCommand) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| unsafe {
        dispatch_command_inner(view_id, command)
    }))
    .unwrap_or(-99)
}

unsafe fn dispatch_command_inner(view_id: u64, command: *const NativeResourceCommand) -> i32 {
    let Some(command) = (unsafe { crate::pointer::as_ref(command) }) else {
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
        RESOURCE_SCROLL => matches!(
            command.command,
            COMMAND_SCROLL_TO_OFFSET..=COMMAND_SCROLL_TO_BOTTOM
        ),
        RESOURCE_LIST => matches!(
            command.command,
            COMMAND_LIST_SCROLL_TO_ITEM..=COMMAND_LIST_REFRESH
        ),
        RESOURCE_INPUT => matches!(
            command.command,
            COMMAND_INPUT_FOCUS..=COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT
        ),
        RESOURCE_SLIDER => command.command == COMMAND_SLIDER_SET_VALUE,
        RESOURCE_DOCK => matches!(
            command.command,
            COMMAND_DOCK_CLOSE_PANEL
                | COMMAND_DOCK_SET_REGION_OPEN
                | COMMAND_DOCK_IMPORT_LAYOUT
                | COMMAND_DOCK_EXPORT_LAYOUT
        ),
        _ => false,
    };
    if !valid_command {
        return -53;
    }
    let payload_valid = match (command.resource_kind, command.command) {
        (RESOURCE_SCROLL, COMMAND_SCROLL_TO_OFFSET) if command.data_length == 0 => {
            if command.a >> 32 != 0 || command.b >> 32 != 0 {
                false
            } else {
                let x = f32::from_bits(command.a as u32);
                let y = f32::from_bits(command.b as u32);
                x.is_finite() && y.is_finite() && x >= 0.0 && y >= 0.0
            }
        }
        (RESOURCE_SCROLL, COMMAND_SCROLL_TO_TOP | COMMAND_SCROLL_TO_BOTTOM) => {
            command.data_length == 0 && command.a == 0 && command.b == 0
        }
        (RESOURCE_LIST, COMMAND_LIST_SCROLL_TO_ITEM) => {
            command.data_length == 0 && command.a >> 32 == 0 && command.b == 0
        }
        (RESOURCE_LIST, COMMAND_LIST_SPLICE) => command.data_length == 0 && command.a >> 32 == 0,
        (RESOURCE_LIST, COMMAND_LIST_RESET) => {
            command.data_length == 0 && command.a >> 32 == 0 && command.b == 0
        }
        (RESOURCE_LIST, COMMAND_LIST_REFRESH) => {
            command.data_length == 0 && command.a >> 32 == 0 && command.b >> 32 == 0
        }
        (RESOURCE_INPUT, COMMAND_INPUT_FOCUS | COMMAND_INPUT_BLUR | COMMAND_INPUT_SELECT_ALL) => {
            command.data_length == 0 && command.a == 0 && command.b == 0
        }
        (RESOURCE_INPUT, COMMAND_INPUT_SET_VALUE) => command.a == 0 && command.b == 0,
        (RESOURCE_INPUT, COMMAND_INPUT_SET_VALUE_IF_CURRENT) => command.a != 0 && command.b <= 3,
        (RESOURCE_INPUT, COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT) => {
            command.a != 0 && command.b >> 2 != 0
        }
        (RESOURCE_SLIDER, COMMAND_SLIDER_SET_VALUE) => {
            let start = f32::from_bits(command.a as u32);
            let end = f32::from_bits((command.a >> 32) as u32);
            command.data_length == 0 && command.b <= 1 && start.is_finite() && end.is_finite()
        }
        // Dock panel commands carry the UTF-8 panel id as data: non-empty,
        // NUL-free, and bounded like resource keys.
        (RESOURCE_DOCK, COMMAND_DOCK_CLOSE_PANEL) => {
            command.a == 0
                && command.b == 0
                && command.data_length > 0
                && command.data_length <= 4096
        }
        (RESOURCE_DOCK, COMMAND_DOCK_SET_REGION_OPEN) => {
            command.data_length == 0 && command.a <= 2 && command.b <= 1
        }
        // Dock layout import carries the JSON document as data, bounded so a
        // corrupt sender cannot queue an unbounded retained payload.
        (RESOURCE_DOCK, COMMAND_DOCK_IMPORT_LAYOUT) => {
            command.a == 0
                && command.b == 0
                && command.data_length > 0
                && command.data_length <= (1 << 20)
        }
        (RESOURCE_DOCK, COMMAND_DOCK_EXPORT_LAYOUT) => {
            command.data_length == 0 && command.a == 0 && command.b == 0
        }
        _ => false,
    };
    if !payload_valid {
        return -54;
    }
    let key = unsafe { crate::pointer::slice(command.key, command.key_length as usize) };
    let Ok(key) = std::str::from_utf8(key) else {
        return -52;
    };
    let data = if command.data_length == 0 {
        ""
    } else {
        let bytes = unsafe { crate::pointer::slice(command.data, command.data_length as usize) };
        let Ok(data) = std::str::from_utf8(bytes) else {
            return -55;
        };
        data
    };
    if command.resource_kind == RESOURCE_DOCK
        && command.data_length != 0
        && (data.is_empty() || data.bytes().any(|byte| byte == 0))
    {
        return -55;
    }
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
    let Some(command) = (unsafe { crate::pointer::as_ref(command) }) else {
        return -60;
    };
    let is_theme = command.command == 8;
    let is_managed_code_update = command.command == 9;
    let is_application_scoped = is_theme || is_managed_code_update;
    if application_id == 0
        || (!is_application_scoped && command.window_id == 0)
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
    if is_theme {
        let payload_valid = command.window_id == 0
            && command.flags == 0
            && no_position
            && no_size
            && command.title_length == size_of::<NativeThemePayload>() as i32;
        if !payload_valid {
            return -62;
        }
        let payload = unsafe { command.title.cast::<NativeThemePayload>().read_unaligned() };
        let Some(theme) = theme::NativeTheme::from_payload(payload) else {
            return -64;
        };
        return app_host::dispatch_application_command(
            application_id,
            app_host::ApplicationCommand::SetTheme(theme),
        );
    }
    if is_managed_code_update {
        let payload_valid =
            command.window_id == 0 && command.flags == 0 && no_title && no_position && no_size;
        if !payload_valid {
            return -62;
        }
        return app_host::dispatch_application_command(
            application_id,
            app_host::ApplicationCommand::ManagedCodeUpdated,
        );
    }

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
        let bytes = unsafe { crate::pointer::slice(command.title, command.title_length as usize) };
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

unsafe extern "C" fn dispatch_application_menu(
    application_id: u64,
    command: *const NativeMenuCommand,
) -> i32 {
    std::panic::catch_unwind(AssertUnwindSafe(|| unsafe {
        dispatch_application_menu_inner(application_id, command)
    }))
    .unwrap_or(-99)
}

unsafe fn dispatch_application_menu_inner(
    application_id: u64,
    command: *const NativeMenuCommand,
) -> i32 {
    const MENU: u16 = 1;
    const ACTION: u16 = 2;
    const SEPARATOR: u16 = 3;
    const MAX_MENU_RECORDS: usize = 4096;
    const NO_PARENT: u32 = u32::MAX;

    let Some(command) = (unsafe { crate::pointer::as_ref(command) }) else {
        return -64;
    };
    if application_id == 0
        || command.reserved != 0
        || command.reserved2 != 0
        || command.item_length < 0
        || command.item_length as usize > MAX_MENU_RECORDS
        || (command.item_length != 0 && command.items.is_null())
    {
        return -65;
    }

    let records = if command.item_length == 0 {
        &[]
    } else {
        unsafe { crate::pointer::slice(command.items, command.item_length as usize) }
    };
    let mut children = vec![Vec::new(); records.len()];
    let mut roots = Vec::new();
    let mut titles = Vec::with_capacity(records.len());
    for (index, record) in records.iter().enumerate() {
        if record.flags != 0
            || record.reserved != 0
            || (record.title_length <= 0 && record.kind != SEPARATOR)
            || record.title_length < 0
            || (record.title_length != 0 && record.title.is_null())
            || (record.kind != MENU && record.kind != ACTION && record.kind != SEPARATOR)
        {
            return -66;
        }

        let title = if record.title_length == 0 {
            String::new()
        } else {
            let bytes =
                unsafe { crate::pointer::slice(record.title, record.title_length as usize) };
            let Ok(title) = std::str::from_utf8(bytes) else {
                return -67;
            };
            title.to_owned()
        };
        match record.parent {
            NO_PARENT if record.kind == MENU => roots.push(index),
            NO_PARENT => return -68,
            parent if parent as usize >= index || parent as usize >= records.len() => return -68,
            parent => {
                if records[parent as usize].kind != MENU {
                    return -68;
                }
                children[parent as usize].push(index);
            }
        }
        if record.kind == ACTION && record.action_id == 0 {
            return -69;
        }
        if record.kind != ACTION && record.action_id != 0 {
            return -69;
        }
        titles.push(title);
    }

    fn build_menu(
        index: usize,
        records: &[NativeMenuRecord],
        children: &[Vec<usize>],
        titles: &[String],
    ) -> app_host::ManagedMenu {
        let items = children[index]
            .iter()
            .map(|&child| match records[child].kind {
                MENU => {
                    app_host::ManagedMenuItem::Submenu(build_menu(child, records, children, titles))
                }
                ACTION => app_host::ManagedMenuItem::Action {
                    id: records[child].action_id,
                    title: titles[child].clone(),
                },
                SEPARATOR => app_host::ManagedMenuItem::Separator,
                _ => unreachable!("menu record kind was validated"),
            })
            .collect();
        app_host::ManagedMenu {
            title: titles[index].clone(),
            items,
        }
    }

    let menus = roots
        .into_iter()
        .map(|index| build_menu(index, records, &children, &titles))
        .collect();
    app_host::dispatch_application_command(
        application_id,
        app_host::ApplicationCommand::SetMenuBar(menus),
    )
}

unsafe extern "C" fn run_application(
    application_id: u64,
    callbacks: *const ManagedCallbacks,
) -> i32 {
    if application_id == 0 || !crate::pointer::valid(callbacks.cast::<u32>(), 1) {
        return -20;
    }

    // Read the declared size before trusting the full structure: a caller that only supplies a
    // prefix must not make us read past its allocation.
    let declared_size = unsafe { callbacks.cast::<u32>().read() };
    if declared_size < size_of::<ManagedCallbacks>() as u32 || !crate::pointer::valid(callbacks, 1)
    {
        return -21;
    }

    let callbacks = unsafe { *callbacks };
    if callbacks.render.is_none()
        || callbacks.click.is_none()
        || callbacks.list_render_range.is_none()
        || callbacks.dynamic_frame.is_none()
        || callbacks.control_event.is_none()
        || callbacks.application_started.is_none()
        || callbacks.window_closed.is_none()
        || callbacks.menu_action.is_none()
        || callbacks.render_completed.is_none()
        || callbacks.release_artifact.is_none()
        || callbacks.accept_artifact.is_none()
    {
        return -21;
    }

    std::panic::catch_unwind(AssertUnwindSafe(|| {
        app_host::run(application_id, callbacks)
    }))
    .unwrap_or(-99)
}

#[cfg(test)]
mod tests {
    #[test]
    fn artifact_invalidation_validates_pointer_count_and_identities() {
        let zero = super::abi::NativeArtifactKey {
            source: 0,
            artifact: 1,
        };
        unsafe {
            assert_eq!(super::invalidate_artifacts(1, std::ptr::null(), 1), -1);
            assert_eq!(super::invalidate_artifacts(1, &zero, -1), -1);
            assert_eq!(super::invalidate_artifacts(1, &zero, 0), -1);
            assert_eq!(super::invalidate_artifacts(1, &zero, 1), -2);
        }
    }
    use super::*;

    fn empty_application_command(command: u16) -> NativeApplicationCommand {
        NativeApplicationCommand {
            window_id: 0,
            command,
            flags: 0,
            reserved: 0,
            title: std::ptr::null(),
            title_length: 0,
            reserved2: 0,
            left: 0.0,
            top: 0.0,
            width: 0.0,
            height: 0.0,
        }
    }

    #[test]
    fn managed_code_update_is_an_empty_application_scoped_command() {
        let mut command = empty_application_command(9);
        assert_eq!(
            unsafe { dispatch_application_command_inner(u64::MAX - 1, &command) },
            -40
        );

        command.window_id = 1;
        assert_eq!(
            unsafe { dispatch_application_command_inner(u64::MAX - 1, &command) },
            -62
        );
        command.window_id = 0;
        command.flags = 1;
        assert_eq!(
            unsafe { dispatch_application_command_inner(u64::MAX - 1, &command) },
            -62
        );
    }

    #[test]
    fn default_host_does_not_advertise_optional_extensions() {
        let id = b"gpui.net.editor";
        assert_eq!(
            unsafe { supports_extension(id.as_ptr(), id.len() as i32, 1, 1) },
            -81
        );
        assert_eq!(unsafe { supports_extension(id.as_ptr(), 128, 1, 1) }, -80);
    }

    fn dock_command(kind: u16, command: u16, a: u64, b: u64, data: &[u8]) -> NativeResourceCommand {
        // Well-formed key routing is covered elsewhere; an unknown view id
        // proves validation passed by reaching the view lookup (-30).
        static KEY: &[u8] = b"dock";
        NativeResourceCommand {
            owner_view: 1,
            resource_kind: kind,
            command,
            key: KEY.as_ptr(),
            key_length: KEY.len() as i32,
            data: data.as_ptr(),
            data_length: data.len() as i32,
            reserved: 0,
            a,
            b,
        }
    }

    #[test]
    fn conditional_input_commands_validate_revision_policies_and_utf8_before_routing() {
        for policies in 0..=3 {
            let command = dock_command(
                RESOURCE_INPUT,
                COMMAND_INPUT_SET_VALUE_IF_CURRENT,
                1,
                policies,
                b"value",
            );
            assert_eq!(
                unsafe { dispatch_command_inner(u64::MAX - 1, &command) },
                -30
            );
        }
        for (revision, policies) in [(0, 0), (0, 3), (1, 4), (1, u64::MAX)] {
            let command = dock_command(
                RESOURCE_INPUT,
                COMMAND_INPUT_SET_VALUE_IF_CURRENT,
                revision,
                policies,
                b"",
            );
            assert_eq!(
                unsafe { dispatch_command_inner(u64::MAX - 1, &command) },
                -54
            );
        }
        let command = dock_command(
            RESOURCE_INPUT,
            COMMAND_INPUT_SET_VALUE_IF_CURRENT,
            1,
            0,
            &[0xff],
        );
        assert_eq!(
            unsafe { dispatch_command_inner(u64::MAX - 1, &command) },
            -55
        );
    }

    #[test]
    fn dock_controller_commands_validate_shape_before_routing() {
        // -30 is the unknown-view status: validation passed, routing failed.
        let panel = b"editor";
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_CLOSE_PANEL, 0, 0, panel),
                )
            },
            -30
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_SET_REGION_OPEN, 1, 1, &[]),
                )
            },
            -30
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_IMPORT_LAYOUT, 0, 0, b"{}"),
                )
            },
            -30
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_EXPORT_LAYOUT, 0, 0, &[]),
                )
            },
            -30
        );

        assert_eq!(
            unsafe {
                dispatch_command_inner(u64::MAX - 1, &dock_command(RESOURCE_DOCK, 44, 0, 0, &[]))
            },
            -53
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_CLOSE_PANEL, 0, 0, &[]),
                )
            },
            -54
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_CLOSE_PANEL, 1, 0, panel),
                )
            },
            -54
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_SET_REGION_OPEN, 3, 0, &[]),
                )
            },
            -54
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_SET_REGION_OPEN, 0, 2, &[]),
                )
            },
            -54
        );
        assert_eq!(
            unsafe {
                dispatch_command_inner(
                    u64::MAX - 1,
                    &dock_command(RESOURCE_DOCK, COMMAND_DOCK_CLOSE_PANEL, 0, 0, b"a\0b"),
                )
            },
            -55
        );
    }

    #[test]
    fn acknowledged_input_commands_validate_request_ids_and_revisions() {
        for (revision, packed, expected) in [
            (1, 4, -30),
            (u64::MAX, u64::MAX, -30),
            (0, 4, -54),
            (1, 0, -54),
            (1, 3, -54),
        ] {
            let command = dock_command(
                RESOURCE_INPUT,
                COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT,
                revision,
                packed,
                b"text",
            );
            assert_eq!(
                unsafe { dispatch_command_inner(u64::MAX - 1, &command) },
                expected
            );
        }
        let command = dock_command(
            RESOURCE_INPUT,
            COMMAND_INPUT_SET_VALUE_IF_CURRENT_WITH_RESULT,
            1,
            4,
            &[0xff],
        );
        assert_eq!(
            unsafe { dispatch_command_inner(u64::MAX - 1, &command) },
            -55
        );
    }
}
