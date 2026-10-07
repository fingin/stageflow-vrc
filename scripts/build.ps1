param([switch]$Test, [switch]$EngineTest)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $repo '..\..\work\toolchain\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { (Resolve-Path -LiteralPath $localDotnet).Path } else { (Get-Command dotnet).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet build (Join-Path $repo 'StageFlow.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Test) {
    & $dotnet run --no-build --project (Join-Path $repo 'tests\StageFlow.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
}
if ($EngineTest) {
    & $dotnet run --no-build --project (Join-Path $repo 'tests\StageFlow.EngineTests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Engine checks failed' }
}
