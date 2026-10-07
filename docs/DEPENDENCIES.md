# StageFlow projects and dependencies

Inventory for the Windows x64 portable release **2.0.0-alpha.5**, application revision `b8cf05553b83cb3056c3394f5877c9374613a315`. Compiled October 6, 2026, Pacific time. This records the delivered build, rather than the latest upstream versions.

The application resolves **17 managed NuGet packages**, includes **3 .NET framework packs**, and ships **12 native media/network DLLs**. Its hls.js bundle includes three additional npm modules. Source adaptations, protocol references, inherited components and development tools are listed separately below. General acknowledgments and inspiration are in [CREDITS.md](CREDITS.md).

## StageFlow projects

| Project | Purpose | Delivered |
|---|---|---|
| StageFlow.App | Windows Forms workspace, live settings, inspector, transport and monitoring | Yes |
| StageFlow.Engine | GPU composition, playback, audio/MIDI, native network video and streaming | Yes |
| StageFlow.Core | Project model, timing/protocol logic, grid layout, import and validation | Yes |
| StageFlow.Tests | Core/protocol/project regression checks | Source only |
| StageFlow.EngineTests | Native graphics, media and transport integration checks | Source only |
| StageFlow.UiTests | UI/settings/layout integration checks | Source only |

## Managed packages

All 17 resolved packages below declare MIT licensing. Direct means an explicit reference in a StageFlow project; transitive packages are resolved through those references. A packaged API is not necessarily an exposed StageFlow feature: for example, the NAudio package family includes ASIO support, while StageFlow's current local output uses WASAPI.

| Package | Version | Relationship and use | Project / notice |
|---|---|---|---|
| FFmpeg.AutoGen | 8.0.0.1 | Direct; native FFmpeg API bindings | [Project](https://github.com/Ruslan-B/FFmpeg.AutoGen), [license](../licenses/FFmpeg.AutoGen-LICENSE.txt) |
| NAudio | 2.2.1 | Direct; Windows audio API package family | [Project](https://github.com/naudio/NAudio), [license](../licenses/NAudio-LICENSE.txt) |
| NAudio.Asio | 2.2.1 | Transitive; NAudio ASIO module | Same NAudio project and license |
| NAudio.Core | 2.2.1 | Transitive; shared audio types | Same NAudio project and license |
| NAudio.Midi | 2.2.1 | Transitive; MIDI module; StageFlow's MTC path uses its own WinMM calls | Same NAudio project and license |
| NAudio.Wasapi | 2.2.1 | Transitive; WASAPI device/output support | Same NAudio project and license |
| NAudio.WinForms | 2.2.1 | Transitive; NAudio Windows Forms module | Same NAudio project and license |
| NAudio.WinMM | 2.2.1 | Transitive; Windows multimedia support | Same NAudio project and license |
| Vortice.Direct3D11 | 3.8.3 | Direct; Direct3D 11 GPU access | [Project](https://github.com/amerkoleci/Vortice.Windows), [license](../licenses/managed/Vortice.Windows/LICENSE.txt) |
| Vortice.Direct2D1 | 3.8.3 | Direct; Direct2D and text/overlay APIs | Same Vortice.Windows project and license |
| Vortice.D3DCompiler | 3.8.3 | Direct; shader compilation bindings | Same Vortice.Windows project and license |
| Vortice.DXGI | 3.8.3 | Transitive; adapters, surfaces and sharing | Same Vortice.Windows project and license |
| Vortice.DirectX | 3.8.3 | Transitive; common DirectX types | Same Vortice.Windows project and license |
| Vortice.Mathematics | 2.1.0 | Transitive; graphics math types | [Project](https://github.com/amerkoleci/Vortice.Mathematics), [license](../licenses/managed/Vortice.Mathematics/LICENSE.txt) |
| SharpGen.Runtime | 2.4.2-beta | Transitive; native interop runtime | [Project](https://github.com/SharpGenTools/SharpGenTools), [license](../licenses/managed/SharpGenTools/LICENSE.txt) |
| SharpGen.Runtime.COM | 2.4.2-beta | Transitive; COM interop support | Same SharpGenTools project and license |
| YamlDotNet | 16.3.0 | Direct; `.shwcfg` YAML parsing | [Project](https://github.com/aaubry/YamlDotNet), [license](../licenses/managed/YamlDotNet/LICENSE.txt) |

## Bundled Microsoft frameworks

| Framework pack | Version | Use | Notices |
|---|---|---|---|
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.12 | CoreCLR and .NET base libraries | [MIT license](../licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/LICENSE.TXT), [third-party notices](../licenses/dotnet/Microsoft.NETCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.TXT) |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 10.0.12 | Windows Forms and the published Windows desktop framework | [MIT license](../licenses/dotnet/Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE) |
| Microsoft.AspNetCore.App.Runtime.win-x64 | 10.0.12 | In-process HTTP/HLS server through Kestrel | [MIT license](../licenses/dotnet/Microsoft.AspNetCore.App.Runtime.win-x64/LICENSE.txt), [third-party notices](../licenses/dotnet/Microsoft.AspNetCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.TXT) |

Microsoft/.NET framework notices credit many embedded algorithms and projects. The [inherited notice index](INHERITED-NOTICES.md) lists their upstream headings. Those notices cover the upstream distributions, including other platforms and build tooling; a name there does not establish a separate StageFlow DLL or feature.

## Native media and network libraries

The FFmpeg set comes from [OBS dependencies 2026-08-26](https://github.com/obsproject/obs-deps/releases/tag/2026-08-26), using recipe revision `f80e1d30181b0ff2b048b6d8fe8b8c501f3d221c`. Five FFmpeg library DLLs are shipped; the OBS application, `libobs`, `avdevice`, `avfilter`, `ffmpeg.exe` and `ffprobe.exe` are not part of the portable runtime.

| Project | Pinned version | Use and shipped files | License / notices |
|---|---|---|---|
| [FFmpeg](https://ffmpeg.org/) | 8.1.2; `38b88335f99e76ed89ff3c93f877fdefce736c13` | `avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`, `swscale-9.dll`; decode/encode, muxing, conversion | This build reports GPL v3 or later; [notices](../licenses/native/FFmpeg) |
| [x264](https://www.videolan.org/developers/x264.html) | r3106; `eaa68fad9e5d201d42fde51665f2d137ae96baf0` | `libx264-164.dll`; CPU H.264 encoding | [GPL notice](../licenses/native/x264/COPYING), [authors](../licenses/native/x264/AUTHORS) |
| [librist](https://code.videolan.org/rist/librist) | 0.2.7 | `librist.dll`; dependency of the FFmpeg distribution; no RIST route exposed by StageFlow | [BSD-2-Clause notice](../licenses/native/librist/COPYING) |
| [SRT](https://github.com/Haivision/srt) | 1.5.2 | `srt.dll`; SRT transport through FFmpeg | [MPL-2.0 notice](../licenses/native/srt/LICENSE) |
| [zlib](https://github.com/madler/zlib) | 1.3.1 | `zlib.dll`; native compression support | [zlib notice](../licenses/native/zlib/README) |
| [NDI Runtime](https://ndi.video/) | 6.3.2.0 | `Processing.NDI.Lib.x64.dll`; NDI input/output and discovery | Proprietary; [agreement](../licenses/ndi/NDI%20License%20Agreement.pdf), [third-party notices](../licenses/ndi/Processing.NDI.Lib.Licenses.txt) |
| [libomt](https://github.com/openmediatransport/libomt) | Binary bundle 1.0.0.19 | `libomt.dll`; native OMT input/output and discovery | [MIT notice](../licenses/omt/LICENSE.txt) |
| [libvmx](https://github.com/openmediatransport/libvmx) | From binary bundle 1.0.0.19 | `libvmx.dll`; VMX video codec for OMT | [MIT bundle notice](../licenses/omt/LICENSE.txt) |

The OMT binary bundle is published through [libomtnet releases](https://github.com/openmediatransport/libomtnet/releases/tag/v1.0.0.19); StageFlow uses its native `libomt`/`libvmx` libraries, not the managed `libomtnet` assembly.

### Components compiled into the native distribution

The FFmpeg build configuration enables the codec libraries below. They are credited even where StageFlow does not expose their encoders in its UI. Version pins come from the preserved OBS recipes.

| Component and project | Version | Role | Notice |
|---|---|---|---|
| [AOM / libaom](https://aomedia.googlesource.com/aom/) | 3.13.1 | AV1 codec support | [BSD-style license and patent notices](../licenses/native/aom) |
| [SVT-AV1](https://gitlab.com/AOMediaCodec/SVT-AV1) | 2.3.0 | AV1 codec support | [BSD-3-Clause-Clear and patent notices](../licenses/native/svt-av1) |
| [libtheora](https://www.theora.org/) | 1.1.1 | Theora codec support | [BSD-style notices](../licenses/native/libtheora) |
| [LAME](https://lame.sourceforge.io/) | 3.100 | MP3 encoding support | [LGPL notice and mpglib credits](../licenses/native/lame) |
| [Opus](https://opus-codec.org/) | 1.5.2 | Opus codec support | [BSD-style notice, authors and patent information](../licenses/native/opus) |
| [libvorbis](https://xiph.org/vorbis/) | 1.3.7 | Vorbis audio codec support | [BSD-style notices](../licenses/native/libvorbis) |
| [libogg](https://xiph.org/ogg/) | 1.3.5 | Ogg framing dependency | [BSD-style notices](../licenses/native/libogg) |
| [libvpx](https://chromium.googlesource.com/webm/libvpx/) | 1.14.1 | VP8/VP9 codec support | [BSD-style and patent notices](../licenses/native/libvpx) |
| [Mbed TLS](https://github.com/Mbed-TLS/mbedtls) | 3.6.4 | Crypto dependency selected by the SRT/librist build recipes | [Apache-2.0 notice](../licenses/native/mbedtls/LICENSE) |
| [nv-codec-headers](https://github.com/FFmpeg/nv-codec-headers) | 13.0.19.0 | NVIDIA codec interfaces used when building FFmpeg | [MIT notice](../licenses/native/nv-codec-headers/LICENSE) |
| [AMD AMF headers](https://github.com/GPUOpen-LibrariesAndSDKs/AMF) | 1.5.0 | Interfaces included in the native build source catalog; no AMF encoder exposed by StageFlow | [MIT notice and additional text](../licenses/native/amf/LICENSE.txt) |

The native notice trees additionally preserve credits for **libwebm, libyuv, x86inc, fastfeat, vector, cpuinfo, safestringlib, GoogleTest and LAME's mpglib**. Some are upstream test/build or architecture-specific material, rather than separately linked Windows components. Their original notices remain with their parent library.

The broader OBS source catalog also lists **libpng 1.6.47**, **libdatachannel v0.24.2** and **gas-preprocessor** at `9309c67acb535ca6248f092e96131d8eb07eefc1`. They belong to the preserved upstream build catalog; this does not make libpng or libdatachannel a StageFlow runtime dependency. Gas-preprocessor is a build tool, with `0.0.0` used only as a recipe placeholder.

### Third-party components acknowledged by NDI

The shipped NDI runtime's own [notice file](../licenses/ndi/Processing.NDI.Lib.Licenses.txt) credits the following. It does not specify separate versions for these internal components.

| Component | Credited author or project | License identified in the notice |
|---|---|---|
| RapidJSON | Tencent / THL A29 Limited and Milo Yip | MIT |
| SpeexDSP resampler | Jean-Marc Valin / Xiph.Org Foundation | BSD-style |
| RapidXML | Marcin Kalicinski | Boost-style text |
| CxxUrl | Christophe Meessen | MIT |
| Asio networking library | think-async.com; distinct from the ASIO audio driver API | Boost Software License 1.0 |
| MsQuic | Microsoft | MIT |
| Opus | Xiph.Org and the contributors named in the notice | BSD-style and patent information |
| SHA-2 implementation | Olivier Gay; C++ update credited to zedwood.com | Modified BSD |

## Web player

`web/hls.min.js` matches the **hls.js 1.6.13** release after CRLF/LF normalization. Its release source map identifies these embedded npm modules; versions follow the pinned release's package metadata.

| Project | Version | Role | Notice |
|---|---|---|---|
| [hls.js](https://github.com/video-dev/hls.js) | 1.6.13 | Browser HLS playback | [Apache-2.0](../licenses/hls.js-LICENSE.txt) |
| [EventEmitter3](https://github.com/primus/eventemitter3) | 5.0.1 | Event dispatch inside hls.js | [MIT](../licenses/web/eventemitter3/LICENSE.txt) |
| [url-toolkit](https://github.com/tjenkinson/url-toolkit) | 2.2.5 | URL resolution inside hls.js | [Apache-2.0](../licenses/web/url-toolkit/LICENSE.txt) |
| [SVTA Common Media Library](https://github.com/streaming-video-technology-alliance/common-media-library) | 0.17.1 | Media metadata and related utilities inside hls.js | [Apache-2.0](../licenses/web/@svta-common-media-library/LICENSE.txt) |

Additional hls.js source acknowledgments credit **DASH Industry Forum / dash.js** for CEA-608 parsing, **vtt.js contributors** for cues, **Masanao Izumo** for UTF-8 conversion, and **kanongil/node-m3u8parse** for attribute parsing. Their attribution comments are preserved in [hls.js inherited notices](../licenses/web/hls.js-INHERITED-NOTICES.txt). The full browser bundle is shipped even though StageFlow's player uses only a subset of its capabilities.

## Adapted source and compatibility references

| Project | Revision or source | Use | Notice |
|---|---|---|---|
| [HNode](https://github.com/Happyrobot33/HNode) | `d4c4f2d4a9204a4b5b193d426028725d4b0727f7` | Grid layout adaptation and `.shwcfg` interoperability | [MIT; Matthew Herber](../licenses/HNode-LICENSE.txt) |
| [Spout2](https://github.com/leadedge/Spout2) | Inspected SDK source; see [upstream notes](UPSTREAM.md) | Direct implementation of shared-texture registry/metadata/synchronization; no SpoutLibrary DLL | [BSD-2-Clause; Lynn Jarvis](../licenses/Spout-LICENSE.txt) |
| [VRC-MIDIDMX](https://github.com/micksam7/VRC-MIDIDMX) | HNode's exporter reference and protocol definitions | Banked MIDI-DMX messaging/handshake compatibility | [MIT; Micca](../licenses/VRC-MIDIDMX-LICENSE.txt) |
| [OBS Studio](https://github.com/obsproject/obs-studio) | Inspected media/graphics source and headers | Engineering and workflow reference; no OBS application runtime | [Upstream project license](https://github.com/obsproject/obs-studio/blob/master/COPYING) |

HNode's complete Unity application and its unrelated packages are not imported wholesale. Unity, KlakSpout, DryWetMIDI and HNode's generator plugins are not StageFlow package dependencies. MTC, OSC and Art-Net handling are StageFlow implementations; protocol acknowledgments are in [CREDITS.md](CREDITS.md).

## Platform and optional external components

- **Windows 10/11 x64** supplies Direct3D 11, DXGI, Direct2D, DirectWrite, Win32/COM, WASAPI and WinMM APIs used by StageFlow. These are operating-system components.
- **GPU drivers** supply hardware decode and NVIDIA NVENC where selected. Drivers are installed separately; `nv-codec-headers` is not a GPU driver.
- **A compatible web browser** runs the supplied HLS player. No Chromium/Electron/WebView runtime is bundled as the desktop UI.
- **loopMIDI / virtualMIDI** may supply local MIDI ports; **grandMA2** is the intended external lighting controller, and **OSC/PILOT** an optional control surface. They are separately installed applications.
- External Spout, NDI, OMT, SRT and RTMP peers are integrations selected by the user. StageFlow includes its own HLS server and does not require MediaMTX, OBS, HNode or VidPlayMTC to run.

## Build and validation tools

| Tool or project | Role |
|---|---|
| .NET SDK **10.0.401**, MSBuild, Roslyn and NuGet | C# build, restore, self-contained publishing and ReadyToRun compilation; SDK pin is in `global.json` |
| PowerShell | Build, package, dependency preparation and diagnostic scripts |
| Git | Source history, release tags and source archives |
| Node.js | Independent RTMP/AMF authentication fixture and development utilities; not a desktop runtime dependency |
| FFmpeg / ffprobe command-line tools | Independent media generation, receive/probe and validation fixtures; not shipped as application processes |
| innounp | Development-only extraction of the official NDI redistributable |
| OBS dependency build infrastructure | Upstream native build recipes, patches, MSVC/MSYS2 and associated build tools; the inspected FFmpeg binary reports MSVC 19.44.35228 |
| OpenAI Codex | Development assistance and validation orchestration |

The upstream build recipes and source catalog retain the individual native build pins. StageFlow's test projects use their own checks; no separate xUnit/NUnit/MSTest package is resolved.

## Inventory evidence and updates

- [Machine-readable dependency inventory](DEPENDENCY-INVENTORY.json): packages, framework versions, repository metadata, notice provenance, native DLL hashes and web bundle constituents.
- `src/**/packages.lock.json` and the portable `StageFlow.App.deps.json`: resolved managed dependency graph.
- Portable `StageFlow.App.runtimeconfig.json` and `build-info.json`: framework and application versions.
- [Native manifest](../native-manifest.json), [network manifest](../network-manifest.json) and [native source catalog](../licenses/native-source-catalog.json): native pins and SHA-256 values.
- [Native build recipes](../licenses/native-build-recipes.zip): the preserved upstream recipe revision and patches.
- [Upstream compatibility notes](UPSTREAM.md) and original notice texts under `licenses/`.

This inventory is a credits and provenance record. The original licenses and notices retain their full terms; listing a component does not change those terms or imply its optional features are exposed by StageFlow. Public source-distribution details for the chosen native build remain documented in [UPSTREAM.md](UPSTREAM.md).
