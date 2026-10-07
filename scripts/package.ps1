param([string]$Destination, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Destination) { $Destination = Join-Path (Split-Path $repo -Parent) 'StageFlow-Portable' }
if (!(Test-Path -LiteralPath (Join-Path $repo 'runtime/avcodec-62.dll'))) { & (Join-Path $PSScriptRoot 'prepare-native.ps1') }
& (Join-Path $PSScriptRoot 'build.ps1') -Test:(!$SkipTests)
$localDotnet = Join-Path $repo '../../work/toolchain/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { (Resolve-Path -LiteralPath $localDotnet).Path } else { (Get-Command dotnet).Source }
& $dotnet publish (Join-Path $repo 'src/StageFlow.App') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -o $Destination
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
foreach ($name in @('licenses','docs','samples')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $Destination -Recurse -Force }
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','native-manifest.json')) {
    $source = Join-Path $repo $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $Destination }
}
@'
@echo off
start "StageFlow stress test" "%~dp0StageFlow.App.exe" --record "%~dp0samples\Sample Show.stageflow"
'@ | Set-Content -LiteralPath (Join-Path $Destination 'Start Stress Test.cmd') -Encoding ascii
Write-Output "Portable app: $(Join-Path $Destination 'StageFlow.App.exe')"
