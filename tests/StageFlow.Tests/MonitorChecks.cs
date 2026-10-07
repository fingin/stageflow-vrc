using System.Net;
using StageFlow.Core.Monitoring;
using StageFlow.Core.Projects;

internal static class MonitorChecks
{
    private static void Check(bool value) { if (!value) throw new Exception("Monitor assertion failed."); }
    public static void Links()
    {
        var settings = new StreamSettings { BindAddress = "0.0.0.0", Port = 18888 };
        var links = VideoMonitor.HlsLinks(settings, [IPAddress.Parse("192.168.1.42"), IPAddress.Parse("192.168.1.42"), IPAddress.Loopback, IPAddress.Parse("::1")]);
        Check(links.Count == 4 && links[0].Url == "http://127.0.0.1:18888/" && links[3].Url == "http://192.168.1.42:18888/live/index.m3u8");
        Check(links.All(l => l.OpenInBrowser && !l.Url.Contains("0.0.0.0")));
        links = VideoMonitor.HlsLinks(settings with { BindAddress = "192.168.1.99" }, [IPAddress.Parse("192.168.1.42")]);
        Check(links.Count == 2 && links.All(l => l.Url.Contains("192.168.1.99")));
        links = VideoMonitor.HlsLinks(settings with { BindAddress = "::" }, [IPAddress.Parse("fe80::1234%3"), IPAddress.Parse("fd00::42")]);
        Check(links.Count == 4 && links[0].Url == "http://[::1]:18888/" && links[2].Url == "http://[fd00::42]:18888/");
        string endpoint = VideoMonitor.SafeEndpoint("rtmps://fixture:password@host:443/live?token=private", false);
        Check(!endpoint.Contains("fixture") && !endpoint.Contains("password") && !endpoint.Contains("private") && endpoint.Contains("/live"));
        endpoint = VideoMonitor.SafeEndpoint("rtmp://host/live/private-key", true); Check(!endpoint.Contains("private-key"));
        endpoint = VideoMonitor.SafeEndpoint("srt://user:password@host:9001?mode=caller&latency=120000&streamid=secret&passphrase=private");
        Check(endpoint.Contains("mode=caller") && endpoint.Contains("latency=120000") && !endpoint.Contains("secret") && !endpoint.Contains("password") && !endpoint.Contains("passphrase"));
    }
    public static void States()
    {
        var destination = new NetworkStreamOutput { Enabled = true, StreamKey = "private-key", Password = "private-password", Url = "rtmp://host/live" };
        var input = new Layer { Kind = LayerKind.Network, NetworkProtocol = NetworkSourceProtocol.Srt, NetworkSource = "srt://host:9001?passphrase=private-input", Visible = false, LiveAudioEnabled = true };
        var project = new ShowProject { NetworkOutputs = [destination], LanVideo = new() { NdiEnabled = true }, Layers = [input] };
        var telemetry = new VideoTelemetry(true, null, 123, 48000, [new("hls", VideoRouteState.Waiting),
            new(destination.Id.ToString(), VideoRouteState.Error, Frames: 99, Queued: 256, Error: "Failed private-key private-password"),
            new("ndi", VideoRouteState.Live, Frames: 123, Address: "PC (Program)"), new(input.Id.ToString(), VideoRouteState.Stalled, Frames: 42, Width: 640, Height: 360)]);
        var rows = VideoMonitor.Build(project, telemetry, []);
        Check(rows.Single(r => r.Id == "hls").State == VideoRouteState.Waiting);
        Check(rows.Single(r => r.Id == "encoder:h264_nvenc").State == VideoRouteState.Live);
        Check(rows.Single(r => r.Id == "encoder:hevc_nvenc").State == VideoRouteState.NotSelected);
        Check(rows.Single(r => r.Id == "omt").State == VideoRouteState.Disabled);
        var output = rows.Single(r => r.Id == destination.Id.ToString()); Check(output.State == VideoRouteState.Error && output.Detail.Contains("256/256"));
        Check(rows.Single(r => r.Id == input.Id.ToString()).State == VideoRouteState.Stalled); // Hidden video can still receive/mix live audio.
        string text = string.Join('\n', rows.Select(r => r.Detail + string.Join('\n', r.Links.Select(l => l.Url))));
        foreach (string secret in new[] { "private-key", "private-password", "private-input" }) Check(!text.Contains(secret));
        rows = VideoMonitor.Build(project, telemetry with { EncoderRunning = false, EncoderError = "Encoder failed", Routes = [] }, []);
        Check(rows.Single(r => r.Id == "encoder:h264_nvenc").State == VideoRouteState.Error && rows.Single(r => r.Id == destination.Id.ToString()).State == VideoRouteState.Error);
        rows = VideoMonitor.Build(project, telemetry with { FatalError = "GPU stopped" }, []);
        Check(!rows.Any(r => r.State == VideoRouteState.Live));
        rows = VideoMonitor.Build(project, new(false, null, 0, 0, []), []);
        Check(rows.Single(r => r.Id == "hls").State == VideoRouteState.Idle && rows.Single(r => r.Id == destination.Id.ToString()).State == VideoRouteState.Idle);
    }
}
