# StageFlow delivery contract

Status: implementation in progress. This document is not a claim of completed features.

## User workflows

1. Load a show movie, route its local audio, rehearse using a timeline, markers,
   scrubbing, frame stepping and loop ranges, and drive grandMA2 with MTC.
2. Alternatively follow an external MTC master, chase the movie and overlays,
   and expose sync lock, rate, offset and loss-of-signal state.
3. Receive grandMA2 Art-Net and encode the DMX data as a compatible gridnode
   video layer. Hnode's purpose is data transport inside video, not a decorative
   lighting visualization. Native grid generation is primary; Spout is optional.
4. Arrange video, multiple clocks and grid layers; preserve encoded grid values,
   pixel geometry and downstream decoder expectations. Allow normal compositing
   for ordinary layers without silently corrupting data layers.
5. Publish synchronized picture, grid data, overlays and MP4 audio as HLS from
   this application. No MediaMTX, OBS process, screen capture, external server
   or separate FFmpeg process in the delivery architecture.
6. Optionally receive and publish Spout textures for interoperability.
7. Save/load projects, recover changes, undo edits, diagnose route failures,
   reconnect inputs and understand actual performance and stream status.

## Engineering constraints

- One Windows x64 application process; native libraries are permitted.
- Native GPU work through Direct3D 11, hardware decoder and hardware encoder.
- UI never schedules video frames or MTC messages.
- Bounded queues; no unbounded video buffering or per-frame bitmap capture.
- Independent show time and monotonically increasing streaming timestamps.
- Audio and video derive from a coherent media clock. Selectable local endpoint,
  local gain/mute independent of encoded stream gain/mute.
- Versioned source, semantic versions, changelog, locked dependencies, versioned
  project schemas, migration hooks, reproducible scripts and automated tests.
- Do not mark the goal complete because a partial build or GUI works.

## Implementation checkpoints

- [ ] Core transport, MTC encode/decode, project persistence and protocol tests.
- [ ] Art-Net reception, node discovery, compatible grid rendering, MIDI-DMX.
- [ ] GPU device, video/audio playback and compositor.
- [ ] MTC Windows device input/output and synchronization.
- [ ] In-process hardware encoding, HLS muxing, HTTP server and player page.
- [ ] Optional Spout texture I/O.
- [ ] Complete desktop workflows and diagnostics.
- [ ] End-to-end decode tests, sustained performance tests and packaged app.

## Current environment

Local .NET SDK 10.0.401 is in ../../work/toolchain/dotnet.
Available GPU hardware: RTX 4080 and RTX 3060 Ti. Runtime must select the
correct GPU and diagnose cross-adapter Spout sources.
Hnode upstream source is inspected under ../../work/reference/HNode; MIT
license and exact upstream revision must accompany adapted serializers.

Latest steering: affordances take priority over copying existing application
boundaries. MTC input is required. Spout is useful but no longer required for
the core Art-Net to gridnode to HLS workflow. grandMA2 remains external.
