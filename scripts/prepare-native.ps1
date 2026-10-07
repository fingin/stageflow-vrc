param([string]$DependencyDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$release = '2026-08-26'
$expectedHash = 'EB5E67AD110543BCB65A4612BCD654B6427972EA70D26412B48D7E21532DFE67'
$url = "https://github.com/obsproject/obs-deps/releases/download/$release/windows-deps-$release-x64.zip"
if (!$DependencyDirectory) {
    $cache = Join-Path $repo 'artifacts/native-cache'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $archive = Join-Path $cache 'dependencies.zip'
    if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest $url -OutFile $archive -TimeoutSec 240 }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Native dependency archive checksum does not match the pinned release.' }
    $DependencyDirectory = Join-Path $cache 'extracted'
    if (!(Test-Path -LiteralPath (Join-Path $DependencyDirectory 'bin/avcodec-62.dll'))) { Expand-Archive -LiteralPath $archive -DestinationPath $DependencyDirectory -Force }
}
$runtime = Join-Path $repo 'runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
$libraries = @('avcodec-62.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll','libx264-164.dll','librist.dll','srt.dll','zlib.dll')
foreach ($name in $libraries) { Copy-Item -LiteralPath (Join-Path $DependencyDirectory "bin/$name") -Destination $runtime }
$licenseTarget = Join-Path $repo 'licenses/native'
New-Item -ItemType Directory -Force $licenseTarget | Out-Null
foreach ($name in @('FFmpeg','aom','svt-av1','libtheora','lame','opus','libvorbis','libogg','libvpx','librist','srt','mbedtls','x264','zlib','nv-codec-headers','amf')) {
    $source = Join-Path $DependencyDirectory "licenses/$name"
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $licenseTarget -Recurse -Force }
}
$manifest = [ordered]@{
    release = $release; archiveUrl = $url; archiveSha256 = $expectedHash
    recipeCommit = 'f80e1d30181b0ff2b048b6d8fe8b8c501f3d221c'
    ffmpegVersion = '8.1.2'; ffmpegCommit = '38b88335f99e76ed89ff3c93f877fdefce736c13'
    files = @($libraries | ForEach-Object { [ordered]@{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $runtime $_)).Hash } })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repo 'native-manifest.json') -Encoding utf8
Write-Output "Prepared $($libraries.Count) native DLLs. No OBS or FFmpeg executable is included."
