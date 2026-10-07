# Compatibility and operating scope


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

HLS contains H.264/AAC by default (HEVC is selectable), normally one-second MPEG-TS segments and six playlist entries.
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
resolution/bitrate and inspect Monitor.

## V2 network protocols

NDI and OMT support discovered/manual LAN video sources and named program senders
with stereo audio. SRT accepts a live source URL and can send MPEG-TS to configured
destinations. RTMP/RTMPS output uses H.264/AAC in FLV. All encoded destinations share
one codec/bitrate/rate; independent per-output encoding profiles are not provided.

NVENC H.264, NVENC HEVC and CPU libx264 are selectable. AMD AMF/Intel QSV are not
implemented; libx264 does not remove the hardware-decoding requirement. NDI/OMT
SDK paths use CPU video buffers with nonblocking GPU staging; do not assume Spout's
zero-copy cost. NDI HX/HDR interoperability and large multi-source workloads have
not been certified. Native runtime provenance and notices are included.

Live input audio is optional per layer and feeds both local and program mixes.
Inputs automatically retry disconnects. Failed RTMP/SRT destinations require a
stream restart. There is no virtual camera, HLS/RTMP input picker, recording-to-file
or external MIDI control mapper in v2 alpha.4. OSC diagnostic recording captures
performance logs, not a movie.

OSC listens on the configured UDP interface/port with exact-address mappings.
Mapped state feedback is available with a configured destination IP/port. No
authentication or address wildcard matching is implemented. See OSC.md.
RTMP supports separate stream-key and username/password challenge-authentication
fields. Encoder preferences include rate control, presets, profile, GOP and B-frames.
See STREAMING.md for supported field values and native credential limits.
Project files contain destination URLs, stream keys and passwords in readable JSON.
