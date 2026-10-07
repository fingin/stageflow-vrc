namespace StageFlow.Core.Grid;

public enum GridEncoding { Vrsl, BinaryStageFlight, Binary, Ternary }
public enum GridOrientation { HorizontalTop, VerticalLeft, VerticalRight, HorizontalBottom }
public readonly record struct ChannelRange(int Start, int End)
{
    public bool Contains(int channel) => channel >= Start && channel <= End;
}
public sealed record GridLayout
{
    public GridEncoding Encoding { get; init; } = GridEncoding.Vrsl;
    public GridOrientation Orientation { get; init; } = GridOrientation.HorizontalTop;
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FirstUniverse { get; init; }
    public int UniverseCount { get; init; } = 3;
    public bool GammaCorrection { get; init; } = true;
    public bool RgbPacking { get; init; }
    public bool MaskZero { get; init; }
    public bool InvertMask { get; init; }
    public List<ChannelRange> Masks { get; init; } = [];
    public bool IsMasked(int channel)
    {
        bool match = Masks.Any(range => range.Contains(channel));
        return InvertMask ? !match : match;
    }
    public void Validate()
    {
        if (Width is < 16 or > 8192 || Height is < 16 or > 8192)
            throw new ArgumentException("Grid dimensions must be 16..8192 pixels.");
        if (FirstUniverse < 0 || UniverseCount is < 1 or > 512 || FirstUniverse + UniverseCount > 32768)
            throw new ArgumentException("Invalid grid universe range.");
        if (!Enum.IsDefined(Encoding) || !Enum.IsDefined(Orientation)) throw new ArgumentException("Unknown grid format.");
        if (Masks.Any(x => x.Start < 0 || x.End < x.Start)) throw new ArgumentException("Invalid channel mask.");
    }
}

/// <summary>Top-left pixel rectangle in a data layer. Color values are unpremultiplied RGBA.</summary>
public readonly record struct GridCell(int X, int Y, int Size, byte R, byte G, byte B, byte A, int Channel);
