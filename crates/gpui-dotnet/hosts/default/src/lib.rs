use gpui_dotnet::abi::GpuiDotnetApiV2;

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV2 {
    gpui_dotnet::api(requested_version)
}
