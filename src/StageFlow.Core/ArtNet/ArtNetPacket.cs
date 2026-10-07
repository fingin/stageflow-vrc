using System.Buffers.Binary;

namespace StageFlow.Core.ArtNet;

public readonly ref struct ArtDmx(ushort universe, byte sequence, byte physical, ReadOnlySpan<byte> channels)
{
    public ushort Universe { get; } = universe;
    public byte Sequence { get; } = sequence;
    public byte Physical { get; } = physical;
    public ReadOnlySpan<byte> Channels { get; } = channels;
}

public static class ArtNetPacket
{
    public const int DefaultPort = 6454;
    public const ushort OpDmx = 0x5000, OpPoll = 0x2000, OpPollReply = 0x2100, OpSync = 0x5200;
    public static bool TryGetOpcode(ReadOnlySpan<byte> packet, out ushort opcode)
    {
        opcode = 0;
        if (packet.Length < 10 || !packet[..8].SequenceEqual("Art-Net\0"u8)) return false;
        opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet[8..]);
        return true;
    }
    public static bool TryReadDmx(ReadOnlySpan<byte> packet, out ArtDmx dmx)
    {
        dmx = default;
        if (!TryGetOpcode(packet, out ushort opcode) || opcode != OpDmx || packet.Length < 18 ||
            BinaryPrimitives.ReadUInt16BigEndian(packet[10..]) < 14) return false;
        ushort universe = BinaryPrimitives.ReadUInt16LittleEndian(packet[14..]);
        int length = BinaryPrimitives.ReadUInt16BigEndian(packet[16..]);
        if (universe > 32767 || length < 2 || length > 512 || (length & 1) != 0 || packet.Length < 18 + length) return false;
        dmx = new(universe, packet[12], packet[13], packet.Slice(18, length));
        return true;
    }
    public static int WriteDmx(Span<byte> destination, ushort universe, byte sequence, ReadOnlySpan<byte> channels)
    {
        if (universe > 32767 || channels.Length is < 2 or > 512 || (channels.Length & 1) != 0)
            throw new ArgumentException("ArtDmx requires 2..512 even channel bytes and a 15-bit universe.");
        int length = 18 + channels.Length;
        if (destination.Length < length) throw new ArgumentException("Packet buffer too short.");
        "Art-Net\0"u8.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], OpDmx);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..], 14);
        destination[12] = sequence; destination[13] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[14..], universe);
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..], (ushort)channels.Length);
        channels.CopyTo(destination[18..]);
        return length;
    }
}
