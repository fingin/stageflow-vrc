# Compatibility and operating scope

## Supplied show

`Sample Show.shwcfg` imports as **BinaryStageFlight**, not VRSL. VRSL is an inactive
deserializer. Output is 2560×1440/60, Art-Net is 127.0.0.1:6454, and unlimited
serialization becomes received channel extent bounded by 45 full universes of canvas
capacity. Original YAML is preserved inside the project; the `.shwcfg` is unchanged.

Native formats: BinaryStageFlight/MDMX, Binary, Ternary and VRSL, including VRSL's
four orientations, gamma and RGB packing. Channel/inverse masks and zero transparency
are imported. Keep data grids at native pixel size and full opacity, with clocks
outside their data area. Overpainting, scaling, low bitrates or downstream resizing
can corrupt encoded DMX. The layer inspector protects grid dimensions.

Configurations requiring Hnode's Spout-grid transcoding or arbitrary DMX generators
are rejected with an explanation. The supplied configuration uses neither. Other
exporters remain in the embedded YAML but do not run. This replaces the supplied
show's active workflows, not every Hnode plugin.

## Art-Net and MIDI

ArtDmx receives unicast/broadcast IPv4 on the selected interface. ArtSync groups
updates from the current controller, with four-second expiry. Length/version and
universe bounds, sequence rollover and sequence-zero operation are handled. This
build does not advertise ArtPollReply nodes or implement multi-controller HTP/LTP
merge. Configure grandMA2's destination directly. Last accepted values are held
until new data or **Clear received DMX**.

MTC supports 24, 25, 29.97 drop-frame and 30 fps, full-frame locate, quarter-frame
I/O, offsets and sync-loss display. Missing incoming timecode holds after 500 ms.
Reverse incoming MTC locates frames; continuous reverse media playback and LTC
audio decoding are not implemented.

Ports are existing Windows MIDI ports, selected by name. Use separate ports for
grandMA2 MTC and gridnode MIDI-DMX. Outputs start disarmed. MIDI-DMX implements
banked updates, change priority, fair refresh, watchdog and MIDIREADY log handshake.
It follows the VRChat log automatically, or a selected log/Unity Editor log. Actual
grandMA2 and target-world validation remains outstanding.

## Media and output

Seekable SDR video must decode through FFmpeg/Direct3D to NV12/P010. H.264 MP4/AAC
is the primary tested path. HDR PQ/HLG is rejected; no tone mapper is included.
Unsupported media reports an error instead of silently using CPU decoding.

Audio becomes 48 kHz stereo, downmixed when needed. Local WASAPI device/gain/mute
is independent of HLS gain/mute. Positive audio offset advances audio; positive
MTC output offset advances timecode. Local device buffers and downstream HLS latency
remain. The browser requires a Play gesture for audio.

HLS contains H.264/AAC, normally one-second MPEG-TS segments and six playlist entries.
This is conventional HLS, not LL-HLS/WebRTC; expect seconds of latency. All viewers
share one encoder. Stop closes the listener. The page retries after stream restart.

Spout input/output uses the playback GPU. Cross-device sharing and independent
official-SDK discovery checks are included in validation. Every OpenGL/OBS plugin
version has not been certified.

## Recovery and limits

Relink missing media with Open movie. Refresh devices and Reconnect routes handle
device changes. Changing GPU restarts playback and disarms MIDI. Full GPU device
removal requires restarting the app.

Projects have allocation bounds, which do not imply that every 8K/240 fps setting
or thousands of clock styles fits the GPU. Rehearse the actual show at its output
resolution/bitrate and inspect Status.
