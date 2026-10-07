# Architecture and modification guide

## Runtime path

```
master/follower clock ── MTC output ── grandMA2
           │                            │ Art-Net
           │                 bounded DMX universe store
           │                            │ GPU grid serializer
MP4 ─ hardware video decode ─ GPU compositor ─ optional Spout sender
 │                           ↑ clocks / Spout inputs
 └ audio decode/resample     │
       ├ selected WASAPI     └ D3D11 NV12 conversion → NVENC H.264
       └ independent gain → AAC ────────┬─────────────────────┘
                                memory MPEG-TS segments
                                in-process HTTP HLS server
```

MTC never feeds Hnode or Art-Net input. MIDI-DMX is a separate optional output
with the Hnode/VRChat readiness handshake. Incoming MTC selects follower mode;
local transport commands and MTC echo are suppressed in that mode.

## Module boundaries

| Directory | Responsibility |
|---|---|
| `src/StageFlow.Core/Timing` | Rational frame rates, drop-frame arithmetic, MTC wire codec and monotonic transport |
| `src/StageFlow.Core/ArtNet` | ArtDmx/ArtSync, bounded universe state and MIDI-DMX scheduler |
| `src/StageFlow.Core/Grid` | Hnode-compatible reference layouts and masks |
| `src/StageFlow.Core/Projects` | Schema, validation, atomic saves, Hnode import and edit history |
| `src/StageFlow.Engine/Graphics` | Direct3D, shaders, GPU surfaces, text atlas and preview |
| `src/StageFlow.Engine/Media` | Native FFmpeg, hardware frames, audio resampling and bounded prefetch |
| `src/StageFlow.Engine/Midi` | WinMM I/O and dedicated MTC scheduling |
| `src/StageFlow.Engine/Spout` | Registry and synchronized shared textures |
| `src/StageFlow.Engine/Streaming` | NVENC/AAC, memory MPEG-TS, HLS retention and HTTP |
| `src/StageFlow.App` | WinForms GUI, editing, recovery and integration harness |
| `web` | Local HLS viewer, pinned hls.js, no CDN |

## Performance rules

The render thread owns compositor/encoder resources. Commands enter through a
queue with task completion; the GUI never drives frame timing. Decode and MIDI
use separate threads. QPC drives transport and output cadence; stream timestamps
keep increasing when the show seeks or loops.

Decoded video remains in Direct3D textures. Composition and BT.709 NV12 conversion
run on the GPU, followed by NVENC. CPU video readback occurs only for requested
stills/tests. DMX uploads contain channel bytes, not bitmap images. Fonts use cached
atlases and instanced glyphs. Grid drawing is cached until DMX/layout changes.
Preview presentation runs at half the output cadence.

Video prefetch holds at most eight frames. Audio uses a two-second PCM ring and a
half-second loop-head cache. HLS retains the playlist plus two segments, with a
256 MB ceiling. Clients share immutable segment bytes. A device stall longer than
450 ms stops encoding with an error instead of silently issuing invalid timestamps.

Spout output uses keyed synchronization and a nonblocking acquire. A busy receiver
can miss an update without holding the render loop. Same-GPU sharing is required.
No screen capture is used.

## Extending the app

Add grid formats to the CPU reference encoder, GPU shader and importer together;
extend independent layout cases in `GridChecks`. Timecode changes require wire
and boundary tests. Keep MIDI callbacks bounded; never call WinMM from its callback.

Schema 1 rejects unsupported future schemas. Add explicit migration before a schema
bump. Application version is in `Directory.Build.props`; the GUI reads assembly
metadata. Update the changelog with releases and commit dependency upgrades with their restore locks.

AMF/QSV implementations belong behind the encoder module and must preserve the
GPU-resident and timestamp contracts; changing a codec name alone is insufficient.

GUI edits use immutable project snapshots with 100 undo entries. Streaming blocks
encoder dimensions/rate/settings changes until stopped. Failed project application
attempts restore the prior configuration; disconnected routes stay visible and
can be retried through Status.
