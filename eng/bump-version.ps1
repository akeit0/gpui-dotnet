param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z\.\-]+)?(\+[0-9A-Za-z\.\-]+)?$') {
    throw "Invalid version '$Version'. Expected SemVer (major.minor.patch with optional -prerelease/+build)."
}

function Read-Text([string] $path) {
    return [System.IO.File]::ReadAllText($path)
}

function Write-Text([string] $path, [string] $text) {
    # Normalize to LF and keep exactly one trailing newline, matching repo convention.
    $text = $text -replace "`r`n", "`n"
    $text = $text.TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}

$propsPath = Join-Path $root 'Directory.Build.props'
$props = Read-Text $propsPath
if ($props -notmatch '<Version>[^<]+</Version>') {
    throw 'Directory.Build.props does not contain <Version>...</Version>.'
}
$props = [regex]::Replace($props, '<Version>[^<]+</Version>', "<Version>$Version</Version>", 1)
Write-Text $propsPath $props

$cargoPath = Join-Path $root 'crates/gpui-dotnet/Cargo.toml'
$cargo = Read-Text $cargoPath
if ($cargo -notmatch '\[workspace\.package\]') {
    throw 'crates/gpui-dotnet/Cargo.toml does not contain [workspace.package]. Run check-version for details.'
}
$cargo = [regex]::Replace(
    $cargo,
    '(?m)(^\[workspace\.package\][^\[]*?^version\s*=\s*")[^"]+(")',
    "`${1}$Version`${2}",
    1)
Write-Text $cargoPath $cargo

Write-Host "Updated Directory.Build.props and Cargo workspace version to $Version."

$manifest = Join-Path $root 'crates/gpui-dotnet/Cargo.toml'
& cargo update --manifest-path $manifest -p gpui-dotnet -p gpui-dotnet-default-host -p gpui-dotnet-editor-host 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) {
    throw "cargo update failed. Refresh Cargo.lock with 'cargo metadata --manifest-path crates/gpui-dotnet/Cargo.toml' and retry."
}

& (Join-Path $root 'eng/check-version.ps1')
Write-Host "Bumped to $Version. Verify with 'dotnet run --project tools/Gpui.Bindings.Generator -- verify' and commit the change before tagging."
