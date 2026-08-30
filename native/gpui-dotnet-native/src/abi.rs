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
    pub reserved: u32,
    pub a: u64,
    pub b: u64,
}

pub type ManagedRenderFn = unsafe extern "C" fn(u64, *mut RenderArena, *mut u32) -> i32;
pub type ManagedClickFn = unsafe extern "C" fn(u64, u64, u64, *const NativeClickEvent) -> i32;
pub type ManagedListRenderRangeFn =
    unsafe extern "C" fn(u64, u64, u32, u32, *mut RenderArena, *mut u32) -> i32;
pub type ManagedShutdownFn = unsafe extern "C" fn(u64) -> i32;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct ManagedCallbacks {
    pub struct_size: u32,
    pub render: Option<ManagedRenderFn>,
    pub click: Option<ManagedClickFn>,
    pub list_render_range: Option<ManagedListRenderRangeFn>,
    pub shutdown: Option<ManagedShutdownFn>,
}

pub type ValidateRenderFn = unsafe extern "C" fn(*const RenderArena, u32) -> i32;
pub type RunApplicationFn =
    unsafe extern "C" fn(u64, *const ManagedCallbacks, *const u8, i32, f32, f32) -> i32;
pub type NotifyViewFn = unsafe extern "C" fn(u64) -> i32;
pub type DispatchCommandFn = unsafe extern "C" fn(u64, *const NativeResourceCommand) -> i32;

#[repr(C)]
pub struct GpuiDotnetApiV3 {
    pub struct_size: u32,
    pub abi_version: u32,
    pub schema_hash: u64,
    pub validate_render: Option<ValidateRenderFn>,
    pub run_application: Option<RunApplicationFn>,
    pub notify_view: Option<NotifyViewFn>,
    pub dispatch_command: Option<DispatchCommandFn>,
}

impl GpuiDotnetApiV3 {
    pub const fn struct_size() -> u32 {
        size_of::<Self>() as u32
    }
}
