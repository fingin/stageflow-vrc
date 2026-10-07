using StageFlow.Core.Grid;
using StageFlow.Core.Timing;

namespace StageFlow.Core.Projects;

public sealed record ShowProject
{
    public const int CurrentSchema = 2;
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
    public SpoutSettings Spout { get; init; } = new();
    public StageFlow.Core.Osc.OscSettings Osc { get; init; } = new();
    public List<NetworkStreamOutput> NetworkOutputs { get; init; } = [];
    public LanVideoSettings LanVideo { get; init; } = new();
    public List<Layer> Layers { get; init; } = [];
    public List<Marker> Markers { get; init; } = [];
    public long LoopInTicks { get; init; }
    public long LoopOutTicks { get; init; }
    public bool LoopEnabled { get; init; }
    public string? ImportedHnodeConfiguration { get; init; }

    public void Validate()
    {
        if (Clock == null || Audio == null || ArtNet == null || Stream == null || MidiDmx == null || Spout == null || Osc == null || Layers == null || Markers == null)
            throw new InvalidDataException("Project is missing required settings.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 256) throw new InvalidDataException("Show name requires 1–256 characters.");
        if (SchemaVersion != CurrentSchema) throw new InvalidDataException($"Unsupported project schema {SchemaVersion}.");
        Osc.Validate();
        if (NetworkOutputs == null || NetworkOutputs.Count > 16 || LanVideo == null) throw new InvalidDataException("Invalid network outputs.");
        foreach (var output in NetworkOutputs) { if (output == null) throw new InvalidDataException("Missing output."); output.Validate(); }
        if (NetworkOutputs.Select(o => o.Id).Distinct().Count() != NetworkOutputs.Count) throw new InvalidDataException("Duplicate output IDs.");
        LanVideo.Validate();
        if (NetworkOutputs.Any(o => o.Enabled && o.Protocol == NetworkStreamProtocol.Rtmp) && Stream.Encoder == "hevc_nvenc")
            throw new InvalidDataException("RTMP output uses H.264. Select h264_nvenc or libx264.");
        if (Width is < 16 or > 8192 || Height is < 16 or > 8192 || (Width & 1) != 0 || (Height & 1) != 0)
            throw new InvalidDataException("Canvas must have even dimensions between 16 and 8192.");
        if (FpsNumerator is < 1 or > 120000 || FpsDenominator is < 1 or > 1001 ||
            (double)FpsNumerator / FpsDenominator is < 1 or > 240) throw new InvalidDataException("Invalid output frame rate.");
        if (!Enum.IsDefined(Clock.Mode) || !Enum.IsDefined(Clock.Rate)) throw new InvalidDataException("Invalid timecode settings.");
        if (ArtNet.Port is < 1 or > 65535 || !System.Net.IPAddress.TryParse(ArtNet.BindAddress, out var bind) || bind.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new InvalidDataException("Invalid Art-Net bind address or port.");
        if (ArtNet.FirstUniverse < 0 || ArtNet.UniverseCount is < 1 or > 512 || ArtNet.FirstUniverse + ArtNet.UniverseCount > 32768)
            throw new InvalidDataException("Invalid Art-Net universe range.");
        Stream.Validate();
        if (!float.IsFinite(Audio.LocalVolume) || !float.IsFinite(Audio.StreamVolume) || Audio.LocalVolume is < 0 or > 2 || Audio.StreamVolume is < 0 or > 2 ||
            Audio.OffsetMilliseconds is < -5000 or > 5000 || Clock.OutputOffsetMilliseconds is < -5000 or > 5000 || Math.Abs((double)Clock.OffsetFrames) > 2592000)
            throw new InvalidDataException("Invalid audio gain or timing offset.");
        if (MidiDmx.ChannelsPerUpdate is < 3 or > 1000 || MidiDmx.IdleScanChannels is < 0 or > 1000)
            throw new InvalidDataException("MIDI-DMX message budget must be 3–1000 and idle scan 0–1000.");
        if (string.IsNullOrWhiteSpace(Spout.OutputName) || Spout.OutputName.Length > 200 || Spout.OutputName.Any(c => c < 32 || c > 126 || c == '\\'))
            throw new InvalidDataException("Use a Spout name of 1–200 plain characters without backslashes.");
        if (LoopInTicks < 0 || LoopOutTicks < 0 || (LoopEnabled && LoopOutTicks <= LoopInTicks)) throw new InvalidDataException("Invalid loop range.");
        if (Layers.Count > 10000 || Markers.Count > 100000) throw new InvalidDataException("Project exceeds supported data size.");
        if (Layers.Any(l => l == null)) throw new InvalidDataException("Missing layer.");
        if (Layers.Select(l => l.Id).Distinct().Count() != Layers.Count) throw new InvalidDataException("Duplicate layer IDs.");
        foreach (var layer in Layers)
        {
            if (layer == null) throw new InvalidDataException("Missing layer.");
            if (!Enum.IsDefined(layer.Kind) || !double.IsFinite(layer.X) || !double.IsFinite(layer.Y) ||
                !double.IsFinite(layer.Width) || !double.IsFinite(layer.Height) || layer.Width <= 0 || layer.Height <= 0 ||
                !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
                throw new InvalidDataException("Invalid layer transform.");
            if (Math.Abs(layer.X) > 65536 || Math.Abs(layer.Y) > 65536 || layer.Width > 65536 || layer.Height > 65536 ||
                layer.CropLeft is < 0 or > 16384 || layer.CropTop is < 0 or > 16384 || layer.CropRight is < 0 or > 16384 || layer.CropBottom is < 0 or > 16384)
                throw new InvalidDataException("Layer bounds or crop are outside the supported range.");
            if (layer.FontSize is < 8 or > 256 || string.IsNullOrWhiteSpace(layer.FontFamily) || layer.FontFamily.Length > 128 ||
                layer.Label == null || layer.Label.Length > 256 || layer.Name == null || layer.Name.Length > 256 || !ValidColor(layer.TextColor) || !ValidColor(layer.BackgroundColor))
                throw new InvalidDataException("Invalid text style. Colors use #RRGGBB or #AARRGGBB; labels allow 256 characters.");
            if (layer.Kind == LayerKind.Grid && layer.Grid == null) throw new InvalidDataException("Grid layer requires a layout.");
            if (layer.Kind == LayerKind.Spout && (string.IsNullOrWhiteSpace(layer.SpoutSender) || layer.SpoutSender.Length > 255 || layer.SpoutSender.Contains('\\')))
                throw new InvalidDataException("Select a valid Spout sender.");
            if (!Enum.IsDefined(layer.NetworkProtocol) || !float.IsFinite(layer.LiveAudioVolume) || layer.LiveAudioVolume is < 0 or > 2 ||
                layer.Kind == LayerKind.Network && (string.IsNullOrWhiteSpace(layer.NetworkSource) || layer.NetworkSource.Length > 4096))
                throw new InvalidDataException("Network layers require a source name or URL.");
            if (layer.Kind == LayerKind.Network && layer.NetworkProtocol == NetworkSourceProtocol.Srt && (!Uri.TryCreate(layer.NetworkSource, UriKind.Absolute, out var source) || source.Scheme != "srt"))
                throw new InvalidDataException("SRT inputs require an srt:// URL.");
            layer.Grid?.Validate();
        }
        foreach (var marker in Markers)
            if (marker == null || marker.PositionTicks < 0 || marker.Name == null || marker.Name.Length > 256 || !ValidColor(marker.Color))
                throw new InvalidDataException("Invalid marker.");
    }
    private static bool ValidColor(string? color) => color != null && (color.Length == 7 || color.Length == 9) && color[0] == '#' &&
        uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _);
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
    public bool UseEditorLog { get; init; }
    public string? LogPath { get; init; }
}
public sealed record SpoutSettings
{
    public bool OutputEnabled { get; init; }
    public string OutputName { get; init; } = "StageFlow Program";
}
public sealed record StreamSettings
{
    public bool HlsEnabled { get; init; } = true;
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 8888;
    public string Encoder { get; init; } = "h264_nvenc";
    public int VideoBitrateKbps { get; init; } = 12000;
    public int AudioBitrateKbps { get; init; } = 192;
    public VideoRateControl RateControl { get; init; } = VideoRateControl.Cbr;
    public int MaxBitrateKbps { get; init; } // 0 = target bitrate
    public double BufferSeconds { get; init; } = 1;
    public int Quality { get; init; } = 23;
    public EncodingPreset Preset { get; init; } = EncodingPreset.Performance;
    public H264Profile Profile { get; init; } = H264Profile.Auto;
    public double KeyframeSeconds { get; init; } // 0 = follow HLS segment duration (legacy behavior)
    public int BFrames { get; init; }
    public double SegmentSeconds { get; init; } = 1;
    public int PlaylistSegments { get; init; } = 6;
    public int GopFrames(int fpsNumerator, int fpsDenominator) => Math.Max(1, (int)Math.Round(
        (double)fpsNumerator / fpsDenominator * (KeyframeSeconds == 0 ? SegmentSeconds : KeyframeSeconds)));
    // Segments end on the next IDR. Account for frame-rate rounding and reordered frames.
    public double HlsTargetSeconds(int fpsNumerator, int fpsDenominator)
    {
        double frame = (double)fpsDenominator / fpsNumerator, gop = GopFrames(fpsNumerator, fpsDenominator) * frame;
        return Math.Max(0.5, Math.Ceiling(SegmentSeconds * 0.8 / gop) * gop + BFrames * frame);
    }
    public void Validate()
    {
        if (Port is < 1 or > 65535 || VideoBitrateKbps is < 500 or > 200000 || AudioBitrateKbps is < 32 or > 512 ||
            !double.IsFinite(SegmentSeconds) || SegmentSeconds is < 0.5 or > 10 || PlaylistSegments is < 3 or > 20 ||
            !System.Net.IPAddress.TryParse(BindAddress, out _) || Encoder is not ("h264_nvenc" or "hevc_nvenc" or "libx264"))
            throw new InvalidDataException("Invalid streaming settings or unsupported encoder.");
        if (!Enum.IsDefined(RateControl) || !Enum.IsDefined(Preset) || !Enum.IsDefined(Profile) || Quality is < 0 or > 51 ||
            MaxBitrateKbps is < 0 or > 200000 || MaxBitrateKbps is > 0 and < 500 ||
            !double.IsFinite(BufferSeconds) || BufferSeconds is < 0.1 or > 10 ||
            !double.IsFinite(KeyframeSeconds) || KeyframeSeconds < 0 || KeyframeSeconds > 10 || KeyframeSeconds is > 0 and < 0.5 || BFrames is < 0 or > 4)
            throw new InvalidDataException("Invalid encoder rate control, quality, buffer, preset, profile, keyframe interval or B-frame count.");
        if (RateControl == VideoRateControl.Vbr && MaxBitrateKbps > 0 && MaxBitrateKbps < VideoBitrateKbps)
            throw new InvalidDataException("VBR maximum bitrate must be at least the target bitrate (or zero to use the target).");
        if (Profile == H264Profile.Baseline && Encoder != "hevc_nvenc" && BFrames != 0)
            throw new InvalidDataException("H.264 Baseline does not support B-frames. Select Main/High or set B-frames to zero.");
        if (Encoder == "libx264" && RateControl == VideoRateControl.ConstantQuality && Quality == 0 && Profile != H264Profile.Auto)
            throw new InvalidDataException("Lossless x264 (CRF 0) requires Auto profile. Use quality 1–51 with a fixed H.264 profile.");
    }
}
public enum VideoRateControl { Cbr, Vbr, ConstantQuality }
public enum EncodingPreset { Performance, Balanced, Quality }
public enum H264Profile { Auto, Baseline, Main, High }
public enum LayerKind { Video, Grid, Timecode, Spout, Network }
public enum NetworkSourceProtocol { Ndi, Omt, Srt }
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
    public NetworkSourceProtocol NetworkProtocol { get; init; }
    public string? NetworkSource { get; init; }
    public bool LiveAudioEnabled { get; init; }
    public float LiveAudioVolume { get; init; } = 1;
    public GridLayout? Grid { get; init; }
}
public sealed record Marker
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Marker";
    public long PositionTicks { get; init; }
    public string Color { get; init; } = "#FF70D6BB";
}
