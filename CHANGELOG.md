# Changelog

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
