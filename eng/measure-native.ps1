param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    cargo test --manifest-path crates/gpui-dotnet/Cargo.toml --release native_workload_measurements -- --ignored --nocapture --test-threads=1
    if ($LASTEXITCODE -ne 0) { throw "Native measurements failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
