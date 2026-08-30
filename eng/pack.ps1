param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    'artifacts/native/win-x64/gpui_dotnet_native.dll',
    'artifacts/native/linux-x64/libgpui_dotnet_native.so',
    'artifacts/native/osx-x64/libgpui_dotnet_native.dylib',
    'artifacts/native/osx-arm64/libgpui_dotnet_native.dylib'
)

foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing native package asset: $relative. Stage each CI build with eng/stage-native.ps1 first."
    }
}

& dotnet pack (Join-Path $root 'dotnet/src/Gpui.Net/Gpui.Net.csproj') `
    -c $Configuration `
    -p:BuildGpuiNative=false `
    -o (Join-Path $root 'artifacts/packages')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
