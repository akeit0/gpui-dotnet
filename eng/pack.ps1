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
$packages = @(
    @{ Project = 'src/Gpui.Core/Gpui.Core.csproj'; Id = 'GPUI.NET.Core' },
    @{ Project = 'src/Gpui.Native/Gpui.Native.win-x64.csproj'; Id = 'GPUI.NET.Native.win-x64' },
    @{ Project = 'src/Gpui.Native/Gpui.Native.linux-x64.csproj'; Id = 'GPUI.NET.Native.linux-x64' },
    @{ Project = 'src/Gpui.Native/Gpui.Native.osx-x64.csproj'; Id = 'GPUI.NET.Native.osx-x64' },
    @{ Project = 'src/Gpui.Native/Gpui.Native.osx-arm64.csproj'; Id = 'GPUI.NET.Native.osx-arm64' },
    @{ Project = 'src/Gpui.Native/Gpui.Native.csproj'; Id = 'GPUI.NET.Native' },
    @{ Project = 'src/Gpui/Gpui.csproj'; Id = 'GPUI.NET' }
)

if (Test-Path -LiteralPath $packOutput) {
    Remove-Item -LiteralPath $packOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $packOutput | Out-Null

foreach ($package in $packages) {
    & dotnet pack (Join-Path $root $package.Project) `
        -c $Configuration `
        -p:BuildGpuiNative=false `
        -o $packOutput
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

[xml] $buildProps = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string] $buildProps.Project.PropertyGroup.Version
$expectedFiles = @($packages | ForEach-Object { "$($_.Id).$version.nupkg" })
$actualFiles = @(Get-ChildItem -LiteralPath $packOutput -Filter '*.nupkg' -File | Select-Object -ExpandProperty Name)
$missingFiles = @($expectedFiles | Where-Object { $_ -notin $actualFiles })
$unexpectedFiles = @($actualFiles | Where-Object { $_ -notin $expectedFiles })

if ($missingFiles.Count -gt 0 -or $unexpectedFiles.Count -gt 0) {
    throw "Unexpected package set. Missing: $($missingFiles -join ', '). Unexpected: $($unexpectedFiles -join ', ')."
}

$nativeEntries = @{
    'GPUI.NET.Native.win-x64' = 'runtimes/win-x64/native/gpui_dotnet.dll'
    'GPUI.NET.Native.linux-x64' = 'runtimes/linux-x64/native/libgpui_dotnet.so'
    'GPUI.NET.Native.osx-x64' = 'runtimes/osx-x64/native/libgpui_dotnet.dylib'
    'GPUI.NET.Native.osx-arm64' = 'runtimes/osx-arm64/native/libgpui_dotnet.dylib'
}

foreach ($entry in $nativeEntries.GetEnumerator()) {
    $packagePath = Join-Path $packOutput "$($entry.Key).$version.nupkg"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        if ($null -eq $archive.GetEntry($entry.Value)) {
            throw "$($entry.Key) does not contain $($entry.Value)."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$gpuiPackagePath = Join-Path $packOutput "GPUI.NET.$version.nupkg"
$gpuiBuildEntries = @(
    'buildTransitive/GPUI.NET.targets',
    'buildTransitive/GPUI.NET.Windows.manifest'
)

$archive = [System.IO.Compression.ZipFile]::OpenRead($gpuiPackagePath)
try {
    foreach ($entry in $gpuiBuildEntries) {
        if ($null -eq $archive.GetEntry($entry)) {
            throw "GPUI.NET does not contain $entry."
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Packed GPUI.NET $version ($($actualFiles.Count) packages) into $packOutput"
