param(
    [switch] $TrackAllocations,
    [string] $Filter = 'native_workload_measurements'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    $measurementArguments = @('test', '--manifest-path', 'crates/gpui-dotnet/Cargo.toml', '--release')
    if ($TrackAllocations) { $measurementArguments += @('--features', 'allocation-tracking') }
    $measurementArguments += @($Filter, '--', '--ignored', '--nocapture', '--test-threads=1')
    cargo @measurementArguments
    if ($LASTEXITCODE -ne 0) { throw "Native measurements failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
