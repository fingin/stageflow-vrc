param([switch]$Test, [switch]$EngineTest, [switch]$UiTest, [switch]$ProtocolTest, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $repo '..\..\work\toolchain\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { (Resolve-Path -LiteralPath $localDotnet).Path } else { (Get-Command dotnet).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$buildArgs = @('build', (Join-Path $repo 'StageFlow.slnx'), '-c', 'Release')
if ($ArtifactsPath) {
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    $buildArgs += @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$ArtifactsPath")
}
& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
function Invoke-Checks([string]$Project, [string[]]$Arguments = @()) {
    $dll = if ($ArtifactsPath) { Join-Path $ArtifactsPath "bin/$Project/release/$Project.dll" } else { Join-Path $repo "tests/$Project/bin/Release/net10.0-windows/$Project.dll" }
    if (!(Test-Path -LiteralPath $dll)) { $dll = Join-Path $repo "tests/$Project/bin/Release/net10.0/$Project.dll" }
    & $dotnet $dll @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Project checks failed" }
}
if ($Test) {
    Invoke-Checks 'StageFlow.Tests'
}
if ($EngineTest -or $ProtocolTest) {
    $previousNative = $env:STAGEFLOW_NATIVE
    try {
        $env:STAGEFLOW_NATIVE = Join-Path $repo 'runtime'
        if ($EngineTest) { Invoke-Checks 'StageFlow.EngineTests' }
        if ($ProtocolTest) { Invoke-Checks 'StageFlow.EngineTests' @('--protocols'); Invoke-Checks 'StageFlow.EngineTests' @('--srt-loopback') }
    }
    finally { $env:STAGEFLOW_NATIVE = $previousNative }
}
if ($UiTest) {
    Invoke-Checks 'StageFlow.UiTests'
}
