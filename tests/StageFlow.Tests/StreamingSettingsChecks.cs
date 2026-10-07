using StageFlow.Core.Projects;

internal static class StreamingSettingsChecks
{
    private static void Check(bool value) { if (!value) throw new Exception("Streaming settings assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid setting accepted."); }
    public static void Destinations()
    {
        var output = new NetworkStreamOutput { Url = "rtmps://example.test:443/live/app?token=server", StreamKey = "folder/key?token=a+b%20c&x=2",
            AuthenticationEnabled = true, Username = "operator", Password = "p@ss:word%+", Enabled = true };
        Check(output.ConnectionUrl() == "rtmps://operator:p@ss:word%+@example.test:443/live/app?token=server");
        Check(output.RtmpApplication() == "live/app?token=server");
        Check((output with { Url = "rtmp://old:secret@[::1]:1935/live" }).ConnectionUrl() == "rtmp://operator:p@ss:word%+@[::1]:1935/live");
        Check((output with { Url = "rtmp://host", StreamKey = "k" }).ConnectionUrl() == "rtmp://operator:p@ss:word%+@host");
        var legacy = new NetworkStreamOutput { Url = "rtmp://old:secret@host/live/key?token=a+b" };
        Check(legacy.ConnectionUrl() == legacy.Url && legacy.RtmpApplication() == null);
        var srt = new NetworkStreamOutput { Protocol = NetworkStreamProtocol.Srt, Url = "srt://host:9001?mode=caller", AuthenticationEnabled = true };
        Check(srt.ConnectionUrl() == srt.Url && srt.RtmpApplication() == null);
        Reject(() => (output with { Username = "" }).Validate());
        Reject(() => (output with { Username = "operator&role=admin" }).Validate());
        Reject(() => (output with { Url = "rtmp://host/live bad" }).Validate());
        Reject(() => (output with { Password = "" }).Validate());
        Reject(() => (output with { Password = "a/b" }).Validate());
        Reject(() => (output with { Password = new string('x', 50) }).Validate());
        Reject(() => (output with { StreamKey = "a\0b" }).Validate());
        Check(!output.ToString().Contains(output.Password));
    }
    public static void EncodingAndPersistence()
    {
        var stream = new StreamSettings { RateControl = VideoRateControl.Vbr, VideoBitrateKbps = 4000, MaxBitrateKbps = 6000,
            Preset = EncodingPreset.Balanced, Profile = H264Profile.High, KeyframeSeconds = 2, BFrames = 2 };
        stream.Validate(); Check(stream.GopFrames(60000, 1001) == 120); Check(stream.HlsTargetSeconds(60000, 1001) > 2);
        Check((stream with { KeyframeSeconds = 0, SegmentSeconds = 1 }).GopFrames(30, 1) == 30);
        Reject(() => (stream with { MaxBitrateKbps = 2000 }).Validate());
        Reject(() => (stream with { KeyframeSeconds = double.NaN }).Validate());
        Reject(() => (stream with { Profile = H264Profile.Baseline }).Validate());
        string path = Path.Combine(Path.GetTempPath(), "StageFlow-encoding-" + Guid.NewGuid() + ".stageflow");
        try
        {
            var output = new NetworkStreamOutput { StreamKey = "fixture-key", Username = "fixture", Password = "fixture-password", AuthenticationEnabled = true };
            ProjectStore.Save(new() { Stream = stream, NetworkOutputs = [output] }, path);
            var loaded = ProjectStore.Load(path); Check(loaded.Stream == stream && loaded.NetworkOutputs.Single() == output);
            File.WriteAllText(path, "{\"schemaVersion\":2,\"networkOutputs\":[{\"url\":\"rtmp://host/live/old-key\"}]}");
            loaded = ProjectStore.Load(path); Check(loaded.Stream.KeyframeSeconds == 0 && loaded.Stream.BFrames == 0 && loaded.NetworkOutputs[0].StreamKey == "");
            Check(loaded.NetworkOutputs[0].ConnectionUrl() == "rtmp://host/live/old-key");
        }
        finally { File.Delete(path); }
    }
}
