using StageFlow.Core.Grid;
using StageFlow.Core.Timing;

namespace StageFlow.Core.Projects;

public sealed record ShowProject
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public string Name { get; init; } = "Untitled show";
    public string? MediaPath { get; init; }
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int FpsNumerator { get; init; } = 60;
    public int FpsDenominator { get; init; } = 1;
    public string? GpuAdapter { get; init; }
    public ClockSettings Clock { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public ArtNetSettings ArtNet { get; init; } = new();
    public StreamSettings Stream { get; init; } = new();
    public MidiDmxSettings MidiDmx { get; init; } = new();
    public List<Layer> Layers { get; init; } = [];
    public List<Marker> Markers { get; init; } = [];
    public long LoopInTicks { get; init; }
    public long LoopOutTicks { get; init; }
    public bool LoopEnabled { get; init; }
    public string? ImportedHnodeConfiguration { get; init; }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema) throw new InvalidDataException($"Unsupported project schema {SchemaVersion}.");
        if (Width is < 16 or > 8192 || Height is < 16 or > 8192 || (Width & 1) != 0 || (Height & 1) != 0)
            throw new InvalidDataException("Canvas must have even dimensions between 16 and 8192.");
        if (FpsNumerator is < 1 or > 120000 || FpsDenominator is < 1 or > 1001 ||
            (double)FpsNumerator / FpsDenominator is < 1 or > 240) throw new InvalidDataException("Invalid output frame rate.");
        if (!Enum.IsDefined(Clock.Mode) || !Enum.IsDefined(Clock.Rate)) throw new InvalidDataException("Invalid timecode settings.");
        if (ArtNet.Port is < 1 or > 65535 || !System.Net.IPAddress.TryParse(ArtNet.BindAddress, out _))
            throw new InvalidDataException("Invalid Art-Net bind address or port.");
        if (ArtNet.FirstUniverse < 0 || ArtNet.UniverseCount is < 1 or > 512 || ArtNet.FirstUniverse + ArtNet.UniverseCount > 32768)
            throw new InvalidDataException("Invalid Art-Net universe range.");
        if (Stream.Port is < 1 or > 65535 || Stream.VideoBitrateKbps is < 500 or > 200000 ||
            Stream.SegmentSeconds is < 0.5 or > 10) throw new InvalidDataException("Invalid streaming settings.");
        if (Layers.Count > 10000 || Markers.Count > 100000) throw new InvalidDataException("Project exceeds supported data size.");
        if (Layers.Select(l => l.Id).Distinct().Count() != Layers.Count) throw new InvalidDataException("Duplicate layer IDs.");
        foreach (var layer in Layers)
        {
            if (!Enum.IsDefined(layer.Kind) || !double.IsFinite(layer.X) || !double.IsFinite(layer.Y) ||
                !double.IsFinite(layer.Width) || !double.IsFinite(layer.Height) || layer.Width <= 0 || layer.Height <= 0 ||
                !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
                throw new InvalidDataException("Invalid layer transform.");
            layer.Grid?.Validate();
        }
    }
}
public sealed record ClockSettings
{
    public ClockMode Mode { get; init; }
    public MtcRate Rate { get; init; } = MtcRate.Fps30;
    public long OffsetFrames { get; init; }
    public string? MidiInput { get; init; }
    public string? MidiOutput { get; init; }
    public bool OutputEnabled { get; init; }
    public int OutputOffsetMilliseconds { get; init; }
    public bool SendLocateDuringScrub { get; init; }
}
public sealed record AudioSettings
{
    public string? DeviceId { get; init; }
    public float LocalVolume { get; init; } = 1;
    public float StreamVolume { get; init; } = 1;
    public bool LocalMute { get; init; }
    public bool StreamMute { get; init; }
    public int OffsetMilliseconds { get; init; }
}
public sealed record ArtNetSettings
{
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 6454;
    public bool Enabled { get; init; }
    public int FirstUniverse { get; init; }
    public int UniverseCount { get; init; } = 48;
    public bool AutomaticUniverseCount { get; init; } = true;
    public int HoldLastSeconds { get; init; } = 10;
}
public sealed record MidiDmxSettings
{
    public bool Enabled { get; init; }
    public string? OutputDevice { get; init; }
    public int ChannelsPerUpdate { get; init; } = 100;
    public int IdleScanChannels { get; init; } = 10;
}
public sealed record StreamSettings
{
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8888;
    public string Encoder { get; init; } = "h264_nvenc";
    public int VideoBitrateKbps { get; init; } = 12000;
    public int AudioBitrateKbps { get; init; } = 192;
    public double SegmentSeconds { get; init; } = 1;
    public int PlaylistSegments { get; init; } = 6;
}
public enum LayerKind { Video, Grid, Timecode, Spout }
public sealed record Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Layer";
    public LayerKind Kind { get; init; }
    public bool Visible { get; init; } = true;
    public bool Locked { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 1920;
    public double Height { get; init; } = 1080;
    public double Opacity { get; init; } = 1;
    public int CropLeft { get; init; }
    public int CropTop { get; init; }
    public int CropRight { get; init; }
    public int CropBottom { get; init; }
    public string FontFamily { get; init; } = "Consolas";
    public int FontSize { get; init; } = 48;
    public string TextColor { get; init; } = "#FFFFFFFF";
    public string BackgroundColor { get; init; } = "#AA000000";
    public string Label { get; init; } = "";
    public string? SpoutSender { get; init; }
    public GridLayout? Grid { get; init; }
}
public sealed record Marker
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Marker";
    public long PositionTicks { get; init; }
    public string Color { get; init; } = "#FF70D6BB";
}
