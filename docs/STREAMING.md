# RTMP publishing and encoding — v2 alpha.4

Open **Edit → Preferences → RTMP & SRT → RTMP / SRT destinations**. Add RTMP,
enter its details, enable the destination, then use **Start stream** in the workspace.
Edits commit when you press Enter or leave a text field. Active encoded outputs
restart after settings change. Destinations can be added, removed and undone.

| Field | Behavior |
|---|---|
| Server URL | RTMP/RTMPS server and application, such as `rtmps://host/live` |
| Stream Key | Exact publish name/key supplied by the service, including token/query suffixes |
| Authentication Enabled | Use the separate username/password for the server's RTMP challenge |
| Username / Password | Server credentials; separate from the stream key |
| Enabled | Include this destination when Start stream is active |

Most services require the stream key alone. Username/password support uses the
native RTMP Adobe/FMLE challenge flow. This is not HTTP Basic authentication or an
OAuth login. Full URLs from older shows remain valid when Stream Key is empty;
embedded URL credentials remain in effect unless the separate authentication
fields override them. A nonempty Stream Key makes the URL path the application
name, including nested paths. The key is passed separately as `rtmp_playpath` and
is not re-escaped, concatenated or logged by the destination status display.
These connection controls use the [FFmpeg RTMP protocol options](https://ffmpeg.org/ffmpeg-protocols.html#rtmp).

Keys and passwords are masked in the editor. Show files and recovery copies store
them as readable JSON; keep those files private. The bundled native RTMP backend
limits each username/password to 49 UTF-8 bytes and does not URL-decode credentials.
Validation rejects `/`, `?`, `#`, backslash and control characters in these fields,
plus whitespace and `: @ & = + %` in usernames, instead of silently changing the credential. RTMPS
uses the bundled TLS transport; public-service/TLS interoperability was not part
of the local authentication fixture.

## Shared encoder

**Preferences → Encoding** configures one video/AAC encoder shared by HLS, RTMP
and SRT. Each network destination has its own bounded queue and I/O worker. Changing
resolution/frame rate changes the program canvas too. Per-output transcoding is
not performed. Spout/NDI/OMT continue using their existing program-output paths.

| Control | Values and effect |
|---|---|
| Encoder | NVIDIA H.264, NVIDIA HEVC, or CPU x264; RTMP requires H.264 |
| Rate Control | Cbr, Vbr, ConstantQuality |
| Video bitrate | Target, 500–200000 kbps; unused in constant quality |
| Maximum bitrate | VBR/CQ ceiling; 0 uses target in VBR and is uncapped in CQ; ignored in CBR |
| Buffer size | VBV buffer at the maximum bitrate, 0.1–10 seconds |
| Quality | CRF for x264 / CQ for NVENC, 0–51; lower positive values give higher quality; default 23 |
| Preset | Performance (NVENC P3 / x264 veryfast), Balanced (P4 / faster), Quality (P6 / medium) |
| Profile | H.264 Auto/Baseline/Main/High; HEVC uses Main |
| Keyframe interval | 0.5–10 seconds; 0 follows HLS segment duration for compatibility with existing shows |
| B-frames | 0–4; 0 gives lowest latency; Baseline requires 0 |
| Output format | Shared program width/height and rational frame rate |
| Audio bitrate | AAC 32–512 kbps, with fixed 48 kHz stereo audio |

NVENC CQ 0 means automatic quality. x264 CRF 0 requests lossless encoding and needs
Auto profile; fixed H.264 profiles are rejected for lossless mode. The CPU path disables
lookahead to bound live latency. NVENC with zero B-frames uses ultra-low-latency
tuning; B-frames permit reordering and add latency. Higher-quality presets need
more encoding work. See the [FFmpeg encoder documentation](https://ffmpeg.org/ffmpeg-codecs.html#libx264_002c-libx264rgb)
for the underlying x264 rate-control terminology.

For common RTMP workflows start with H.264, CBR, High/Auto, a 2-second keyframe
interval and the receiving service's required bitrate. Use zero B-frames when
minimum latency matters. HLS segment length stays under **Preferences → HLS server**;
segments end at keyframes, and their fixed playlist target accounts for the GOP
and frame reordering. The codec test verifies timestamps across segment boundaries.

## Repeatable verification

Build with `scripts/build.ps1 -Test -UiTest`. Native encoder checks:

```powershell
$env:STAGEFLOW_NATIVE = (Resolve-Path runtime).Path
dotnet <EngineTests.dll> --encoding <artifact-directory>
node tests/rtmp-auth-fixture.mjs <dotnet.exe> <EngineTests.dll> <artifact-directory>
```

The RTMP fixture binds only to loopback on an ephemeral port. It independently
implements handshake/AMF, verifies the challenge response and exact key, observes
H.264/AAC, and rejects an incorrect password before receiving media. It requires
Node only for testing; the application has no Node or external RTMP server dependency.
