// Layouts adapted from HNode (MIT), copyright 2025 Matthew Herber.
// See licenses/HNode-LICENSE.txt and docs/UPSTREAM.md.
namespace StageFlow.Core.Grid;

/// <summary>
/// Converts small channel buffers to GPU instance data. Never expands to a full video bitmap.
/// Callers own/reuse the output buffer. The same cells drive CPU reference tests and GPU draws.
/// </summary>
public sealed class GridEncoder
{
    private static readonly byte[] Linear = Enumerable.Range(0, 256).Select(value =>
    {
        double s = value / 255.0;
        double linear = s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        return (byte)Math.Clamp((int)Math.Round(linear * 255), 0, 255);
    }).ToArray();

    public static int RequiredCellCapacity(GridLayout layout) => checked(layout.UniverseCount * 512 *
        (layout.Encoding == GridEncoding.Ternary ? 6 : layout.Encoding == GridEncoding.Vrsl ? 1 : 9));

    public int Encode(GridLayout layout, ReadOnlySpan<byte> channels, Span<GridCell> cells)
    {
        layout.Validate();
        if (cells.Length < RequiredCellCapacity(layout)) throw new ArgumentException("Cell buffer too small.");
        int count = Math.Min(channels.Length, layout.UniverseCount * 512), written = 0;
        for (int channel = 0; channel < count; channel++)
        {
            if (layout.IsMasked(channel) && !(layout.Encoding == GridEncoding.Vrsl && layout.RgbPacking)) continue;
            byte value = channels[channel], alpha = (byte)(layout.MaskZero && value == 0 ? 0 : 255);
            if (layout.Encoding == GridEncoding.Vrsl)
            {
                // RGB packing overlays each set of 3 universes into a separate component.
                if (layout.RgbPacking && channel / 512 / 3 > 2) continue;
                (int x, int y) = VrslPosition(layout, channel);
                byte intensity = layout.IsMasked(channel) ? (byte)0 : layout.GammaCorrection ? Linear[value] : value;
                if (layout.RgbPacking && channel >= 1536) continue;
                cells[written++] = new(x, y, 16,
                    intensity,
                    layout.RgbPacking ? Component(layout, channels, channel + 1536) : intensity,
                    layout.RgbPacking ? Component(layout, channels, channel + 3072) : intensity,
                    layout.RgbPacking ? (byte)255 : alpha, channel);
            }
            else if (layout.Encoding == GridEncoding.Ternary)
            {
                int divisor = 243;
                for (int digit = 0; digit < 6; digit++, divisor /= 3)
                {
                    int index = channel * 6 + digit;
                    byte intensity = (byte)((value / divisor % 3) * 127.5);
                    cells[written++] = new(index / 48 * 4, index % 48 * 4, 4,
                        intensity, intensity, intensity, alpha, channel);
                }
            }
            else
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    int index = channel * 8 + bit;
                    int rows = layout.Encoding == GridEncoding.BinaryStageFlight ? 48 : 52;
                    int x = index / rows * 4, y = index % rows * 4;
                    if (layout.Encoding == GridEncoding.BinaryStageFlight)
                    { y += x / layout.Width * 208; x %= layout.Width; }
                    int bitIndex = layout.Encoding == GridEncoding.BinaryStageFlight ? 7 - bit : bit;
                    byte intensity = (byte)((value & (1 << bitIndex)) != 0 ? 255 : 0);
                    cells[written++] = new(x, y, 4, intensity, intensity, intensity, alpha, channel);
                }
            }
        }
        if (layout.Encoding == GridEncoding.BinaryStageFlight)
        {
            Span<byte> group = stackalloc byte[6];
            for (int i = 0; i < count; i += 6)
            {
                group.Clear(); channels.Slice(i, Math.Min(6, count - i)).CopyTo(group);
                byte crc = Crc4(group);
                for (int bit = 0; bit < 4; bit++)
                {
                    int x = i / 6 * 4, y = 192 + bit * 4 + (x / layout.Width) * 208;
                    x %= layout.Width;
                    byte v = (byte)((crc & (1 << (7 - bit))) != 0 ? 255 : 0);
                    cells[written++] = new(x, y, 4, v, v, v, 255, -1);
                }
            }
        }
        return written;
    }

    public static (int X, int Y) VrslPosition(GridLayout layout, int channel)
    {
        int universe = channel / 512, within = channel % 512;
        if (layout.RgbPacking) universe %= 3;
        int x = within / 13 * 16 + universe * 640, y = within % 13 * 16;
        return layout.Orientation switch
        {
            GridOrientation.HorizontalTop => (x, y),
            GridOrientation.HorizontalBottom => (x, y + layout.Height - 208),
            GridOrientation.VerticalLeft => (y, layout.Height - x - 16),
            GridOrientation.VerticalRight => (y + layout.Width - 208, layout.Height - x - 16),
            _ => throw new ArgumentException("Invalid grid orientation.")
        };
    }
    public static byte Crc4(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (byte value in data)
            for (int bit = 7; bit >= 0; bit--)
            {
                bool top = (crc & 8) != 0;
                crc = ((crc << 1) | ((uint)(value >> bit) & 1)) & 15;
                if (top) crc ^= 3;
            }
        return (byte)(crc << 4);
    }
    private static byte Component(GridLayout layout, ReadOnlySpan<byte> channels, int index)
    {
        if (index >= channels.Length || index >= layout.UniverseCount * 512 || layout.IsMasked(index)) return 0;
        return layout.GammaCorrection ? Linear[channels[index]] : channels[index];
    }
}
