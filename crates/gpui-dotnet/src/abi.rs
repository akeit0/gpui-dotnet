use std::mem::size_of;

pub const ABI_VERSION: u32 = 3;
pub const ARENA_FLAG_NATIVE_OWNED: u32 = 1;
pub const RENDER_GROW_REQUIRED: i32 = 1;

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

#[repr(C)]
#[derive(Clone, Copy)]
pub struct NativeMenuCommand {
    pub items: *const NativeMenuRecord,
    pub item_length: i32,
    pub reserved: u32,
    pub reserved2: u32,
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

pub type ManagedRenderFn = unsafe extern "C" fn(u64, *mut RenderArena, *mut u32) -> i32;
pub type ManagedClickFn = unsafe extern "C" fn(u64, u64, u64, *const NativeClickEvent) -> i32;
pub type ManagedListRenderRangeFn =
    unsafe extern "C" fn(u64, u64, u32, u32, *mut RenderArena, *mut u32) -> i32;
pub type ManagedDynamicFrameFn = unsafe extern "C" fn(u64, u32) -> i32;
pub type ManagedControlEventFn = unsafe extern "C" fn(u64, u64, *const NativeControlEvent) -> i32;
pub type ManagedApplicationStartedFn = unsafe extern "C" fn(u64) -> i32;
pub type ManagedWindowClosedFn = unsafe extern "C" fn(u64, u64, i32) -> i32;
pub type ManagedMenuActionFn = unsafe extern "C" fn(u64, u64) -> i32;

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
}

pub type ValidateRenderFn = unsafe extern "C" fn(*const RenderArena, u32) -> i32;
pub type RunApplicationFn = unsafe extern "C" fn(u64, *const ManagedCallbacks) -> i32;
pub type NotifyViewFn = unsafe extern "C" fn(u64) -> i32;
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
}

impl GpuiDotnetApiV3 {
    pub const fn struct_size() -> u32 {
        size_of::<Self>() as u32
    }
}
