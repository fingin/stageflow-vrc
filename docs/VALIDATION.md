# Validation log — alpha.3

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

Run `StageFlow.App.exe --record` or use **Status → Start performance recording**.
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
