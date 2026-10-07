# StageFlow credits and inspiration

StageFlow brings video, lighting data, timecode, compositing and streaming into one show workspace. Thank you to the developers, maintainers and communities whose software and ideas make that work possible.

These credits cover StageFlow **2.0.0-alpha.5**. Exact versions, package names, native components and notice locations are in the [dependency inventory](DEPENDENCIES.md).

## Project development

- **Project owner and operator** — product concept, the original show workflow, visual direction, stress testing and feedback.
- **StageFlow development** — application-specific engine integration, transport, compositing, project/settings infrastructure and controls, developed with OpenAI Codex assistance.
- **Earlier MTC Display prototype** — the starting workflow for repeatable, synchronized timecode overlays.

## Software and source foundations

| Project and contributors | Contribution to StageFlow |
|---|---|
| [HNode — Matthew Herber / Happyrobot33 and contributors](https://github.com/Happyrobot33/HNode) | Adapted grid serialization layouts and Hnode show configuration compatibility. This is a direct source-adaptation credit. |
| [VRC-MIDIDMX — Micca / micksam7](https://github.com/micksam7/VRC-MIDIDMX) | MIDI-DMX protocol reference, including the implementation referenced by HNode. |
| [Spout2 — Lynn Jarvis / leadedge and contributors](https://github.com/leadedge/Spout2) | Shared GPU texture registry and synchronization compatibility reference. |
| [FFmpeg developers](https://ffmpeg.org/) | Media decoding, encoding, audio conversion and stream muxing. |
| [FFmpeg.AutoGen — Ruslan Balanukhin and contributors](https://github.com/Ruslan-B/FFmpeg.AutoGen) | .NET bindings to FFmpeg's native libraries. |
| [OBS Project / obs-deps maintainers](https://github.com/obsproject/obs-deps) | Native dependency builds, pinned recipes and patches used by the packaged media stack. |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) and [Vortice.Mathematics](https://github.com/amerkoleci/Vortice.Mathematics) — Amer Koleci and contributors | Direct3D, DXGI, Direct2D/DirectWrite and supporting .NET types. |
| [SharpGenTools — Alexandre Mutel, Jeremy Koritzinsky and contributors](https://github.com/SharpGenTools/SharpGenTools) | Native/COM interop support used by Vortice. |
| [NAudio — Mark Heath and contributors](https://github.com/naudio/NAudio) | Windows audio device access and playback support. |
| [YamlDotNet — Antoine Aubry and contributors](https://github.com/aaubry/YamlDotNet) | Hnode YAML configuration parsing. |
| [Microsoft and .NET contributors](https://github.com/dotnet) | .NET, Windows Forms, ASP.NET Core/Kestrel, build tools and Windows graphics/audio APIs. |
| [hls.js — video-dev contributors](https://github.com/video-dev/hls.js) | Browser playback of the built-in HLS stream. |
| [NDI / Vizrt NDI AB](https://ndi.video/) | NDI video/audio runtime and interoperability. |
| [Open Media Transport contributors](https://github.com/openmediatransport) | OMT transport and the VMX video codec. |
| [Haivision and SRT contributors](https://github.com/Haivision/srt) | SRT transport in the native media stack. |
| [VideoLAN / x264 contributors](https://www.videolan.org/developers/x264.html) | Software H.264 encoding. |
| [NVIDIA](https://developer.nvidia.com/video-codec-sdk) | NVENC/NVDEC driver facilities and codec interface headers. |
| Alliance for Open Media, Xiph.Org, WebM, LAME, Mbed TLS, librist, zlib and their contributors | Additional codec, compression and networking components inherited through the native media libraries; listed individually in [DEPENDENCIES.md](DEPENDENCIES.md). |

The smaller projects embedded in hls.js, NDI and the native/.NET distributions also deserve credit. Their names, source references and complete upstream notices are indexed in the dependency documents and `licenses/` folder.

## Workflow and interface inspiration

These acknowledgments describe the workflows and design references that shaped StageFlow. They are separate from the dependency inventory.

| Project or reference | Inspiration |
|---|---|
| [OBS Studio](https://obsproject.com/) | Scene/source composition, layer transforms, streaming controls and output diagnostics; also a media/GPU implementation reference during development. |
| [Spout2 for OBS — Off World Live and contributors](https://github.com/Off-World-Live/obs-spout2-plugin) | The original workflow of bringing shared GPU textures into a compositor. |
| [HNode](https://github.com/Happyrobot33/HNode) | Flexible Art-Net-to-grid-video routing and the affordance of turning lighting data into a video overlay. |
| [VidPlayMTC](https://vidplaymtc.com/index.htm) | Efficient video playback synchronized to MIDI timecode, cue navigation and visible timecode. |
| [loopMIDI — Tobias Erichsen](https://www.tobias-erichsen.de/software/loopmidi.html) | Straightforward virtual MIDI routing between applications; remains an optional external port provider. |
| [grandMA2 — MA Lighting](https://www.malighting.com/downloads/products/grandma2/) | The user's timecoded lighting-programming workflow and Art-Net integration target. |
| [OSC/PILOT](https://oscpilot.com/) | User-mapped remote controls with feedback that follows the application's actual state. |
| [Unity Inspector](https://docs.unity3d.com/Manual/UsingTheInspector.html) | Selection-driven property editing and settings that update without an Apply button. Unity is not the StageFlow runtime. |
| [MediaMTX — bluenviron contributors](https://github.com/bluenviron/mediamtx) | The original local streaming workflow and convenient playback endpoints. StageFlow's HLS server runs inside StageFlow. |
| Original **MTC Sender** utility | The standalone master-timecode transport in the original setup. The exact product/author was not identified in the supplied references. |
| User-supplied dashboard reference, **images.jpg** | Visual direction for the v2 workspace. The original designer is unidentified; no third-party ownership is attributed to StageFlow. |

## Protocol acknowledgments

- **Art-Net™ Designed by and Copyright Artistic Licence Engineering Ltd.** [Art-Net project](https://art-net.org.uk/)
- **MIDI and MIDI Time Code** — the MIDI standards community and [MIDI Association specification](https://midi.org/midi-time-code).
- **Open Sound Control** — Matt Wright, Adrian Freed, CNMAT and the [OSC community](https://opensoundcontrol.stanford.edu/).
- **HTTP Live Streaming** — [Apple's HLS specification and ecosystem](https://developer.apple.com/streaming/).
- **VRSL / VR Stage Lighting** — [AcChosen and contributors](https://github.com/AcChosen/VR-Stage-Lighting), acknowledged for the grid-format ecosystem supported through the HNode-derived layouts.
- **BinaryStageFlight / MDMX, Binary and Ternary grids** — compatibility follows HNode's referenced serializers; those names identify formats, not additional bundled applications.

NDI is a registered trademark of Vizrt NDI AB. Project names and trademarks belong to their respective owners. These acknowledgments do not imply endorsement or affiliation.

## Short credits text

> Built with FFmpeg, .NET, Vortice, SharpGenTools, NAudio, YamlDotNet, hls.js, NDI and Open Media Transport, with native dependencies supplied by the OBS Project. Grid layouts adapted from HNode; Spout2 and VRC-MIDIDMX informed protocol compatibility. Inspired by the production workflows of OBS Studio, VidPlayMTC, loopMIDI, grandMA2, OSC/PILOT and MediaMTX, and by Unity's Inspector. Thank you to the maintainers and contributors behind these projects.

Use the short text alongside the [full dependency inventory](DEPENDENCIES.md) and included license notices.
