param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string] $Rid,

    [Parameter(Mandatory = $true)]
    [string] $Library
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root "artifacts/native/$Rid"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath $Library -Destination $destination -Force
Write-Host "Staged $Rid native asset in $destination"
