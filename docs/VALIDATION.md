# Validation log — v2 alpha.5

## Alpha.5 Monitor follow-up (October 6, Pacific)

Release build: zero warnings/errors; **33/33 core checks**. Monitor checks cover
IPv4/IPv6 and interface-specific HLS links, endpoint/credential redaction,
per-destination failures independently of the shared encoder, disabled/unselected
states, stalled inputs, and stop/fatal transitions. UI checks cover initial HLS
selection, link actions, selection retention, privacy and live state updates, plus
the existing preferences, OSC feedback, menu, persistence and resize suite.

Native HLS checks pass for H.264 NVENC, HEVC NVENC and x264 with AAC. Production
NDI/OMT output and input checks verify real source identifiers, sent/received
frames, dimensions and GPU/audio delivery; the short shared staging test sent
157 frames per sender with zero reported drops/skips. SRT video/AAC loopback also
passes. The independent RTMP authentication fixture receives 212 video and 331
audio messages, verifies telemetry counters, and rejects an incorrect password
with zero publish/media messages. These are local checks, not public-service tests.

The 60-second application run opens Monitor during MP4 playback,
three changing Art-Net universes, twelve additional clocks,
Spout output and HLS/AAC at 2560×1440/60. The actual engine snapshot reports the
encoder running, HLS segments ready and Spout frames sent. After warmup, 45.03
measured seconds produced 2,700 frames, **zero missed deadlines and zero audio
underruns**, 1.48 ms mean / 9.00 ms peak render submission and wait, and 9.75% CPU
of one logical core. Working set moved from 295.6 to 286.8 MiB on the RTX 4080.
A final 30-second run after the initial-selection UI fix also passes: the live
HLS address and Open action are immediately available; its 15-second measured
window has 900 frames, zero missed deadlines and zero audio underruns.

An earlier run reported three cumulative audio underruns with zero missed video
deadlines; the unchanged repeat and the later run above were clean. The cause
was not established, so this remains an intermittent observation, not a claimed
audio fix. Monitor reads published playback state and does not add another poll
of the master timeline. These short tests do not replace a long show soak.

Monitor's Active state reports local sender activity. NDI/OMT do not report viewer
counts here; RTMP/SRT counters measure successfully submitted encoded payload, not
remote display confirmation. Spout input reports the shared texture and dimensions
without claiming a distinct-source-frame count. LAN links describe the configured
HTTP bind and active interfaces; firewall/remote reachability still needs checking
on the target network. Unselected encoders are not presented as GPU-certified.

## Alpha.4 follow-up (October 6, Pacific)

Release build: zero warnings/errors; **31/31 core checks**. UI checks cover masked
RTMP credentials/key, separate SRT properties, live encoding edits, persistence,
undo/redo and destination removal, plus the existing preferences/OSC/resize suite.

Twelve native cases (H.264 NVENC, HEVC NVENC, x264 × CBR/VBR/capped CQ/uncapped CQ) pass with 180 video
frames and AAC per case. Tests inspect actual packet timestamps, two-second GOPs,
H.264 Baseline/Main/High profiles and reordering with two B-frames. HLS publishes
three segments per case. A first-segment timestamp shift found by these checks was
fixed by disabling automatic timestamp offsets in the per-segment muxer.

An independent Node RTMP/AMF fixture accepts the production sender's Adobe-style
challenge response and exact key (including path/query/special characters), then
receives 212 video messages and 331 AAC messages. An incorrect password is rejected
with zero publish/media messages. This is local RTMP verification; public hosted
services and RTMPS certificates were not exercised.

OSC new-show defaults are 8000 for commands and 8001 for feedback. Existing explicit
project ports survive loading.

The 60-second application smoke run used example data provided by the user,
2560×1440 at 60 fps, three dynamic Art-Net universes, twelve additional clocks,
MP4 playback, Spout output and HLS/AAC. After warmup, 45.02 measured seconds produced
2,700 frames with **zero missed deadlines and zero audio underruns**. Mean render
submission/wait time was 1.51 ms; peak was 5.58 ms. CPU use was 8.61% of one logical
core; working set moved from 280.2 to 266.3 MiB. This is a short regression run,
not a long-duration soak or a 45-universe measurement.

The pre-existing grid-allocation check failed once during a rebuild/test run; an
isolated rerun of the same compiled suite passed 31/31 with zero measured mask-loop
allocation. The grid implementation was unchanged. This intermittent check remains
noted rather than treated as a reproduced production regression.

## Alpha.3 follow-up (October 6, Pacific)

Release build: zero warnings/errors; **29/29 core checks**. New checks cover OSC
feedback state projection, inverse ranges, wire types, UDP delivery, periodic
refresh, validation, backward-compatible defaults and project persistence.

UI integration verifies OSC inside Preferences, explicit Add/Remove, inline
enable, action defaults, custom addresses, undo/redo, close/reopen and local
volume edits received over real UDP. All 16 Preferences sections and two workspace
tabs pass the UI suite. Preview tests include 16:9, portrait and square canvases
with a visible contrasting frame at different window sizes.

OSC Pilot interaction itself remains a manual check; the automated receiver
independently decodes the actual packets sent by the application. Engine/media
paths did not change in alpha.3; performance measurements below are from alpha.2.


## Current v2 checks (2026-10-07 UTC)

Release build has zero warnings/errors. Core checks: **24/24**. UI checks cover
200 menu dismissals, the alpha.1 inspector property-owner crash, all 15 Preferences
sections, global search, four live skins, consecutive settings edits, undo/redo,
invalid-value rollback, save persistence, recovery and resize layout. Windows
1100×720, 1920×1080, 1200×900 and 1500×780 keep the preview aspect; action layout on all three workspace tabs is
also checked at 8, 9 and 13 pt. Small panes scroll instead of hiding the layer list.

The following native checks passed after the v2 changes:

- Spout discovery/share/cleanup, 22 GPU grid/orientation/mask cases (193,900 samples)
  and 13,312 BinaryStageFlight reference cells.
- H.264 NVENC, HEVC NVENC and libx264 each produced HLS video/audio packets.
- Native NDI and OMT video/audio loopback, then production sender/input-to-GPU and
  PCM mixing checks. The shared GPU staging check delivered 157 frames per sender,
  with zero reported dropped frames or GPU skips in this short check.
- Production SRT H.264/AAC output → input → GPU/audio loopback.
- RTMP output received independently by a development-only FFmpeg listener;
  ffprobe reports H.264 640×360 plus AAC 48 kHz stereo, 3.021 seconds. That listener
  is a test fixture and is not shipped or required by StageFlow.

### Full-load performance

RTX 4080, supplied movie, 2560×1440/60, 45 continuously changing
Art-Net universes, 12 timecode overlays, Spout output and encoded HLS/AAC. The run
lasted 60.91 s with a 45.32 s measured window after warm-up:

| Metric | Measured result |
|---|---:|
| Rendered frames | 2,718 |
| Missed deadlines | 0 |
| Audio underruns | 0 |
| Mean / peak CPU render submission + wait | 2.19 / 13.70 ms |
| Process CPU | 61.34% of one logical core |
| Working set | 335.5 → 340.2 MB |
| Engine / media errors | None |

This is a short-run measurement on this machine, not a guarantee across GPUs,
codecs, network receivers or long shows. It did not include simultaneous NDI/OMT
or a browser viewer. Those routes have separate focused loopback checks. Native
window capture timed out twice; regression renders from the app were inspected,
but a complete manual/accessibility audit was not possible in this environment.

Actual grandMA2 timing/chase, the target-world decoder, physical audio listening
and long-duration soak remain external checks. See V2-REVIEW.md for review findings
and known protocol limits. V1's earlier measurements below are historical and
must not be read as current v2 performance.

---

# Historical v1 validation — alpha.4

This document distinguishes implemented behavior from external show certification.
Performance figures below are measured on this PC's RTX 4080, not universal claims.
Render milliseconds measure CPU submission/wait time, not GPU timestamp queries.

## Passing checks

- Release build: zero warnings/errors; 19 core checks covering timecode/drop-frame,
  MTC wire messages, transport/follower behavior, Art-Net/ArtSync, MIDI-DMX wire
  scheduling, Hnode import, project saves and undo/redo.
- 22 GPU grid/orientation/mask cases: 193,900 cell samples match independent layouts.
- BinaryStageFlight: 13,312 cells match the CPU reference; encoded-grid verification
  independently decodes HLS and compares binary thresholds.
- D3D11 MP4 decode: 150 GPU frames and a two-second seek. Actual AAC audio decode,
  stereo resampling, and a sample-by-sample loop-boundary comparison pass.
- In-process NVENC/AAC → five MPEG-TS/HLS segments; independent ffprobe sees H.264
  video and AAC audio, including initial audio packets. HTTP player, playlist and
  MPEG-TS byte-range requests pass.
- Spout discovery and texture sharing between two Direct3D devices pass. Official
  SpoutLibrary.dll independently reads sender metadata.
- Actual browser playback reaches 2560×1440, readyState 4 and advancing video time,
  with video, grid and twelve synchronized clocks visible.
- GUI integration exercises add clock, undo, redo, remove, pause, seek, loop,
  still export and streaming. Native GUI automation capture timed out; do not
  interpret the harness as a completed manual interaction/accessibility review.

## Alpha.4 crash regression

Windows recorded the same `ObjectDisposedException` in alpha.3 at 19:16:38 and
19:27:14 local time on October 6. `ShowFileMenu` disposed its dropdown inside
`Closed`, while WinForms' modal-menu filter still required its window handle.
An isolated UI suite reproduced the identical stack through
`Application.FilterMessage` before the fix. The corrected form-owned menu passes
100 outside-click dismissals (50 with the Recent submenu open) plus 100 explicit
keyboard/item dismissals, and is disposed at owner shutdown. Tests use a separate
temporary state folder and open no hardware routes. This exercises real WinForms
menu filtering, without injecting global mouse/keyboard input.

Crash report tests verify full inner exceptions/stacks, a writable fallback and
safe failure when neither destination is writable. Abnormal form disposal
preserves the recovery file. Native access violations still depend on Windows
Error Reporting; managed crash logs do not replace native dumps.

## Performance measurements

All runs include hardware MP4 decode, 1440p60 output, twelve clocks, Spout output,
local audio muted, and AAC/HLS. Synthetic media loops every five seconds.

| Workload | Measured interval | Frames | Missed deadlines | Mean / peak render | Audio underruns |
|---|---:|---:|---:|---:|---:|
| Static 3-universe grid + browser viewer, before loop fix | 75.04 s | 4,500 | 2 | 1.48 / 16.24 ms | 6 |
| Static grid after loop fix | 45.03 s | 2,700 | 0 | 1.45 / 11.93 ms | 0 |
| Continuously changing 45 universes + browser viewer | 165.34 s | 9,660 | 253 | 3.32 / 151.75 ms | 0 |

The full-capacity dynamic test **fails** the current <1% late-frame gate (about
2.55%). It receives 510,300 Art-Net packets; memory falls from 335 MB to 289 MB.
This remains an optimization target. The user is now running real-workload stress
tests with diagnostic recording. The deliverable remains an alpha, not a claim
that the performance requirement is fully met for all workloads.

Alpha.3 removes per-channel mask allocations and caches mask flags. The focused
45-universe × 60-update allocation check reports **0 bytes**. Grid buffers now use
the configured capacity, rather than always reserving 512 universes. Core and GPU
correctness checks pass after this change. A new full-workload comparison is pending;
the earlier stress numbers above must not be presented as post-change measurements.

## Recording a real workload

Run `StageFlow.App.exe --record` or use **Monitor → Start performance recording**.
The bottom bar displays **REC**. A JSONL file is written once per second in the
portable app's `diagnostics` directory. It includes interval frame/Art-Net rates,
missed deadlines, render phases for slow frames, CPU, working/private/managed
memory, GC counters, audio underruns, route errors, output settings and media
filename. It does not record video pixels, audio samples or DMX values.

Recording runs on a background task. Logs can be read while the application is
running. Closing the app or stopping recording flushes them. **Open diagnostic
logs** opens the folder. Preserve logs around a reported hitch, with its approximate
time and the action being performed, so changes can target an observed bottleneck.

## External checks still outstanding

- Actual grandMA2 timecode chase, MIDI timing against an independent hardware clock,
  physical audio output listening/latency, VRChat/gridnode world decoding and
  MIDI-DMX handshake in that world.
- Wider media/codec/device combinations and long-duration production soak.
- Manual GUI stress review by the user and optimization of observed issues.

No unrelated installed app, MIDI route, firewall or graphics-driver setting was
modified for testing. Automated Art-Net/HLS fixtures use loopback test ports.
