using System.Buffers.Binary;
using System.Text;

namespace StageFlow.Core.Osc;

public sealed record OscMessage(string Address, object?[] Arguments, DateTimeOffset? Due = null);
public static class OscCodec
{
    private static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static bool TryDecode(ReadOnlySpan<byte> data, out IReadOnlyList<OscMessage> messages)
    {
        var result = new List<OscMessage>();
        try { Decode(data, result, 0, null); messages = result; return true; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        { messages = []; return false; }
    }
    private static void Decode(ReadOnlySpan<byte> data, List<OscMessage> result, int depth, DateTimeOffset? due)
    {
        if (depth > 8 || data.Length is < 4 or > 65504 || data.Length % 4 != 0 || result.Count >= 256) throw new InvalidDataException();
        int offset = 0; string address = ReadString(data, ref offset);
        if (address == "#bundle")
        {
            Need(data, offset, 8); ulong tag = BinaryPrimitives.ReadUInt64BigEndian(data[offset..]); offset += 8;
            if (tag != 1)
            {
                var time = Epoch.AddSeconds(tag >> 32).AddTicks((long)((tag & uint.MaxValue) * (double)TimeSpan.TicksPerSecond / 4294967296.0));
                if (due == null || time > due) due = time;
            }
            while (offset < data.Length)
            {
                int size = ReadInt(data, ref offset); Need(data, offset, size);
                Decode(data.Slice(offset, size), result, depth + 1, due); offset += size;
            }
            return;
        }
        if (!OscSettings.ValidAddress(address)) throw new InvalidDataException();
        if (offset == data.Length) { result.Add(new(address, [], due)); return; }
        string tags = ReadString(data, ref offset);
        if (!tags.StartsWith(',') || tags.Length > 65) throw new InvalidDataException();
        object?[] args = new object?[tags.Length - 1];
        for (int i = 1; i < tags.Length; i++)
        {
            args[i - 1] = tags[i] switch
            {
                'i' => ReadInt(data, ref offset),
                'f' => BitConverter.Int32BitsToSingle(ReadInt(data, ref offset)),
                'h' => ReadLong(data, ref offset),
                'd' => BitConverter.Int64BitsToDouble(ReadLong(data, ref offset)),
                's' => ReadString(data, ref offset),
                'b' => ReadBlob(data, ref offset),
                'T' => true, 'F' => false, 'N' => null,
                _ => throw new InvalidDataException()
            };
        }
        if (offset != data.Length) throw new InvalidDataException();
        result.Add(new(address, args, due));
    }
    private static void Need(ReadOnlySpan<byte> data, int offset, int size)
    { if (size < 0 || offset > data.Length - size) throw new InvalidDataException(); }
    private static int ReadInt(ReadOnlySpan<byte> data, ref int offset)
    { Need(data, offset, 4); int value = BinaryPrimitives.ReadInt32BigEndian(data[offset..]); offset += 4; return value; }
    private static long ReadLong(ReadOnlySpan<byte> data, ref int offset)
    { Need(data, offset, 8); long value = BinaryPrimitives.ReadInt64BigEndian(data[offset..]); offset += 8; return value; }
    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        int length = data[offset..].IndexOf((byte)0);
        if (length < 0 || length > 4096) throw new InvalidDataException();
        int padded = (length + 4) & ~3; Need(data, offset, padded);
        string value = Encoding.UTF8.GetString(data.Slice(offset, length));
        for (int i = length; i < padded; i++) if (data[offset + i] != 0) throw new InvalidDataException();
        offset += padded; return value;
    }
    private static byte[] ReadBlob(ReadOnlySpan<byte> data, ref int offset)
    {
        int size = ReadInt(data, ref offset); Need(data, offset, size);
        byte[] bytes = data.Slice(offset, size).ToArray(); int padded = checked(size + 3) & ~3;
        Need(data, offset, padded); offset += padded; return bytes;
    }
    public static byte[] Encode(string address, params object[] args)
    {
        if (!OscSettings.ValidAddress(address) || args.Length > 64) throw new ArgumentException("Invalid OSC message.");
        using var output = new MemoryStream();
        static void Text(Stream stream, string text)
        { byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes); for (int i = 0; i < 4 - bytes.Length % 4; i++) stream.WriteByte(0); }
        Text(output, address);
        Text(output, "," + string.Concat(args.Select(a => a switch { int => 'i', float => 'f', double => 'd', long => 'h', bool b => b ? 'T' : 'F', string => 's', _ => throw new ArgumentException("Unsupported OSC argument.") })));
        Span<byte> number = stackalloc byte[8];
        foreach (var arg in args)
        {
            switch (arg)
            {
                case int i: BinaryPrimitives.WriteInt32BigEndian(number, i); output.Write(number[..4]); break;
                case float f: BinaryPrimitives.WriteInt32BigEndian(number, BitConverter.SingleToInt32Bits(f)); output.Write(number[..4]); break;
                case long l: BinaryPrimitives.WriteInt64BigEndian(number, l); output.Write(number); break;
                case double d: BinaryPrimitives.WriteInt64BigEndian(number, BitConverter.DoubleToInt64Bits(d)); output.Write(number); break;
                case string s: Text(output, s); break;
            }
        }
        return output.ToArray();
    }
}
