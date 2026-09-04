$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failed = $false

function Fail([string] $message) {
    Write-Host "check-version: $message" -ForegroundColor Red
    $script:failed = $true
}

[xml] $props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string] $props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    Fail 'Directory.Build.props does not define /Project/PropertyGroup/Version.'
    $version = ''
}

$cargoRoot = Get-Content -LiteralPath (Join-Path $root 'crates/gpui-dotnet/Cargo.toml') -Raw
$workspaceVersion = ([regex]::Match(
    $cargoRoot,
    '(?ms)^\[workspace\.package\].*?^version\s*=\s*"([^"]+)"')).Groups[1].Value
if ([string]::IsNullOrWhiteSpace($workspaceVersion)) {
    Fail 'crates/gpui-dotnet/Cargo.toml does not define [workspace.package] version.'
} elseif ($workspaceVersion -ne $version) {
    Fail "Cargo workspace version '$workspaceVersion' does not match Directory.Build.props '$version'."
}

foreach ($member in @('hosts/default', 'extensions/editor-host')) {
    $memberToml = Get-Content -LiteralPath (Join-Path $root "crates/gpui-dotnet/$member/Cargo.toml") -Raw
    if ($memberToml -match '(?m)^version\s*=\s*"') {
        Fail "crates/gpui-dotnet/$member/Cargo.toml sets an explicit version; use 'version.workspace = true'."
    }
    if ($memberToml -notmatch '(?m)^version\.workspace\s*=\s*true') {
        Fail "crates/gpui-dotnet/$member/Cargo.toml does not inherit 'version.workspace = true'."
    }
}

if ($cargoRoot -notmatch '(?m)^version\.workspace\s*=\s*true') {
    Fail "crates/gpui-dotnet/Cargo.toml root package does not inherit 'version.workspace = true'."
}

$lock = Get-Content -LiteralPath (Join-Path $root 'crates/gpui-dotnet/Cargo.lock') -Raw
foreach ($name in @('gpui-dotnet', 'gpui-dotnet-default-host', 'gpui-dotnet-editor-host')) {
    $pattern = "(?ms)\[\[package\]\]\s*name\s*=\s*`"$name`"\s*version\s*=\s*`"([^`"]+)`""
    $match = [regex]::Match($lock, $pattern)
    if (-not $match.Success) {
        Fail "Cargo.lock has no entry for '$name'."
    } elseif ($match.Groups[1].Value -ne $version) {
        Fail "Cargo.lock '$name' is '$($match.Groups[1].Value)', expected '$version'. Run eng/bump-version.ps1 or 'cargo update -p $name'."
    }
}

$readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
if ($readme -match 'dotnet add package GPUI\.NET --version \d' -or
    $readme -match 'current package line is `[^`]+`') {
    Fail 'README.md must not pin a version literal; use version-agnostic install text.'
}

if ($script:failed) {
    throw 'Version check failed. Directory.Build.props is authoritative; sync with eng/bump-version.ps1 -Version <version>.'
}

Write-Host "Versions consistent at $version."
