param([string]$Destination, [switch]$SkipTests, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Destination) { $Destination = Join-Path (Split-Path $repo -Parent) 'StageFlow-v2-Portable' }
$Destination = [IO.Path]::GetFullPath($Destination)
foreach ($frozenName in @('StageFlow', 'StageFlow-Portable')) {
    $frozenPath = [IO.Path]::GetFullPath((Join-Path (Split-Path $repo -Parent) $frozenName))
    if ($Destination -eq $frozenPath -or $Destination.StartsWith($frozenPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The frozen v1 directories cannot be overwritten.' }
}
if (!$ArtifactsPath) { $ArtifactsPath = Join-Path $repo 'artifacts' }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
if (!(Test-Path -LiteralPath (Join-Path $repo 'runtime/avcodec-62.dll'))) { & (Join-Path $PSScriptRoot 'prepare-native.ps1') }
& (Join-Path $PSScriptRoot 'prepare-network.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Test:(!$SkipTests) -ArtifactsPath $ArtifactsPath
$localDotnet = Join-Path $repo '../../work/toolchain/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { (Resolve-Path -LiteralPath $localDotnet).Path } else { (Get-Command dotnet).Source }
& $dotnet publish (Join-Path $repo 'src/StageFlow.App') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -p:UseArtifactsOutput=true "-p:ArtifactsPath=$ArtifactsPath" -o $Destination
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
foreach ($name in @('licenses','docs','samples')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $Destination -Recurse -Force }
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','native-manifest.json','network-manifest.json')) {
    $source = Join-Path $repo $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $Destination }
}
@'
@echo off
start "StageFlow v2 stress test" "%~dp0StageFlow.App.exe" --record "%~dp0samples\Example.stageflow"
'@ | Set-Content -LiteralPath (Join-Path $Destination 'Start Stress Test.cmd') -Encoding ascii
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version
$commit = & git -C $repo rev-parse HEAD
@{ version = $version; sourceCommit = $commit; builtUtc = [DateTime]::UtcNow.ToString('o'); target = 'win-x64'; selfContained = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'build-info.json') -Encoding UTF8
Write-Output "Portable app: $(Join-Path $Destination 'StageFlow.App.exe')"
