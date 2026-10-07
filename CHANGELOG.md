# Changelog

## 2.0.0-alpha.4 — 2026-10-06 (Pacific)

- Set new-show OSC command/feedback defaults to 8000/8001; preserve saved custom ports.
- Add separate RTMP server URL, stream key and optional username/password authentication
  in the live destination editor. Mask keys/passwords, preserve legacy full URLs,
  and keep RTMP-only fields out of SRT destinations.
- Expose CBR/VBR/constant quality, target/maximum bitrate, VBV buffer, performance/quality
  presets, H.264 profile, keyframe interval and B-frames in Encoding Preferences.
  Include shared output dimensions/frame rate and AAC bitrate/format in the same page.
- Keep a single shared encode for HLS/RTMP/SRT. Compute HLS target duration from GOP
  timing, and preserve continuous timestamps across segments with reordered frames.
- Verify native encoder settings across twelve codec/rate-control cases, live
  editor persistence/undo, and independent RTMP challenge authentication with positive
  and rejected-password cases.

## 2.0.0-alpha.3 — 2026-10-06 (Pacific)

- Move OSC entirely into Preferences, with separate connection and mapping sections.
- Add actual-state UDP feedback, independent destination/enable settings, per-mapping
  addresses/types/arguments, inverse fader scaling, change suppression and periodic resync.
- Add explicit mapping Add/Remove, inline enable switches, unique addresses, appropriate
  button/fader defaults, target-layer selection and a reset-defaults action.
- Keep the black preview canvas visibly framed against a contrasting workspace;
  preserve landscape, portrait and square aspect ratios during window resizing.
- Add five core feedback checks and UI/UDP integration checks for local edits, mapping
  lifecycle, persistence and preferences reopening. Release build: zero warnings/errors.


## 2.0.0-alpha.2 — 2026-10-07

- Independent v2 branch, schema migration and isolated preferences/recovery; v1 frozen.
- Modern workspace, consistent menus/tabs, aspect-safe preview, responsive transport,
  scrolling hierarchy, floating inspector, timeline markers and application icon.
- Fifteen searchable Preferences sections, four skins, five accents, live text size,
  remembered layout, preference import/export and clock color pickers.
- Live property edits with validation, coalescing, undo/redo and pending-edit autosave.
- Fix alpha.1 property-owner crash, menu lifetime, clipped controls, text undo capture,
  async file/edit races, import settings loss and OSC reconnect handling.
- RTMP/SRT outputs, H.264/HEVC NVENC and CPU H.264 encoder choices; shared AAC program.
- NDI/OMT input/output and SRT input, optional live-source audio, source discovery,
  reconnect status, bounded network queues and GPU staging.
- OSC receiver, custom address/action mappings, learn, triggers, values and bundles.
- Extend core/UI/protocol checks, review documentation, pinned network runtimes and
  reproducible separate portable publishing.

## 2.0.0-alpha.1 — internal preview

- Initial v2 workspace and live inspector preview. Superseded by alpha.2.


## 0.1.0-alpha.4 — 2026-10-06

- Fix the Show menu crash when clicking outside it: retain the menu until its
  owning window is disposed, and refresh recent projects when it opens.
- Capture managed UI/process failures with version, runtime and full exception
  stacks in `diagnostics/`, with a LocalAppData fallback for read-only installs.
- Stop media/MIDI/recording resources on abnormal UI-loop exit while retaining
  the recovery autosave. Dispose reusable menus and UI timers on shutdown.
- Add a regression suite that reproduces the original WinForms message-filter
  crash and checks 200 menu dismissals, nested menus, logging and recovery.

## 0.1.0-alpha.3 — 2026-10-06

- Add background JSONL performance capture, visible REC state, slow-frame phase
  timings, CPU/memory/GC/route metrics and a log analysis script.
- Remove per-channel mask allocations, cache masks and right-size GPU grid buffers.
  The 45-universe × 60-update mask check allocates zero bytes.
- Preserve route errors when streaming starts; retain standalone native dependencies.
- Add a ready-to-load Sample project and stress-test launcher.
- Full dynamic-workload performance remains under review; see validation results.

## 0.1.0-alpha.2 — 2026-10-06

- Integrated Direct3D MP4 decode/compositor, local WASAPI audio, NVENC/AAC and
  in-process MPEG-TS/HLS/HTTP playback without external media processes.
- WinMM MTC master/follower, ArtDmx/ArtSync, Hnode grid layouts and MIDI-DMX handshake.
- Spout input/output with keyed cross-device synchronization and SDK discovery check.
- Multiple timecode layers, editable transforms/styles, timeline, source frame steps,
  markers/rename/navigation, loops, undo/redo, atomic projects, recovery and device routes.
- GPU NV12 encoding path, audio loop prefetch and codec seek preroll. Preserve
  opening AAC packets while video encoding warms up.
- Hardware/HTTP/browser checks and self-contained Windows publishing.

## 0.1.0-alpha.1 — in development

- Establish modular solution and delivery contract.
- Implement transport/timecode, protocol and grid encoding foundations.

This version is not yet a complete usable release.
