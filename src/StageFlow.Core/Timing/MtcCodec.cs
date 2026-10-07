namespace StageFlow.Core.Timing;

public static class MtcCodec
{
    public static byte QuarterFrame(Timecode value, int piece)
    {
        if (!value.IsValid) throw new ArgumentException("Invalid timecode.", nameof(value));
        int data = piece switch
        {
            0 => value.Frames & 15, 1 => value.Frames >> 4,
            2 => value.Seconds & 15, 3 => value.Seconds >> 4,
            4 => value.Minutes & 15, 5 => value.Minutes >> 4,
            6 => value.Hours & 15, 7 => (value.Hours >> 4) | ((int)value.Rate << 1),
            _ => throw new ArgumentOutOfRangeException(nameof(piece))
        };
        return (byte)((piece << 4) | data);
    }

    public static void WriteFullFrame(Timecode value, Span<byte> destination, byte device = 0x7f)
    {
        if (!value.IsValid || device > 127) throw new ArgumentException("Invalid timecode or device.");
        if (destination.Length < 10) throw new ArgumentException("Ten bytes required.", nameof(destination));
        destination[0] = 0xf0; destination[1] = 0x7f; destination[2] = device;
        destination[3] = 1; destination[4] = 1;
        destination[5] = (byte)(value.Hours | ((int)value.Rate << 5));
        destination[6] = (byte)value.Minutes; destination[7] = (byte)value.Seconds;
        destination[8] = (byte)value.Frames; destination[9] = 0xf7;
    }

    public static bool TryReadFullFrame(ReadOnlySpan<byte> message, out Timecode value)
    {
        value = default;
        if (message.Length != 10 || message[0] != 0xf0 || message[1] != 0x7f || message[2] > 127 ||
            message[3] != 1 || message[4] != 1 || message[5] > 127 || message[9] != 0xf7) return false;
        value = new(message[5] & 31, message[6], message[7], message[8], (MtcRate)((message[5] >> 5) & 3));
        return value.IsValid;
    }
}

/// <summary>Rejects partial/out-of-order cycles; applies the MTC two-frame receiver compensation.</summary>
public sealed class MtcQuarterFrameDecoder
{
    private readonly byte[] _parts = new byte[8];
    private int _last = -1, _count, _direction;
    public int Direction => _direction;
    public void Reset() { _last = -1; _count = 0; _direction = 0; }

    public bool Push(byte data, out Timecode value)
    {
        value = default;
        if (data > 127) { Reset(); return false; }
        int piece = data >> 4;
        if (_count == 0)
        {
            if (piece != 0 && piece != 7) return false;
            _direction = piece == 0 ? 1 : -1;
        }
        else if (piece != _last + _direction)
        {
            Reset();
            if (piece != 0 && piece != 7) return false;
            _direction = piece == 0 ? 1 : -1;
        }
        _parts[piece] = (byte)(data & 15);
        _last = piece;
        if (++_count != 8) return false;
        _count = 0; _last = -1;
        if (_parts[1] > 1 || _parts[3] > 3 || _parts[5] > 3 || _parts[7] > 7) return false;
        value = new(_parts[6] | ((_parts[7] & 1) << 4), _parts[4] | (_parts[5] << 4),
            _parts[2] | (_parts[3] << 4), _parts[0] | (_parts[1] << 4), (MtcRate)(_parts[7] >> 1));
        if (!value.IsValid) return false;
        value = value.AddFrames(2 * _direction);
        return true;
    }
}
