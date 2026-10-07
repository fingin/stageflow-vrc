# Upstream references and compatibility

The consolidated [dependency inventory](DEPENDENCIES.md) includes all resolved
managed packages, native libraries, web bundle components and framework notices.
[Credits and inspiration](CREDITS.md) distinguishes adapted source from workflow
and interface influences.

Hnode: https://github.com/Happyrobot33/HNode
Inspected revision: d4c4f2d4a9204a4b5b193d426028725d4b0727f7
License: MIT; included in licenses/HNode-LICENSE.txt.

GridEncoder adapts the VRSL, MDMX/BinaryStageFlight, Binary and Ternary layouts
from Hnode's Assets/Plugin/Serializers. Grid cells use top-left image coordinates;
Hnode's PixelToIndex flips into Unity's bottom-left texture array. The resulting
visible pixel coordinates must match, not the intermediate array indexing.
BinaryStageFlight uses 4x4 binary cells, six bytes per column, four CRC bits and
208-pixel vertical bands. CRC polynomial is 0x03 with the resulting nibble stored
in the upper four bits. A partial six-byte group is zero padded to avoid the
upstream GetRange exception when the received channel count is not divisible by 6.


## Other components

- Spout2: https://github.com/leadedge/Spout2 — BSD-2-Clause. Shared registry,
  metadata and synchronization compatibility follows the SDK sources. The runtime
  implements Direct3D sharing directly and does not require SpoutLibrary.dll.
- hls.js 1.6.13: https://github.com/video-dev/hls.js — Apache-2.0, bundled locally.
- Vortice.Windows 3.8.3: https://github.com/amerkoleci/Vortice.Windows — MIT.
- FFmpeg.AutoGen 8.0.0.1: https://github.com/Ruslan-B/FFmpeg.AutoGen — MIT.
- NAudio 2.2.1: https://github.com/naudio/NAudio — MIT.
- YamlDotNet 16.3.0: https://github.com/aaubry/YamlDotNet — MIT.
- .NET/ASP.NET 10: https://github.com/dotnet — matching runtime licenses and
  third-party notices are preserved under licenses/dotnet. Managed package
  notices are under licenses/managed, and embedded browser-module notices under
  licenses/web.

## Native media dependencies

Unmodified DLLs come from the official OBS dependency release, not the OBS application:
https://github.com/obsproject/obs-deps/releases/tag/2026-08-26

`native-manifest.json` records the archive and DLL SHA-256 hashes. FFmpeg reports
version n8.1.2 and GPL version 3 or later. The exact upstream FFmpeg revision is
38b88335f99e76ed89ff3c93f877fdefce736c13. The OBS build recipes, dependency revisions,
checksums and patches at f80e1d30181b0ff2b048b6d8fe8b8c501f3d221c are preserved in
`licenses/native-build-recipes.zip`. `licenses/native-source-catalog.json` lists
the corresponding upstream source locations and recipe pins. Per-component
license texts are under `licenses/native/`.

To rebuild native libraries, extract the recipe archive and follow its Windows
build instructions with MSVC/MSYS2. Keep the pinned sources and supplied patches.
The StageFlow package script uses the verified prebuilt DLLs, not a local native
compiler. Public redistribution should accompany the full corresponding source
and notices for the chosen native dependency build, as well as StageFlow's source.

## V2 network runtimes

- Open Media Transport 1.0.0.19: https://github.com/openmediatransport/libomtnet
  and https://github.com/openmediatransport/libomt. MIT notices and the matching
  C ABI header are under licenses/omt. libvmx accompanies libomt.
- NDI Runtime 6.3.2: official redistributable https://ndi.link/NDIRedistV6.
  NDI is a registered trademark of Vizrt NDI AB. The unmodified runtime keeps its
  proprietary license; agreement and third-party notices are under licenses/ndi.
  NDI information is available from the application's Connections menu.
- SRT, RTMP and RTMPS use the existing pinned FFmpeg/native dependencies.

network-manifest.json records archive/DLL SHA-256 values. Source checkouts obtain
NDI from the official runtime directory; the delivered private portable includes
that runtime. Review the included agreements and corresponding-source obligations
before public redistribution; this package does not change upstream license terms.
