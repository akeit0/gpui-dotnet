param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [ValidateSet('debug', 'release')]
    [string] $Configuration = 'debug'
)

$ErrorActionPreference = 'Stop'

$hasExplicitFxc = -not [string]::IsNullOrWhiteSpace($env:GPUI_FXC_PATH) -and
    (Test-Path -LiteralPath $env:GPUI_FXC_PATH)

if ($env:OS -eq 'Windows_NT' -and -not $hasExplicitFxc) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $fxc = Get-ChildItem -LiteralPath $kitsRoot -Filter fxc.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*\x64' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $fxc) {
        throw 'GPUI release builds require fxc.exe from the Windows SDK. Set GPUI_FXC_PATH explicitly.'
    }

    $env:GPUI_FXC_PATH = $fxc.FullName
}

$cargoArguments = @('build', '--manifest-path', $ManifestPath)
if ($Configuration -eq 'release') {
    $cargoArguments += '--release'
}

& cargo @cargoArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
