# StageFlow 2.0.0-alpha.3

A Windows show workspace for video, timecode, Art-Net grids, compositing and live
video routing. This is the independent **v2 fork**. V1 remains frozen.

Launch **StageFlow.App.exe** in the portable folder (this release is in
**StageFlow-v2-Portable-alpha.3**). Save and close the previous build before switching. The portable build bundles
.NET, the native media/network libraries and the HLS player. It runs as one
application process; OBS, MediaMTX, Hnode and an external FFmpeg process are not
required. Existing Windows MIDI ports, including loopMIDI, remain selectable.

Windows 10/11 x64 and a Direct3D 11 GPU are required. NVIDIA NVENC is the primary
hardware encoding path; CPU H.264 is also available. Hardware MP4 decoding remains
required. This alpha was validated on the supplied RTX 4080 computer. See
[validation](docs/VALIDATION.md) for measurements and remaining external checks.

## Start a show

1. **File → Open movie**, then **Import Hnode layout**, or open the supplied
   Sample Show sample. Import preserves your movie, clocks and existing live sources.
2. Open **Edit → Preferences** (Ctrl+,). Under **Timecode**, select Internal mode,
   the MTC rate and MIDI output to grandMA2, and enable output. Arm MIDI in the
   main window when ready. IncomingMtc mode instead follows the selected MIDI input.
3. Under **Art-Net**, select the interface/universe range receiving grandMA2's DMX.
   StageFlow converts that data directly into the grid overlay. MTC does not feed Hnode.
4. Select your local audio device under **Audio**. Local and program gains/mutes
   are independent. Movie audio is included in HLS and other program outputs.
5. Use **+ Add** for clock overlays, grids, Spout or network sources. Select a layer
   to edit it live in Inspector. Drag in the preview to position it; Shift snaps.
6. Configure encoding/destinations, then **Start stream** for HLS/RTMP/SRT.
   Spout and NDI/OMT have their own enable switches in Preferences.

The supplied layout is BinaryStageFlight, 2560×1440/60, with Art-Net bound to
127.0.0.1:6454. Close the old Hnode receiver when transferring that port to StageFlow.
For a remote grandMA2, select this PC's network interface and send ArtDmx to it.
Universe addresses are zero-based. Keep data grids at native size/full opacity,
with clocks outside the encoded data area.

All clock overlays follow the same master MTC. Each has independent positioning,
font, size, label, color, alpha, opacity and visibility. The color picker changes
RGB; enter #AARRGGBB to set exact alpha. Duplicate layers to add more clocks.

## Workspace and settings

The framed black canvas keeps the show aspect ratio when the window changes size.
A contrasting workspace surrounds it, so empty black output has visible boundaries. Resize the
hierarchy/inspector dividers, hide panes through View, float the inspector, or reset
the workspace. Menus, tabs, buttons and surfaces share the selected palette.

Preferences has global search and 16 named sections:

| Sections | Controls |
|---|---|
| General | Recovery, autosave interval, import/export personal preferences |
| Appearance | Slate, Graphite, Midnight or Light; five accents; 8–13 pt text; compact rows |
| Workspace | Pane widths, remembered window bounds, tooltips, status cards, snap spacing |
| Video & canvas | Show name, GPU, output dimensions and rational frame rate |
| Audio / Timecode | Device routes, independent gains/mutes, clock mode/rate and timing offsets |
| Art-Net / MIDI-DMX | Receiver interface/universes, separate MIDI-DMX route and handshake |
| Encoding / HLS server | Encoder, bitrates, keyframe/segment duration, server and playlist |
| Spout / NDI & OMT | Program output enable switches and advertised names |
| RTMP & SRT | Named output destinations and enable switches |
| OSC / OSC mappings | Command input, state feedback, destinations, add/remove/learn and live mapping properties |
| Diagnostics | Recording at startup, status cadence, logs and reconnect/reset tools |

Changes take effect on Enter or leaving an editor; there are no Apply buttons.
Invalid values are rejected. Encoder/canvas changes restart an active encoded
stream. GPU changes restart playback and disarm MIDI. Personal preferences save on
this computer automatically; show settings belong to the current .stageflow file.

V2 keeps its preferences/recovery/recent files in **%LOCALAPPDATA%/StageFlowV2**.
Opening a v1 project migrates it in memory and asks for a new save location. V2
schema-2 shows should not be opened in v1. Show saves are atomic and retain a .bak.

## Video routes

| Protocol | Input | Program output | Audio |
|---|---|---|---|
| Spout | Named shared texture | Named shared texture | No audio in the protocol |
| NDI | Source discovery or manual source | Named LAN sender | Stereo PCM |
| OMT | Source discovery or manual source | Named LAN sender | Stereo PCM |
| SRT | URL, hardware-decoded live layer | MPEG-TS destination URL | AAC input/output |
| RTMP / RTMPS | — | FLV destination URL | AAC |
| HLS | — | Built-in HTTP server and player | AAC |

Live-source audio starts disabled to avoid feedback; enable it per layer in
Inspector. It mixes with the movie before independent local/program gain controls.
Inputs retry after disconnect. Network output errors appear in Monitor; stop/start
stream or reconnect routes to retry a failed output.

Choose **h264_nvenc**, **hevc_nvenc**, or **libx264** under Encoding. HLS, RTMP and
SRT share one encoded video/audio program. RTMP requires H.264. HEVC playback depends
on the receiver; H.264/AAC is the browser compatibility default. Per-destination
transcoding, AMF and QSV are not implemented.

For RTMP enter the complete endpoint including the stream key. For SRT use a URL
such as srt://receiver:9001?mode=caller&latency=120000. The latency value is in
microseconds. Destinations are stored in the show file, including any keys in URLs.

Default HLS player: http://127.0.0.1:8888/; playlist: /live/index.m3u8.
**Player ↗** opens the local viewer. Remote clients use this PC's LAN address and
the selected port. HLS is conventional segmented HTTP playback, with seconds of
latency. The server has no login/TLS; firewall/router settings are not changed.
Browser audio needs a user Play gesture. NDI® is a registered trademark of Vizrt NDI AB.

## OSC controls and feedback

All OSC configuration is now in **Edit → Preferences → OSC** and **OSC mappings**.
The main inspector has no OSC tab. Add and Remove are explicit buttons. Choose a
mapping in the list to edit its control, trigger, target layer, range and feedback
properties below. Changes are live and undoable. Learn captures the next command.

For OSC Pilot on the same PC, enable the StageFlow receiver on port **9000** and
feedback to **127.0.0.1:9001**. In OSC Pilot, use Send To Port **9000** and Listen On
Port **9001**. On another computer/tablet, replace the feedback IP with that device's
LAN address. Input and feedback have independent switches and start off for old shows.

New mappings use unique /transport/play addresses. Changing the selected action
sets appropriate button/fader defaults and preserves a custom address. For example,
**/project/record → Play → Press** remains a valid custom mapping. Mute and visibility
use EveryMessage so zero switches them off; continuous controls use Value with a
0–1 range by default. SeekSeconds defaults to 0–60 seconds. Reset control defaults
restores trigger/range/argument/type without erasing the address or target.

Feedback reports actual transport, seek, audio, layer, stream and diagnostic states,
including changes made locally. Play returns 1 while playing and 0 otherwise. Leave
Feedback Address empty to use the command address, or set a separate indicator
address. Float32 is the default; Int32/Boolean and argument indices are configurable.
Value feedback reverses the configured range to normalized 0–1. Next/previous cue
buttons have no persistent state and return 0.

Default cadence is 100 ms, changed values only, plus a full refresh every two seconds
for restarted controllers. Send current state forces a refresh. A bounded background
sender keeps network operations away from the video/MTC threads. A sent count reports
UDP sends, not delivery acknowledgement. Use a separate feedback address if a remote
controller is configured to echo received values back as commands.

See [OSC setup and defaults](docs/OSC.md), including the OSC Pilot documentation.
Address wildcards and MIDI control mapping are not implemented in this build.

## Keyboard and recovery

- Space: play/pause. Left/right: one source frame. F11: full screen.
- Drag the timeline or enter HH:MM:SS:FF in Go to; drop-frame uses HH:MM:SS;FF.
- M: add marker; double-click to locate; right-click to rename/remove.
- Ctrl+Up/Down: previous/next cue. I/O: loop in/out. L: toggle loop.
- Ctrl+S / Ctrl+Shift+S: save / save as. Ctrl+O: movie. Ctrl+Shift+O: show. Ctrl+N: new.
- Ctrl+Z/Y: show undo/redo; text editors keep their own undo. Ctrl+,: Preferences.
- Drop a movie, show or Hnode file onto the window to open it.

Use Monitor to record performance, inspect route errors, reconnect devices or
clear DMX. **Start Stress Test.cmd** opens the supplied layout with recording on.
JSONL logs live in diagnostics/ and can be read while recording. Managed crashes
include full stacks and build version; read-only installs use the StageFlowV2
LocalAppData fallback. The source includes scripts/analyze-performance.ps1.

## Build and modify

Use Windows x64 and .NET SDK 10.0.401 (or a compatible .NET 10 SDK):

~~~powershell
.\scripts\prepare-native.ps1
.\scripts\prepare-network.ps1 -NdiRuntimeDirectory 'C:\path\to\NDI runtime'
.\scripts\build.ps1 -Test -EngineTest -UiTest -ProtocolTest -ArtifactsPath .\artifacts
.\scripts\package.ps1
~~~

The delivered runtime folder is already prepared. For a fresh source checkout,
prepare-network obtains pinned OMT binaries and takes the official NDI 6.3.2 DLL
from the supplied directory or NDI_RUNTIME_DIR_V6; it checks all hashes. Download
that runtime from https://ndi.link/NDIRedistV6. Native media preparation uses pinned
OBS dependency DLLs; the OBS application is not included. A VC++ x64 runtime may be
needed on a fresh PC. Manifests and third-party notices accompany the portable app.

Build artifacts can be redirected so developing v2 does not overwrite a running
preview. Package defaults to StageFlow-v2-Portable and rejects the frozen v1 paths.
Application version lives in Directory.Build.props; build-info.json records the
packaged source commit. Keep schema migration and tests with behavior changes.

See [architecture](docs/ARCHITECTURE.md), [review](docs/V2-REVIEW.md),
[compatibility](docs/COMPATIBILITY.md), and [upstream notices](docs/UPSTREAM.md).
StageFlow source is GPL-3.0-or-later; bundled libraries retain their own terms.
NDI's proprietary runtime is covered by its included agreement, not StageFlow's license.
