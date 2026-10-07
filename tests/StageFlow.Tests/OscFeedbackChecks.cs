using System.Net;
using System.Net.Sockets;
using StageFlow.Core.Osc;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;

internal static class OscFeedbackChecks
{
    private static void Check(bool ok) { if (!ok) throw new Exception("OSC feedback assertion failed."); }
    private static float Value(IEnumerable<OscFeedbackValue> values, string address) => (float)values.Single(v => v.Address == address).Arguments[^1];
    public static void Defaults()
    {
        var settings = new OscSettings(); Check(settings.Port == 8000 && settings.FeedbackPort == 8001 && !settings.FeedbackEnabled);
        var a = OscMappingDefaults.Create(ControlAction.Play, []); var b = OscMappingDefaults.Create(ControlAction.Play, [a]);
        Check(a.Address == "/transport/play" && b.Address == "/transport/play/2");
        foreach (var action in Enum.GetValues<ControlAction>()) (settings with { Mappings = [OscMappingDefaults.ForAction(action)] }).Validate();
        var custom = OscMappingDefaults.ChangeAction(a with { Address = "/project/record" }, ControlAction.LocalVolume);
        Check(custom.Address == "/project/record" && custom.Trigger == OscTrigger.Value && custom.Minimum == 0 && custom.Maximum == 1);
        var mute = OscMappingDefaults.ForAction(ControlAction.LocalMute); var router = new OscRouter(); router.Configure([mute]);
        Check(router.Route(new(mute.Address, [0f]), "pilot").Single().Value == 0);
        bool invalid = false;
        try { (settings with { Enabled = true, FeedbackEnabled = true, FeedbackPort = settings.Port }).Validate(); } catch (InvalidDataException) { invalid = true; }
        Check(invalid); invalid = false;
        try { (settings with { FeedbackEnabled = true, Mappings = [a, a with { Id = Guid.NewGuid() }] }).Validate(); } catch (InvalidDataException) { invalid = true; }
        Check(invalid);
    }
    public static void Transport()
    {
        var transport = new Transport(); transport.Load(TimeSpan.FromSeconds(120));
        var settings = new OscSettings { Mappings = [.. new[] { ControlAction.Play, ControlAction.Pause, ControlAction.Stop, ControlAction.SeekNormalized }.Select(OscMappingDefaults.ForAction)] };
        var projector = new OscFeedbackProjector(settings);
        OscFeedbackState State() => new(transport.Snapshot(), new(), [], null, false, false);
        var initial = projector.Updates(State(), 0).ToArray(); Check(Value(initial, "/transport/play") == 0 && Value(initial, "/transport/pause") == 1);
        Check(!projector.Updates(State(), 10).Any());
        transport.Play(); Check(Value(projector.Updates(State(), 100).ToArray(), "/transport/play") == 1);
        transport.Pause(); var paused = projector.Updates(State(), 200).ToArray(); Check(Value(paused, "/transport/play") == 0 && Value(paused, "/transport/pause") == 1);
        transport.Seek(TimeSpan.FromSeconds(60)); Check(Value(projector.Updates(State(), 300), "/transport/position") == .5f);
        transport.Stop(); Check(Value(projector.Updates(State(), 400), "/transport/stop") == 1);
        Check(projector.Updates(State(), 2000).Count() == settings.Mappings.Count);
        Check(projector.Updates(State(), 2010, true).Count() == settings.Mappings.Count);
    }
    public static void Values()
    {
        var layer = new Layer { Opacity = .25, Visible = true };
        var settings = new OscSettings { Mappings = [
            OscMappingDefaults.ForAction(ControlAction.LocalVolume) with { Minimum = 1, Maximum = 0, FeedbackAddress = "/fader" },
            OscMappingDefaults.ForAction(ControlAction.StreamMute) with { FeedbackFormat = OscFeedbackFormat.Boolean },
            OscMappingDefaults.ForAction(ControlAction.LayerOpacity),
            OscMappingDefaults.ForAction(ControlAction.LayerVisible) with { LayerId = layer.Id, FeedbackFormat = OscFeedbackFormat.Int32, FeedbackArgument = 2 },
            OscMappingDefaults.ForAction(ControlAction.StartStream), OscMappingDefaults.ForAction(ControlAction.StartDiagnostics),
            OscMappingDefaults.ForAction(ControlAction.Pause) with { FeedbackEnabled = false },
            OscMappingDefaults.ForAction(ControlAction.Stop) with { Enabled = false }] };
        settings.Validate();
        var projector = new OscFeedbackProjector(settings);
        var state = new OscFeedbackState(default, new() { LocalVolume = .25f, StreamMute = true }, [layer], layer.Id, true, true);
        var values = projector.Updates(state, 0).ToArray(); Check(values.Length == 6);
        Check(Value(values, "/fader") == .75f && Value(values, "/layer/opacity") == .25f);
        Check((bool)values.Single(v => v.Address == "/audio/program/mute").Arguments[0]);
        var visible = values.Single(v => v.Address == "/layer/visible"); Check(visible.Arguments.Length == 3 && (int)visible.Arguments[2] == 1);
        Check(Value(values, "/stream/start") == 1 && Value(values, "/diagnostics/start") == 1);
        Check(Value(projector.Updates(state with { Layers = [] }, 100), "/layer/opacity") == 0);
        byte[] packet = OscCodec.Encode("/play", 1f);
        Check(packet.SequenceEqual(new byte[] { 47,112,108,97,121,0,0,0,44,102,0,0,63,128,0,0 }));
    }
    public static void Udp()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        var settings = new OscSettings { FeedbackEnabled = true, FeedbackPort = port, Mappings = [OscMappingDefaults.ForAction(ControlAction.LocalVolume)] };
        using var sender = new OscFeedbackSender(settings);
        var state = new OscFeedbackState(default, new() { LocalVolume = .25f }, [], null, false, false);
        float Receive()
        {
            using var timeout = new CancellationTokenSource(3000); var packet = receiver.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            Check(OscCodec.TryDecode(packet.Buffer, out var values)); return (float)values.Single().Arguments[0]!;
        }
        sender.Publish(state); Check(Receive() == .25f);
        sender.Publish(state with { Audio = new() { LocalVolume = .75f } }); Check(Receive() == .75f);
        sender.Publish(state with { Audio = new() { LocalVolume = .75f } }, true); Check(Receive() == .75f);
        Check(sender.Sent >= 2 && sender.Error == null);
    }
    public static void Persistence()
    {
        string path = Path.Combine(Path.GetTempPath(), "stageflow-feedback-" + Guid.NewGuid() + ".stageflow");
        File.WriteAllText(path, """{"schemaVersion":2,"osc":{"mappings":[{"address":"/project/record","action":"Play"}]}}""");
        var legacy = ProjectStore.Load(path); Check(!legacy.Osc.FeedbackEnabled && legacy.Osc.Mappings[0].Address == "/project/record");
        var changed = legacy with { Osc = legacy.Osc with { FeedbackEnabled = true, FeedbackPort = 9101, Mappings = [legacy.Osc.Mappings[0] with { FeedbackAddress = "/lights/play", FeedbackArgument = 1 }] } };
        ProjectStore.Save(changed, path); var loaded = ProjectStore.Load(path);
        Check(loaded.Osc.FeedbackEnabled && loaded.Osc.FeedbackPort == 9101 && loaded.Osc.Mappings[0].FeedbackAddress == "/lights/play" && loaded.Osc.Mappings[0].FeedbackArgument == 1);
    }
}
