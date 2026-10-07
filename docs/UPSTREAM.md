# Upstream references and compatibility

Hnode: https://github.com/Happyrobot33/HNode
Inspected revision: d4c4f2d4a9204a4b5b193d426028725d4b0727f7
License: MIT; included in licenses/HNode-LICENSE.txt.

GridEncoder adapts the VRSL, MDMX/BinaryStageFlight, Binary and Ternary layouts
from Hnode's Assets/Plugin/Serializers. Grid cells use top-left image coordinates;
Hnode's PixelToIndex flips into Unity's bottom-left texture array. The resulting
visible pixel coordinates must match, not the intermediate array indexing.
BinaryStageFlight uses 4x4 binary cells, six bytes per column, four CRC bits and
208-pixel vertical bands. CRC polynomial is 0x03 with the resulting nibble stored
in the upper four bits. A partial six-byte group is zero padded to avoid the
upstream GetRange exception when the received channel count is not divisible by 6.

User reference (read-only): C:/Users/Night/Documents/Sample Show.shwcfg.
Its actual active serializer is BinaryStageFlight; VRSL is the disabled
transcoding input. Import must never confuse those two fields. Its output is
2560x1440 at 60 fps; Art-Net bind is 127.0.0.1:6454. MIDI-DMX uses loopMIDI Port
with 100 updates and 10 idle-scan channels. Unlimited serialization means the
currently received universe range, bounded by the output layout, not billions
of preallocated universes.
