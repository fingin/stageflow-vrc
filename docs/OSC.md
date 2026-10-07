# OSC commands and feedback — v2 alpha.3

## Configure the connection

Open Edit → Preferences → OSC. Command reception and feedback transmission have
separate switches. Configure the addresses/ports before enabling them. These are
show settings: save the show to retain them. Old shows keep their custom mappings;
feedback remains off until enabled.

| Setting | StageFlow default | OSC Pilot on the same PC |
|---|---|---|
| Command interface | 0.0.0.0 | Send to 127.0.0.1 |
| Command input port | 9000 | Send To Port 9000 |
| Feedback destination | 127.0.0.1 | This PC |
| Feedback destination port | 9001 | Listen On Port 9001 |
| Feedback update interval | 100 ms / 10 Hz | — |
| Full state refresh | Every 2 seconds | — |

For a remote controller, Feedback Host is that controller's IP address; OSC Pilot's
Host is the StageFlow PC's IP. Literal IPv4/IPv6 addresses are supported. Select
matching sockets for the OSC Pilot widgets. Its widgets can receive values on the
same OSC address used for sending, as described in the [official OSC Pilot
manual](https://oscpilot.com/pages/docs).

The receiver is bounded and does not broadcast discovery. No firewall setting is
changed. Input and feedback on the same computer should use different ports.
Reconnect OSC recreates both routes. The status shows receive/send counts or errors.
UDP has no receipt acknowledgement; Send current state and periodic refresh recover
controller state after a controller restart or lost packet.

## Create and edit mappings

Open Preferences → OSC mappings. Add mapping creates a unique /transport/play
address (/transport/play/2, etc. when needed). Select a row and edit its properties
below. Remove deletes that mapping; show Undo restores it. On and Feedback can be
toggled directly in the list. Learn address needs the receiver enabled and captures
the next incoming address without executing that learned command.

Choose the Action before customizing its trigger/range. Choosing an action sets
appropriate defaults. Custom addresses survive action changes. Target layer can
be a fixed layer or the current selection. Reset control defaults restores the
trigger, range, input/feedback argument indices and feedback value format while
preserving custom addresses and the selected target.

| Control | Default address | Default input behavior | Returned state |
|---|---|---|---|
| Play | /transport/play | Press (positive value) | 1 playing, otherwise 0 |
| Pause | /transport/pause | Press | 1 paused, otherwise 0 |
| Stop | /transport/stop | Press | 1 stopped, otherwise 0 |
| Toggle play | /transport/toggle | RisingEdge | 1 playing, otherwise 0 |
| Seek normalized | /transport/position | Value, range 0–1 | Current normalized position |
| Seek seconds | /transport/seconds | Value, range 0–60 | Current seconds normalized to that range |
| Local/program volume | /audio/local/volume, /audio/program/volume | Value, range 0–1 | Gain normalized to the range |
| Local/program mute | /audio/local/mute, /audio/program/mute | EveryMessage | 1 muted, 0 unmuted |
| Layer opacity | /layer/opacity | Value, range 0–1 | Selected/target opacity |
| Layer visibility | /layer/visible | EveryMessage | 1 visible, 0 hidden/missing |
| Start/stop/toggle stream | /stream/start, /stream/stop, /stream/toggle | Press / Press / RisingEdge | Start/toggle: 1 active; stop: 1 inactive |
| Start/stop diagnostics | /diagnostics/start, /diagnostics/stop | Press | Start: 1 recording; stop: 1 inactive |
| Next/previous marker | /markers/next, /markers/previous | Press | 0 (momentary control) |

Press ignores zero release packets. RisingEdge waits for a release before another
press. EveryMessage is needed for on/off setters to accept zero. Value maps 0–1
input to Minimum/Maximum and performs the inverse for feedback; reversed ranges
work, equal endpoints are rejected. Choose EveryMessage for raw seconds/gain values
instead of normalized faders. Incoming and outgoing argument indices start at zero.

The default feedback address is blank, which means the command address. A custom
address can drive a separate indicator. A global feedback switch and each mapping's
Feedback Enabled must both be on; disabled mappings send nothing. Float32 sends
button state as 0.0/1.0; Int32 and OSC Boolean are available for other receivers.
Feedback Argument inserts float-zero padding before the selected outgoing argument.
The encoder follows [OSC wire encoding](https://opensoundcontrol.stanford.edu/spec-1_0.html).

Example: keep /project/record as your button address and map it to Play. With
feedback enabled and that widget listening on port 9001, local playback changes
send 1/0 back to /project/record. Actual state is reported even when a command cannot
change playback (for example while following incoming MTC). If a controller echoes
received messages as commands, use a distinct feedback address for its indicator.

## Implementation and verification

The UI submits an immutable state snapshot at the configured cadence. A worker
serializes/deduplicates and sends it; its mailbox holds only the newest snapshot.
Periodic full refresh bounds recovery time for dropped UDP updates. No feedback
network call runs on the render or MIDI scheduling thread.

Tests cover local Play/Pause/Stop/seek state, volume/mute/layer/stream/recording state,
normalized and reversed ranges, disabled mappings, explicit addresses, padded
arguments, wire types, UDP send/cancel, show persistence, defaults, add/remove,
undo/redo, Preferences reopening and actual local-edit-to-UDP integration. The live
OSC Pilot UI itself has not been driven by automated tests.
