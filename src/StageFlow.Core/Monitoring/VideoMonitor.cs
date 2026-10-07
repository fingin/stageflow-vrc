using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using StageFlow.Core.Projects;

namespace StageFlow.Core.Monitoring;

public sealed record MonitorLink(string Label, string Url, bool OpenInBrowser = false);
public sealed record MonitorRoute(string Id, string Name, VideoRouteState State, string Detail, IReadOnlyList<MonitorLink> Links, string Settings);

public static class VideoMonitor
{
    public static IReadOnlyList<MonitorLink> HlsLinks(StreamSettings settings, IEnumerable<IPAddress> interfaces)
    {
        var bind = IPAddress.Parse(settings.BindAddress);
        bool any = bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any);
        var addresses = any ? new[] { bind.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback }
            .Concat(interfaces.Where(a => a.AddressFamily == bind.AddressFamily && !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any) && !a.IsIPv6LinkLocal))
            .Distinct() : [bind];
        return addresses.SelectMany(address =>
        {
            string root = new UriBuilder("http", address.ToString(), settings.Port).Uri.AbsoluteUri;
            string location = IPAddress.IsLoopback(address) ? "This PC" : any ? "LAN " + address : "Bound interface " + address;
            return new[] { new MonitorLink(location + " · Player", root, true), new MonitorLink(location + " · Playlist", root + "live/index.m3u8", true) };
        }).ToArray();
    }
    public static string SafeEndpoint(string? value, bool legacyRtmp = false)
    {
        if (string.IsNullOrEmpty(value)) return "Not configured";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtmp" or "rtmps" or "srt")) return value;
        string server = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        if (uri.Scheme is "rtmp" or "rtmps")
            return server + (legacyRtmp ? "/[publish path hidden]" : uri.AbsolutePath) + (uri.Query.Length > 0 ? "?[query hidden]" : "");
        var visible = new List<string>(); bool hidden = false;
        foreach (string parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = parameter.Split('=', 2); string key = pair[0].ToLowerInvariant(), argument = pair.Length == 2 ? pair[1] : "";
            if (key == "mode" && argument is "caller" or "listener" or "rendezvous" ||
                key is "latency" or "rcvlatency" or "peerlatency" or "pkt_size" && uint.TryParse(argument, out _)) visible.Add(key + "=" + argument);
            else hidden = true;
        }
        if (hidden) visible.Add("[private options hidden]");
        return server + (visible.Count > 0 ? "?" + string.Join('&', visible) : "");
    }
    public static string SafeError(string? error, ShowProject project)
    {
        if (error == null) return "";
        foreach (var output in project.NetworkOutputs)
            foreach (string secret in new[] { output.StreamKey, output.Password }) if (secret.Length > 0) error = error.Replace(secret, "[hidden]", StringComparison.Ordinal);
        return Regex.Replace(error, @"(?:rtmps?|srt)://[^\s]+", m => SafeEndpoint(m.Value, legacyRtmp: true), RegexOptions.IgnoreCase);
    }
    public static string StateText(VideoRouteState state) => state switch
    {
        VideoRouteState.NotSelected => "Not selected", VideoRouteState.Live => "Active", VideoRouteState.Error => "Error",
        _ => state.ToString()
    };
    public static IReadOnlyList<MonitorRoute> Build(ShowProject project, VideoTelemetry? telemetry, IEnumerable<IPAddress> interfaces)
    {
        var rows = new List<MonitorRoute>();
        string video = $"{project.Width} × {project.Height} · {((double)project.FpsNumerator / project.FpsDenominator).ToString("0.###", CultureInfo.InvariantCulture)} fps";
        string codec = project.Stream.Encoder == "hevc_nvenc" ? "HEVC" : "H.264";
        string audio = $"AAC · 48 kHz stereo · {project.Stream.AudioBitrateKbps} kbps" + (project.Audio.StreamMute ? " · MUTED" : "");
        string encoded = $"{codec} · {video}\n{audio}\nEncoder: {project.Stream.Encoder} · {project.Stream.RateControl}\n" +
            (project.Stream.RateControl == VideoRateControl.ConstantQuality ? $"Quality {project.Stream.Quality}" : $"Target {project.Stream.VideoBitrateKbps} kbps") +
            $" · preset {project.Stream.Preset}\nKeyframe interval {(project.Stream.KeyframeSeconds == 0 ? project.Stream.SegmentSeconds : project.Stream.KeyframeSeconds):0.###} s · B-frames {project.Stream.BFrames}";
        VideoRouteTelemetry Status(string id, bool enabled, bool encodedOutput = false)
        {
            if (!enabled) return new(id, VideoRouteState.Disabled);
            if (telemetry?.FatalError != null) return new(id, VideoRouteState.Error, Error: telemetry.FatalError);
            return telemetry?.Routes.FirstOrDefault(r => r.Id == id) ?? new(id,
                encodedOutput && telemetry?.EncoderError != null ? VideoRouteState.Error : encodedOutput ? VideoRouteState.Idle : VideoRouteState.Waiting,
                Error: encodedOutput ? telemetry?.EncoderError : null);
        }
        void Add(string id, string name, VideoRouteTelemetry state, string details, IReadOnlyList<MonitorLink> links, string settings)
        {
            if (state.Error != null) details += "\n\n" + SafeError(state.Error, project);
            rows.Add(new(id, name, state.State, details, links, settings));
        }
        var hls = Status("hls", project.Stream.HlsEnabled, true);
        Add("hls", "HLS · player & playlist", hls, encoded + $"\nMPEG-TS segments · HTTP\nSegments retained: {hls.Frames} · HTTP requests: {hls.AudioPackets}\nServed: {hls.Bytes / 1048576d:0.00} MiB\n" +
            (hls.State == VideoRouteState.Waiting ? "Listener started; waiting for the first complete segment." : hls.State == VideoRouteState.Live ? "Playlist ready. LAN reachability depends on the selected interface and network." : "Start streaming to serve these links."),
            HlsLinks(project.Stream, interfaces), "HLS server");
        foreach (var protocol in Enum.GetValues<NetworkStreamProtocol>())
        {
            var outputs = project.NetworkOutputs.Where(o => o.Protocol == protocol).ToArray();
            if (outputs.Length == 0) rows.Add(new(protocol.ToString(), protocol == NetworkStreamProtocol.Rtmp ? "RTMP / RTMPS" : "SRT", VideoRouteState.Disabled,
                "No destinations configured. Add one in Preferences → RTMP & SRT.", [], "RTMP & SRT"));
            foreach (var output in outputs)
            {
                var state = Status(output.Id.ToString(), output.Enabled, true);
                string format = output.Protocol == NetworkStreamProtocol.Rtmp ? "FLV · publish destination (not a playback page)" : "MPEG-TS · network destination";
                Add(output.Id.ToString(), new Uri(output.Url).Scheme.ToUpperInvariant() + " · " + output.Name, state,
                    encoded + $"\n{format}\nVideo packets sent: {state.Frames:N0} · audio packets: {state.AudioPackets:N0}\nEncoded payload: {state.Bytes / 1048576d:0.00} MiB · queue: {state.Queued}/256\n" +
                    (output.AuthenticationEnabled ? "Authentication configured\n" : "") + "Private keys, credentials and query tokens are hidden. Counters show sender activity.",
                    [new("Configured endpoint", SafeEndpoint(output.Url, output.StreamKey.Length == 0))], "RTMP & SRT");
            }
        }
        foreach (var (id, name, enabled, source, format) in new[] {
            ("spout", "Spout", project.Spout.OutputEnabled, project.Spout.OutputName, "D3D11 shared BGRA texture · no audio"),
            ("ndi", "NDI", project.LanVideo.NdiEnabled, Environment.MachineName + " (" + project.LanVideo.NdiName + ")", "BGRX supplied to NDI SDK · 48 kHz stereo PCM"),
            ("omt", "OMT", project.LanVideo.OmtEnabled, project.LanVideo.OmtName, "BGRA supplied to OMT SDK · 48 kHz stereo PCM") })
        {
            var state = Status(id, enabled);
            Add(id, name + " · program output", state, $"{video}\n{format}\nFrames sent: {state.Frames:N0} · dropped/skipped: {state.Dropped:N0}\n" +
                (id == "spout" ? "Select this sender name in a Spout receiver." : "Select the source in a compatible receiver. Active indicates sending; receiver/viewer count is not reported.") +
                (id != "spout" && project.Audio.StreamMute ? "\nProgram audio: MUTED" : ""),
                [new(id == "omt" && state.Address != null ? "OMT address" : "Source name", state.Address ?? source)], id == "spout" ? "Spout" : "NDI & OMT");
        }
        foreach (var layer in project.Layers.Where(l => l.Kind is LayerKind.Network or LayerKind.Spout))
        {
            var state = Status(layer.Id.ToString(), true);
            string format = state.Width > 0 ? $"Received {state.Width} × {state.Height} → GPU compositor" : "Waiting for video format";
            string protocol = layer.Kind == LayerKind.Spout ? "Spout" : layer.NetworkProtocol.ToString().ToUpperInvariant();
            Add(layer.Id.ToString(), protocol + " in · " + layer.Name, layer.Visible || layer.Kind == LayerKind.Network ? state : state with { State = VideoRouteState.Hidden },
                format + (layer.Kind == LayerKind.Spout ? "\nShared texture sampled at compositor frame rate.\n" : $"\nFrames received: {state.Frames:N0}\n") + (layer.Visible ? "Visible in program" : "Layer hidden") +
                (layer.Kind == LayerKind.Network ? "\nLive audio mix: " + (layer.LiveAudioEnabled ? "enabled (48 kHz stereo)" : "off") : "\nSpout has no audio") +
                (state.State == VideoRouteState.Stalled ? "\nNo new frames for more than 3 seconds." : ""),
                [new("Source", SafeEndpoint(layer.Kind == LayerKind.Spout ? layer.SpoutSender : layer.NetworkSource))], "Selection");
        }
        foreach (string encoder in new[] { "h264_nvenc", "hevc_nvenc", "libx264" })
        {
            bool selected = encoder == project.Stream.Encoder;
            string? error = selected ? telemetry?.EncoderError ?? telemetry?.FatalError : null;
            var state = !selected ? VideoRouteState.NotSelected : error != null ? VideoRouteState.Error : telemetry?.EncoderRunning == true ? VideoRouteState.Live : VideoRouteState.Idle;
            string description = encoder switch { "h264_nvenc" => "NVIDIA hardware H.264", "hevc_nvenc" => "NVIDIA hardware HEVC", _ => "CPU x264 H.264" };
            Add("encoder:" + encoder, "Encoder · " + description, new(encoder, state, Error: error), selected ? encoded +
                $"\nVideo frames encoded: {telemetry?.EncodedFrames ?? 0:N0}\nAudio samples encoded: {telemetry?.EncodedAudioSamples ?? 0:N0}\nShared by HLS, RTMP and SRT." :
                description + "\nIncluded backend; not selected. Hardware capability is checked when starting this encoder.", [], "Encoding");
        }
        return rows;
    }
}
