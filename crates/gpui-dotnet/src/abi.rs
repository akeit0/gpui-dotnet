use std::mem::size_of;

pub const ABI_VERSION: u32 = 11;

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct NativeArtifactKey {
    pub source: u64,
    pub artifact: u64,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct NodeRecord {
    pub component: u16,
    pub flags: u16,
    pub data_offset: u32,
    pub data_length: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct OpRecord {
    pub node: u32,
    pub code: u16,
    pub value_kind: u16,
    pub a: u64,
    pub b: u64,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct ChildRecord {
    pub parent: u32,
    pub child: u32,
}

#[repr(C)]
pub struct RenderArena {
    pub nodes: *mut NodeRecord,
    pub node_length: i32,
    pub node_capacity: i32,
    pub ops: *mut OpRecord,
    pub op_length: i32,
    pub op_capacity: i32,
    pub children: *mut ChildRecord,
    pub child_length: i32,
    pub child_capacity: i32,
    pub utf8: *mut u8,
    pub utf8_length: i32,
    pub utf8_capacity: i32,
    pub generation: u32,
    // ABI 4: flags and the legacy growth-request fields are reserved and must be zero.
    pub flags: u32,
    pub required_node_capacity: i32,
    pub required_op_capacity: i32,
    pub required_child_capacity: i32,
    pub required_utf8_capacity: i32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct NativeClickEvent {
    pub x: f32,
    pub y: f32,
    pub buttons: u32,
    pub modifiers: u32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeResourceCommand {
    pub owner_view: u32,
    pub resource_kind: u16,
    pub command: u16,
    pub key: *const u8,
    pub key_length: i32,
    pub data: *const u8,
    pub data_length: i32,
    pub reserved: u32,
    pub a: u64,
    pub b: u64,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeExtensionCommand {
    pub owner_view: u32,
    pub command: u16,
    pub flags: u16,
    pub schema_version: u32,
    pub reserved: u32,
    pub schema_hash: u64,
    pub expected_revision: u64,
    pub extension_id: *const u8,
    pub extension_id_length: i32,
    pub component_kind: *const u8,
    pub component_kind_length: i32,
    pub key: *const u8,
    pub key_length: i32,
    pub payload: *const u8,
    pub payload_length: i32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeControlEvent {
    pub kind: u16,
    pub flags: u16,
    pub reserved: u32,
    pub revision: u64,
    pub data: *const u8,
    pub data_length: i32,
    pub reserved2: u32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeApplicationCommand {
    pub window_id: u64,
    pub command: u16,
    pub flags: u16,
    pub reserved: u32,
    pub title: *const u8,
    pub title_length: i32,
    pub reserved2: u32,
    pub left: f32,
    pub top: f32,
    pub width: f32,
    pub height: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct NativeThemePayload {
    pub version: u32,
    pub appearance: u32,
    pub background: u32,
    pub text: u32,
    pub text_muted: u32,
    pub text_placeholder: u32,
    pub text_on_accent: u32,
    pub border: u32,
    pub border_variant: u32,
    pub border_focused: u32,
    pub surface_background: u32,
    pub element_background: u32,
    pub element_hover: u32,
    pub element_active: u32,
    pub accent: u32,
    pub info: u32,
    pub info_background: u32,
    pub error: u32,
    pub scrollbar_thumb_background: u32,
    pub scrollbar_track_background: u32,
}

/// Application-scoped image-cache spill budget, carried in the application command's byte
/// pointer like the theme payload. `max_bytes == 0` disables the spill tier (pure live-set);
/// `max_entries == 0` leaves the entry count uncapped (bytes rule alone).
#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeImageCacheBudget {
    pub version: u32,
    pub reserved: u32,
    pub max_bytes: u64,
    pub max_entries: u64,
}

pub const IMAGE_CACHE_BUDGET_VERSION: u32 = 1;
pub const IMAGE_CACHE_BUDGET_COMMAND: u16 = 10;
pub const IMAGE_EVICT_COMMAND: u16 = 11;

/// Validates an image-cache budget command record and copies out the budget. Pure so the
/// contract is unit-testable without a running application; the entry point only enqueues.
pub fn parse_image_cache_budget(command: &NativeApplicationCommand) -> Result<(u64, u64), i32> {
    let payload_valid = command.window_id == 0
        && command.command == IMAGE_CACHE_BUDGET_COMMAND
        && command.flags == 0
        && command.reserved == 0
        && command.reserved2 == 0
        && command.left == 0.0
        && command.top == 0.0
        && command.width == 0.0
        && command.height == 0.0
        && command.title_length == size_of::<NativeImageCacheBudget>() as i32
        && !command.title.is_null();
    if !payload_valid {
        return Err(-62);
    }
    let payload = unsafe {
        command
            .title
            .cast::<NativeImageCacheBudget>()
            .read_unaligned()
    };
    if payload.version != IMAGE_CACHE_BUDGET_VERSION || payload.reserved != 0 {
        return Err(-62);
    }
    Ok((payload.max_bytes, payload.max_entries))
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeMenuCommand {
    pub items: *const NativeMenuRecord,
    pub item_length: i32,
    pub reserved: u32,
    pub reserved2: u32,
    pub generation: u64,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeMenuRecord {
    pub parent: u32,
    pub kind: u16,
    pub flags: u16,
    pub action_id: u64,
    pub title: *const u8,
    pub title_length: i32,
    pub reserved: u32,
}

pub type ManagedRenderFn = unsafe extern "C" fn(u64, *mut RenderArena, *mut u32, *mut u64) -> i32;
pub type ManagedRenderCompletedFn = unsafe extern "C" fn(u64, u64, i32) -> i32;
pub type ManagedClickFn = unsafe extern "C" fn(u64, u64, u64, *const NativeClickEvent) -> i32;
pub type ManagedListRenderRangeFn =
    unsafe extern "C" fn(u64, u64, u64, u32, u32, *mut RenderArena, *mut u32, *mut u64) -> i32;
pub type ManagedReleaseArtifactFn = unsafe extern "C" fn(u64, u64, u64, i32) -> i32;
pub type ManagedAcceptArtifactFn = unsafe extern "C" fn(u64, u64, u64) -> i32;
pub type ManagedDynamicFrameFn = unsafe extern "C" fn(u64, u32) -> i32;
pub type ManagedControlEventFn = unsafe extern "C" fn(u64, u64, *const NativeControlEvent) -> i32;
pub type ManagedApplicationStartedFn = unsafe extern "C" fn(u64) -> i32;
pub type ManagedApplicationReadyFn = unsafe extern "C" fn(u64) -> i32;
pub type ManagedWindowClosedFn = unsafe extern "C" fn(u64, u64, i32) -> i32;
pub type ManagedWindowOpenedFn = unsafe extern "C" fn(u64, u64) -> i32;
pub type ManagedWindowPlacementFn =
    unsafe extern "C" fn(u64, u64, *const NativeWindowPlacement) -> i32;
pub type ManagedMenuActionFn = unsafe extern "C" fn(u64, u64) -> i32;
pub type ManagedMenuAppliedFn = unsafe extern "C" fn(u64, u64) -> i32;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct ManagedCallbacks {
    pub struct_size: u32,
    pub render: Option<ManagedRenderFn>,
    pub click: Option<ManagedClickFn>,
    pub list_render_range: Option<ManagedListRenderRangeFn>,
    pub control_event: Option<ManagedControlEventFn>,
    pub application_started: Option<ManagedApplicationStartedFn>,
    pub window_closed: Option<ManagedWindowClosedFn>,
    pub menu_action: Option<ManagedMenuActionFn>,
    pub dynamic_frame: Option<ManagedDynamicFrameFn>,
    pub render_completed: Option<ManagedRenderCompletedFn>,
    pub release_artifact: Option<ManagedReleaseArtifactFn>,
    pub accept_artifact: Option<ManagedAcceptArtifactFn>,
    pub menu_applied: Option<ManagedMenuAppliedFn>,
    pub window_placement: Option<ManagedWindowPlacementFn>,
    pub window_opened: Option<ManagedWindowOpenedFn>,
    pub application_ready: Option<ManagedApplicationReadyFn>,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct NativeWindowPlacement {
    pub left: f32,
    pub top: f32,
    pub width: f32,
    pub height: f32,
    pub state: u32,
    pub reserved: u32,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn acceptance_callback_extends_the_callback_table() {
        let pointer_size = std::mem::size_of::<usize>();
        assert_eq!(std::mem::size_of::<ManagedCallbacks>(), 16 * pointer_size);
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, application_ready),
            15 * pointer_size
        );
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, window_opened),
            14 * pointer_size
        );
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, window_placement),
            13 * pointer_size
        );
        assert_eq!(std::mem::size_of::<NativeWindowPlacement>(), 24);
        assert_eq!(std::mem::offset_of!(NativeWindowPlacement, state), 16);
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, menu_applied),
            12 * pointer_size
        );
        assert_eq!(std::mem::size_of::<NativeMenuCommand>(), 32);
        assert_eq!(std::mem::offset_of!(NativeMenuCommand, generation), 24);
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, render_completed),
            9 * pointer_size
        );
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, release_artifact),
            10 * pointer_size
        );
        assert_eq!(
            std::mem::offset_of!(ManagedCallbacks, accept_artifact),
            11 * pointer_size
        );
        assert_eq!(std::mem::size_of::<NativeArtifactKey>(), 16);
        assert_eq!(std::mem::offset_of!(NativeArtifactKey, artifact), 8);
        assert_eq!(
            std::mem::offset_of!(GpuiDotnetApiV3, invalidate_artifacts),
            16 + 8 * pointer_size
        );
        assert_eq!(ABI_VERSION, 11);
    }

    #[test]
    fn image_cache_budget_layout_matches_managed_contract() {
        assert_eq!(std::mem::size_of::<NativeImageCacheBudget>(), 24);
        assert_eq!(std::mem::offset_of!(NativeImageCacheBudget, version), 0);
        assert_eq!(std::mem::offset_of!(NativeImageCacheBudget, reserved), 4);
        assert_eq!(std::mem::offset_of!(NativeImageCacheBudget, max_bytes), 8);
        assert_eq!(
            std::mem::offset_of!(NativeImageCacheBudget, max_entries),
            16
        );
        assert_eq!(IMAGE_CACHE_BUDGET_VERSION, 1);
        assert_eq!(IMAGE_CACHE_BUDGET_COMMAND, 10);
    }

    fn budget_command(payload: &NativeImageCacheBudget) -> NativeApplicationCommand {
        NativeApplicationCommand {
            window_id: 0,
            command: IMAGE_CACHE_BUDGET_COMMAND,
            flags: 0,
            reserved: 0,
            title: (payload as *const NativeImageCacheBudget).cast::<u8>(),
            title_length: size_of::<NativeImageCacheBudget>() as i32,
            reserved2: 0,
            left: 0.0,
            top: 0.0,
            width: 0.0,
            height: 0.0,
        }
    }

    #[test]
    fn image_cache_budget_accepts_any_u64_pair() {
        for (max_bytes, max_entries) in [(0, 0), (100 << 20, 64), (u64::MAX, u64::MAX)] {
            let payload = NativeImageCacheBudget {
                version: IMAGE_CACHE_BUDGET_VERSION,
                reserved: 0,
                max_bytes,
                max_entries,
            };
            assert_eq!(
                parse_image_cache_budget(&budget_command(&payload)),
                Ok((max_bytes, max_entries))
            );
        }
    }

    #[test]
    fn image_cache_budget_rejects_malformed_records() {
        let payload = NativeImageCacheBudget {
            version: IMAGE_CACHE_BUDGET_VERSION,
            reserved: 0,
            max_bytes: 1,
            max_entries: 1,
        };
        let command = budget_command(&payload);
        // Wrong command id, scoped window, flags, reserved words, geometry, size, version.
        let mutators: [fn(&mut NativeApplicationCommand); 7] = [
            |command: &mut NativeApplicationCommand| command.command = 8,
            |command: &mut NativeApplicationCommand| command.window_id = 7,
            |command: &mut NativeApplicationCommand| command.flags = 1,
            |command: &mut NativeApplicationCommand| command.reserved = 1,
            |command: &mut NativeApplicationCommand| command.reserved2 = 1,
            |command: &mut NativeApplicationCommand| command.width = 1.0,
            |command: &mut NativeApplicationCommand| command.title_length -= 1,
        ];
        for (index, mutate) in mutators.into_iter().enumerate() {
            let mut probe = command;
            mutate(&mut probe);
            assert_eq!(
                parse_image_cache_budget(&probe),
                Err(-62),
                "mutator {index}"
            );
        }
        let mut bad_version = payload;
        bad_version.version += 1;
        assert_eq!(
            parse_image_cache_budget(&budget_command(&bad_version)),
            Err(-62)
        );
        let mut bad_reserved = payload;
        bad_reserved.reserved = 1;
        assert_eq!(
            parse_image_cache_budget(&budget_command(&bad_reserved)),
            Err(-62)
        );
    }
}

pub type ValidateRenderFn = unsafe extern "C" fn(*const RenderArena, u32) -> i32;
pub type RunApplicationFn = unsafe extern "C" fn(u64, *const ManagedCallbacks) -> i32;
pub type NotifyViewFn = unsafe extern "C" fn(u64) -> i32;
pub type InvalidateArtifactsFn = unsafe extern "C" fn(u64, *const NativeArtifactKey, i32) -> i32;
pub type DispatchCommandFn = unsafe extern "C" fn(u64, *const NativeResourceCommand) -> i32;
pub type DispatchExtensionCommandFn =
    unsafe extern "C" fn(u64, *const NativeExtensionCommand) -> i32;
pub type DispatchApplicationCommandFn =
    unsafe extern "C" fn(u64, *const NativeApplicationCommand) -> i32;
pub type DispatchApplicationMenuFn = unsafe extern "C" fn(u64, *const NativeMenuCommand) -> i32;
pub type SupportsExtensionFn = unsafe extern "C" fn(*const u8, i32, u32, u64) -> i32;

#[repr(C)]
pub struct GpuiDotnetApiV3 {
    pub struct_size: u32,
    pub abi_version: u32,
    pub schema_hash: u64,
    pub validate_render: Option<ValidateRenderFn>,
    pub run_application: Option<RunApplicationFn>,
    pub notify_view: Option<NotifyViewFn>,
    pub dispatch_command: Option<DispatchCommandFn>,
    pub dispatch_application_command: Option<DispatchApplicationCommandFn>,
    pub dispatch_application_menu: Option<DispatchApplicationMenuFn>,
    pub supports_extension: Option<SupportsExtensionFn>,
    pub dispatch_extension_command: Option<DispatchExtensionCommandFn>,
    pub invalidate_artifacts: Option<InvalidateArtifactsFn>,
}

impl GpuiDotnetApiV3 {
    pub const fn struct_size() -> u32 {
        size_of::<Self>() as u32
    }
}
