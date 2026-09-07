param(
    [string] $Filter = 'Category=Performance'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousTiering = $env:DOTNET_TieredCompilation
Push-Location $repositoryRoot
try {
    # Measure one JIT tier, in a separate test process. All timing probes share one xUnit
    # collection and run sequentially; correctness tests keep their normal runtime settings.
    $env:DOTNET_TieredCompilation = '0'
    dotnet test tests/Gpui.Tests/Gpui.Tests.csproj -c Release -m:1 --filter $Filter --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw "Runtime measurements failed with exit code $LASTEXITCODE." }
}
finally {
    if ($null -eq $previousTiering) { Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue }
    else { $env:DOTNET_TieredCompilation = $previousTiering }
    Pop-Location
}
