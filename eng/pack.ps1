param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    'artifacts/native/win-x64/gpui_dotnet.dll',
    'artifacts/native/linux-x64/libgpui_dotnet.so',
    'artifacts/native/osx-x64/libgpui_dotnet.dylib',
    'artifacts/native/osx-arm64/libgpui_dotnet.dylib'
)

foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing native package asset: $relative. Stage each CI build with eng/stage-native.ps1 first."
    }
}

$packOutput = Join-Path $root 'artifacts/packages'
$packProjects = @(
    'src/Gpui.Core/Gpui.Core.csproj',
    'src/Gpui.Native/Gpui.Native.win-x64.csproj',
    'src/Gpui.Native/Gpui.Native.linux-x64.csproj',
    'src/Gpui.Native/Gpui.Native.osx-x64.csproj',
    'src/Gpui.Native/Gpui.Native.osx-arm64.csproj',
    'src/Gpui.Native/Gpui.Native.csproj',
    'src/Gpui/Gpui.csproj'
)

foreach ($project in $packProjects) {
    & dotnet pack (Join-Path $root $project) `
        -c $Configuration `
        -p:BuildGpuiNative=false `
        -o $packOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
