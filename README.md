# StageFlow 0.1.0-alpha.4

Windows show playback, master/follower MIDI timecode, Art-Net gridnode overlays,
GPU compositing, Spout and live HLS in one application process.

Open **StageFlow.App.exe** in the portable folder. No .NET installation, OBS,
MediaMTX, Hnode, VidPlayMTC, separate MTC sender, or FFmpeg executable is used at
runtime. Native libraries and the browser player ship beside the application.
Windows 10/11 x64, Direct3D 11 and an NVIDIA GPU with H.264 NVENC are required for
this build. Tested on this computer's RTX 4080. Existing Windows MIDI ports,
including loopMIDI ports, remain selectable; StageFlow does not install a driver.

This is a working **alpha**, with automated hardware and streaming checks. Actual
grandMA2 and VRChat/gridnode show validation remains to be performed. See
[validation](docs/VALIDATION.md) for measured results and exact limits.

## Start a show

1. Open a movie. Its audio feeds the selected local output and independently HLS.
2. Import the Hnode `.shwcfg`, or open `samples/Sample Show.stageflow`. The supplied
   configuration imports as BinaryStageFlight at 2560×1440/60. Loading a movie afterward
   preserves the imported grid layout.
3. In **Routing**, choose the MTC output for grandMA2, enable **Send master MTC**, and
   apply. Select local audio, volumes and timing offsets in the same tab.
4. Turn **MIDI outputs: ON** when ready to drive configured outputs. MTC goes to
   grandMA2; grandMA2's Art-Net drives the grid. MIDI-DMX has a separate route.
5. Add/duplicate clocks, arrange them in the preview, then **Start stream**.
   **Output → Open player** opens the built-in HLS viewer.

The supplied Art-Net bind is `127.0.0.1:6454`. Only one application can own that
endpoint: close Hnode's receiver when transferring that job to StageFlow. For
another machine, choose the local network interface or `0.0.0.0` and send ArtDmx to
this PC. Universe addresses are zero-based.

Default player: `http://127.0.0.1:8888/`. Playlist: `/live/index.m3u8`. Other computers
use this PC's LAN address and configured port. The server is HTTP on the selected
interfaces, without login or TLS. No firewall or router setting is changed.

## Controls

- Space: play/pause. Left/right: one source-video frame. Stop returns to the start.
- Drag the timeline or enter `HH:MM:SS:FF` in Go to. Rate and show-start offset
  determine displayed MTC; 29.97 drop-frame uses `HH:MM:SS;FF`.
- M: add marker; double-click to locate. Right-click to rename/remove.
  Ctrl+Up/Down: previous/next marker.
- I/O: loop in/out; L: toggle loop. The range must be positive and fit the movie.
- Drag unlocked layers to move them; use the lower-right handle to resize. Shift
  snaps movement to eight pixels. Grid dimensions are protected in the inspector.
- Ctrl+S: save, Ctrl+Shift+S: save as, Ctrl+O: movie, Ctrl+Shift+O: show, Ctrl+N: new.
- Ctrl+Z/Y: undo/redo. F11: full screen. Drop a movie, show or Hnode file to open it.

Clocks share master timecode, with independent position, label, font, size,
colors, opacity, visibility and layer order. There is no small fixed clock count;
project size and GPU capacity are the practical limits.

Projects are readable, versioned JSON (`.stageflow`), with relative media paths,
atomic saves and a previous-file `.bak`. Recovery autosaves run every 30 seconds.
**Show** includes recent projects. **Status** exposes timing, missed deadlines,
audio underruns and route errors, with reconnect/reset controls.

For stress testing, use `Start Stress Test.cmd` in the portable folder. It loads
the supplied Sample layout and enables diagnostic recording. The bottom bar shows
REC. Logs remain in `diagnostics/`, readable while the app is running. You can
also toggle recording in Status. The source includes `scripts/analyze-performance.ps1`
to summarize a saved JSONL log.

Managed crashes also save a `Crash-*.txt` report with the full exception and build
version. If the app folder is read-only, crash reports use
`%LOCALAPPDATA%\StageFlow\diagnostics`. Keep these reports with the performance log.

## Build and modify

Use Windows x64 and .NET SDK 10.0.401 or a compatible .NET 10 SDK:

```powershell
.\scripts\prepare-native.ps1
.\scripts\build.ps1 -Test -EngineTest -UiTest
.\scripts\package.ps1
```

Native preparation downloads a pinned, SHA-256 checked OBS dependency release;
only DLLs are copied. No OBS program is included or launched. Portable publishing
bundles .NET and ASP.NET. On another PC the Microsoft Visual C++ x64 runtime may
also be required. Versions and provenance are in `native-manifest.json` and `licenses/`.

See [architecture](docs/ARCHITECTURE.md), [compatibility](docs/COMPATIBILITY.md),
[validation](docs/VALIDATION.md) and [upstream attribution](docs/UPSTREAM.md).

StageFlow source is GPL-3.0-or-later; see `LICENSE`. Third-party components retain
their own notices. Source, lock files, build scripts, native build recipes and
source references accompany the portable app.
