# StageFlow

Single-process Windows show playback, timecode, Art-Net gridnode rendering and HLS streaming.

**In development — not a finished release.** See [delivery contract](docs/DELIVERY.md).

The user workflow is MP4 + grandMA2 Art-Net → synchronized picture, gridnode data,
audio and timecode → an in-process HLS stream. MTC can be generated or received.
Spout is optional interoperability, not the primary Art-Net transport.

## Build

Windows x64 and .NET SDK 10.0.401. The workspace's local SDK is automatically
located by the build script; a system SDK also works.

```powershell
.\scripts\build.ps1 -Test -EngineTest
```

The GPU test requires Direct3D 11 hardware. The media integration test currently
loads FFmpeg 8 ABI DLLs from the installed OBS directory only as a development
fixture. The delivered application will package its own native libraries and
will not require OBS or run an OBS/FFmpeg/MediaMTX process.

## Source map

- `src/StageFlow.Core`: transport, MTC codec, Art-Net, Hnode import, grid layouts,
  projects and undo/redo. Independent of the GUI and GPU.
- `src/StageFlow.Engine`: native Direct3D/FFmpeg pipeline, Windows MIDI and audio,
  in-memory HLS segmentation and the in-process HTTP server.
- `tests/StageFlow.Tests`: deterministic protocol, transport and project tests.
- `tests/StageFlow.EngineTests`: hardware integration and encoded-grid checks.
- `web`: self-hosted HLS player, including pinned hls.js 1.6.13.

Actual work lives in this source tree. Generated binaries are ignored by Git.
Dependencies are pinned and restore lock files are checked in. Avoid coupling
the rendering loop to UI timers, native I/O callbacks, or HTTP clients.

## Validation to date

Core checks pass, including the user's actual Hnode configuration import.
On an RTX 4080: D3D11 hardware decode, GPU conversion/compositing, BinaryStageFlight
grid rendering, NVENC video and AAC audio into in-memory MPEG-TS/HLS segments.
Independent FFmpeg decoding confirms both streams and preservation of 13,312
binary/CRC cells in the current integration fixture. This is not yet a sustained
performance result or external grandMA2/VRChat compatibility certification.

## Upstream

See [upstream notes](docs/UPSTREAM.md) and `licenses/`. Media/graphics calls are
bound through [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) and
[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen), with native execution
in Direct3D and FFmpeg. Hnode-compatible grid layouts retain upstream attribution.
