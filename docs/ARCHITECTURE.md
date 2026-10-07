# V2 architecture and modification guide

## Runtime path

```
master/follower clock ── MTC output ── grandMA2
           │                            │ Art-Net
           │                 bounded DMX universe store
           │                            │ GPU grid serializer
MP4 ─ hardware video decode ─ GPU compositor ─ optional Spout sender
 │                           ↑ clocks / Spout / NDI / OMT / SRT inputs
 └ audio decode/resample     │
       ├ selected WASAPI     └ D3D11 NV12 conversion → NVENC H.264/HEVC or CPU H.264
       └ independent gain → AAC ────────┬─────────────────────┘
                                memory MPEG-TS segments
                                in-process HTTP HLS server + RTMP/SRT packet sinks

GPU program → optional staging ring → NDI / OMT program + stereo audio
OSC UDP → bounded control queue → transport / routes / live project edits
```

MTC never feeds Hnode or Art-Net input. MIDI-DMX is a separate optional output
with the Hnode/VRChat readiness handshake. Incoming MTC selects follower mode;
local transport commands and MTC echo are suppressed in that mode.

## Module boundaries

| Directory | Responsibility |
|---|---|
| `src/StageFlow.Core/Timing` | Rational frame rates, drop-frame arithmetic, MTC wire codec and monotonic transport |
| `src/StageFlow.Core/ArtNet` | ArtDmx/ArtSync, bounded universe state and MIDI-DMX scheduler |
| `src/StageFlow.Core/Osc` | OSC wire codec, bundle scheduling, mapping validation and action routing |
| `src/StageFlow.Core/Grid` | Hnode-compatible reference layouts and masks |
| `src/StageFlow.Core/Projects` | Schema, validation, atomic saves, Hnode import and edit history |
| `src/StageFlow.Engine/Graphics` | Direct3D, shaders, GPU surfaces, text atlas and preview |
| `src/StageFlow.Engine/Media` | Native FFmpeg, hardware frames, audio resampling and bounded prefetch |
| `src/StageFlow.Engine/Midi` | WinMM I/O and dedicated MTC scheduling |
| `src/StageFlow.Engine/Spout` | Registry and synchronized shared textures |
| `src/StageFlow.Engine/Streaming` | H.264/HEVC/AAC, memory MPEG-TS, HLS retention, HTTP and bounded RTMP/SRT sinks |
| `src/StageFlow.Engine/Network` | Native NDI/OMT ABI, discovery, sender workers, staging and live input/audio |
| `src/StageFlow.App` | WinForms GUI, editing, recovery and integration harness |
| `web` | Local HLS viewer, pinned hls.js, no CDN |

## Performance rules

The render thread owns compositor/encoder resources. Commands enter through a
queue with task completion; the GUI never drives frame timing. Decode and MIDI
use separate threads. QPC drives transport and output cadence; stream timestamps
keep increasing when the show seeks or loops.

Decoded video remains in Direct3D textures. Composition and BT.709 NV12 conversion
run on the GPU, followed by NVENC on the hardware path. Optional libx264 uses CPU encoding.
NDI/OMT use a shared three-texture staging ring and pooled CPU frames because the
selected SDK send interfaces accept CPU buffers; the render thread never waits
for a sender. Stills/tests also read back. DMX uploads contain channel bytes, not bitmap images. Fonts use cached
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

Schema 2 migrates schema 1 and rejects unsupported future schemas. The UI clears
the source save path for v1 projects, so the original is preserved. Add explicit
migration before a schema bump. Application version is in `Directory.Build.props`; the GUI reads assembly
metadata. Update the changelog with releases and commit dependency upgrades with their restore locks.

AMF/QSV implementations belong behind the encoder module and must preserve the
GPU-resident and timestamp contracts; changing a codec name alone is insufficient.

GUI edits use immutable project snapshots with 100 undo entries. LiveProperties
wraps cloned records; property ownership and converters must remain valid for
WinForms PropertyGrid. Setters validate before queuing. A 40 ms coalescer commits
the newest snapshot, while WorkingProject includes pending/in-flight values.
Project/file operations drain that queue and serialize against edits. Autosave and
Save use WorkingProject. Do not mutate records already handed to the render thread.

Encoder dimensions/rate/settings edits stop/apply/restart an active stream. The
UI records the applied settings before attempting restart; an output failure leaves
the settings visible and the failed stream stopped. Engine-side validation still
rejects unsafe in-place encoder changes. Disconnected routes remain visible.

Personal AppPreferences are separate from show schema. Preference section builders
share the same descriptors as Inspector; search wraps those descriptors without
losing converters, editors or ownership. Theme and WorkspaceControls own presentation.
Do not put frame scheduling or per-frame GPU work in preference setters.

NDI/OMT sender queues hold two frames each; RTMP/SRT sinks hold at most 256 encoded
packets. A slow destination reports failure/drop state instead of growing memory
or blocking render. NDI/OMT/SRT input receivers run on worker threads, hold the latest
video frame and bounded PCM, and retry failed input connections. Turning on live
input audio is explicit. Program gain is applied after mixing, independently of
local monitoring. NDI/OMT enabled switches are independent of encoded Start stream.

OSC uses a 512-message queue, max 128 UI dispatches per tick and bounded bundle
depth/element counts. Future timetags are limited to one minute and 512 pending
messages. Address routing is compiled when settings change. Learn needs an active
listener and consumes one message. Keep control callbacks out of the render loop.

Build/package scripts support a separate ArtifactsPath. Package refuses frozen v1
folders and records source revision/version. Native hashes are pinned in separate
media/network manifests. Optional native library handles are cached for process
lifetime; do not unload while any sender/receiver worker can call them.

## Alpha.3 OSC feedback

OscFeedbackProjector derives controller state from applied project and transport
snapshots, applies inverse input scaling and typed output arguments, deduplicates
changes and refreshes periodically. OscFeedbackSender owns a UDP socket and a
one-state mailbox on a worker task. UI polling never waits for network sends.
Feedback can run with command reception disabled. OSC mapping controls are created
inside Preferences and disposed with that window; the runtime remains independent.
