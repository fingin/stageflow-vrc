param([string]$NdiRuntimeDirectory, [string]$OmtDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $repo 'runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $runtime 'libomt.dll'))) {
    if (!$OmtDirectory) {
        $cache = Join-Path $repo 'artifacts/network-cache'
        New-Item -ItemType Directory -Force $cache | Out-Null
        $archive = Join-Path $cache 'omt-v1.0.0.19.zip'
        if (!(Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest 'https://github.com/openmediatransport/libomtnet/releases/download/v1.0.0.19/OpenMediaTransport.Binaries.Release.v1.0.0.19.zip' -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive).Hash -ne 'C62460174499DABE703E3E57372DAF5F1DD95F890355D11E30052B9ED35BDAC4') { throw 'OMT archive checksum mismatch' }
        $OmtDirectory = Join-Path $cache 'omt'
        Expand-Archive -LiteralPath $archive -DestinationPath $OmtDirectory -Force
    }
    foreach ($name in @('libomt.dll','libvmx.dll')) { Copy-Item -LiteralPath (Join-Path $OmtDirectory "Libraries/Winx64/$name") -Destination $runtime }
}
if (!(Test-Path -LiteralPath (Join-Path $runtime 'Processing.NDI.Lib.x64.dll'))) {
    if (!$NdiRuntimeDirectory) { $NdiRuntimeDirectory = $env:NDI_RUNTIME_DIR_V6 }
    if (!$NdiRuntimeDirectory) { throw 'Supply -NdiRuntimeDirectory with the official NDI 6.3.2 runtime DLL directory. Get the redistributable at https://ndi.link/NDIRedistV6. The existing portable build already contains this runtime.' }
    Copy-Item -LiteralPath (Join-Path $NdiRuntimeDirectory 'Processing.NDI.Lib.x64.dll') -Destination $runtime
}
$hashes = @{
    'libomt.dll' = '673B1D33222295F1C9190C54CEFC89D5E322301702532E2C8FE78A16E8AA28DA'
    'libvmx.dll' = 'EFFD2EB33A85689DA940AB6744CF8931448445144C5D7991AC50908800AF19BE'
    'Processing.NDI.Lib.x64.dll' = '2B6602075868BA4401F82F417D72424805D69B11CA86078023D0D489FF45DD84'
}
foreach ($name in $hashes.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $runtime $name)).Hash -ne $hashes[$name]) { throw "Pinned runtime checksum mismatch: $name" } }
Write-Output 'Verified NDI 6.3.2 and OMT 1.0.0.19 runtime libraries.'
