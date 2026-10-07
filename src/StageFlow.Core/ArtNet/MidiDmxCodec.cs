namespace StageFlow.Core.ArtNet;

/// <summary>VRC-MIDIDMX / Hnode banked 18-bit note protocol; independent of MTC.</summary>
public static class MidiDmxCodec
{
    public const int MaxChannels = 16384;
    public static uint Control(int value)
    {
        if ((uint)value > 127) throw new ArgumentOutOfRangeException(nameof(value));
        return 0xbfU | (127U << 8) | ((uint)value << 16);
    }
    public static uint Channel(int channel, byte value)
    {
        if ((uint)channel >= MaxChannels) throw new ArgumentOutOfRangeException(nameof(channel));
        int local = channel % 2048, index = local % 1024;
        return (uint)((local < 1024 ? 0x90 : 0x80) | ((index >> 6) & 15)) |
            (uint)(((index << 1) & 127) | (value >> 7)) << 8 | (uint)(value & 127) << 16;
    }
}

/// <summary>Changed channels plus rolling refresh, strictly bounded by the receiver's message budget.</summary>
public sealed class MidiDmxScheduler
{
    private readonly short[] _previous = new short[MidiDmxCodec.MaxChannels];
    private int _cursor, _scan, _bank = -1;
    public MidiDmxScheduler() => Reset();
    public void Reset() { Array.Fill(_previous, (short)-1); _cursor = _scan = 0; _bank = -1; }
    public int Build(ReadOnlySpan<byte> channels, Span<uint> messages, int budget, int idleScan)
    {
        if (budget < 3 || messages.Length < budget) throw new ArgumentException("MIDIDMX requires at least 3 message slots.");
        int length = Math.Min(channels.Length, MidiDmxCodec.MaxChannels), written = 0;
        if (length == 0) return 0;
        _cursor %= length; _scan %= length;
        for (int visited = 0; visited < length; visited++)
        {
            int index = _cursor;
            bool refresh = (index - _scan + length) % length < idleScan;
            if (_previous[index] != channels[index] || refresh)
            {
                int bank = index / 2048, required = bank != _bank ? 2 : 1;
                if (written + required >= budget) break; // Reserve one message for watchdog.
                if (bank != _bank) { messages[written++] = MidiDmxCodec.Control(bank); _bank = bank; }
                messages[written++] = MidiDmxCodec.Channel(index, channels[index]); _previous[index] = channels[index];
            }
            _cursor = (_cursor + 1) % length;
        }
        _scan = (_scan + Math.Max(0, idleScan)) % length;
        messages[written++] = MidiDmxCodec.Control(127);
        return written;
    }
}
