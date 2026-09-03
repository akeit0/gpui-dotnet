use gpui_dotnet::abi::GpuiDotnetApiV3;

#[unsafe(no_mangle)]
pub extern "C" fn gpui_dotnet_get_api(requested_version: u32) -> *const GpuiDotnetApiV3 {
    gpui_dotnet::api(requested_version)
}
