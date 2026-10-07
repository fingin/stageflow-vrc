# V2 code review — 2.0.0-alpha.2

The review covered the application/settings lifecycle, core timing/protocols,
project validation/persistence, media/GPU/streaming modules, native interop and
packaging. Fixes are in v2 only. This is a source review with targeted automated
verification, not an independent security audit or production certification.

## Findings addressed

Alpha.4 adds validated RTMP publish-key/authentication fields and native encoder
controls. The destination editor participates in live commit, persistence, undo,
project-operation exclusion and theme updates. Removing/undoing a destination
clears stale descriptors. Keys/passwords are masked in the UI and omitted from
destination record display text; show/recovery JSON remains readable by design.
Native credential length/delimiter limits fail validation instead of truncating.
An independent challenge server verifies successful and rejected authentication.

Encoding review found per-segment timestamp offsets broke B-frame continuity.
The muxer now preserves the shared clock, and twelve native encoding cases inspect
packet timestamps/profiles/GOPs/audio. HLS target duration accounts for keyframe
spacing; all encoded destinations continue sharing one encoder. Full details and
the short alpha.4 performance run are in VALIDATION.md and STREAMING.md.

| Finding | Change and verification |
|---|---|
| Alpha.1 inspector could crash inside WinForms property expansion | Custom descriptor now returns its property owner/converter; all 15 settings sections are exercised |
| Fixed-height rows clipped actions; resizing stretched preview | Wrapped rows measure their height after layout, small hierarchy scrolls, preview child fits canvas aspect; 1100–1920 px window checks and 8–13 pt text checks |
| Native tabs/menus ignored the selected palette | Shared theme renderer, custom workspace tabs, rounded controls and owner-drawn selectors; rendered UI artifacts inspected |
| Rapid property changes could overwrite pending values | Edits compose against the latest snapshot and coalesce; consecutive edits, undo/redo, validation rollback and save tests pass |
| File operations could race asynchronous edits | Drain edits and serialize project operations; disable affected editors during the operation and defer close until work finishes |
| Autosave could omit a pending edit | Recovery saves the current working snapshot; abnormal disposal preserves recovery |
| Global undo intercepted text editing | Focused editors retain their own keyboard undo |
| Lost mouse capture could leave scrubbing suspended | Capture loss finishes the seek and clears scrub state once |
| Hnode import discarded v2 routes/live sources | Preserve clock/audio/output/OSC/GPU settings, live sources, markers and loop range while replacing imported grids |
| OSC reconnect could keep a failed listener | Explicit route reconnect recreates the OSC listener; bounded UDP/bundle dispatch remains |
| Native constructor failure could retain partial resources | Dispose partially constructed LAN staging slots and network packet sinks |
| BGRX input alpha could make video invisible | Normalize BGRX alpha before GPU upload |
| V2 packaging could overwrite v1 or a running preview | Separate output/artifact paths, frozen-directory guards and source-revision build metadata |

## Verification

- Release build: zero warnings/errors; 24/24 core checks.
- UI: 200 real menu-filter dismissals, nested Recent menu, descriptor ownership,
  all Preferences sections, live skin persistence/search, consecutive edits,
  undo/redo, invalid-value rollback, project save, resizing and recovery.
- GPU: 22 grid/orientation/mask cases, 193,900 independent cell samples, Spout sharing.
- H.264 NVENC, HEVC NVENC and libx264 each emit HLS video/audio packets.
- Production NDI/OMT sender → receiver → GPU layer/audio loopback; 157 frames per
  staging-ring sender, zero reported drops/skips in the focused check.
- SRT H.264/AAC loopback through production input/output. RTMP independently
  received by a development-only FFmpeg fixture and probed as H.264/AAC.
- Full-load smoke: 1440p60, 45 changing universes, 12 clocks, Spout and HLS;
  45.32 s measured, 2,718 rendered frames, zero missed deadlines/underruns,
  2.19 ms mean CPU render submission/wait time. See VALIDATION.md for limits.

## Remaining operating limits

Manual interaction/accessibility coverage is incomplete: native GUI screenshot
capture timed out in this environment. App-generated regression renders were
inspected instead. Physical grandMA2 chase, target-world grid decoding, hardware
MIDI/audio timing and a long production soak still require the actual show setup.

RTMP/SRT outputs require manual restart after a failed destination; receiver
reconnect is automatic. NDI/OMT performance at 4K/multiple independent sources is
not certified. LAN SDK paths require CPU frame transfers, unlike Spout/NVENC's GPU
path. There is no per-output transcoding, HDR tone mapping, LTC,
ArtPollReply or multi-controller Art-Net merge. Runtime/license scope is documented
in COMPATIBILITY.md and UPSTREAM.md.

## Alpha.3 follow-up

OSC feedback and the Preferences mapping editor are now implemented. The main
workspace contains Inspector and Monitor only. Five new core checks bring the
total to 29/29; UI checks additionally exercise actual local-edit UDP feedback,
add/remove/undo, defaults and reopening. Preview framing now distinguishes the
canvas from its workspace and checks portrait/square as well as landscape ratios.
See OSC.md for configuration and semantics.
