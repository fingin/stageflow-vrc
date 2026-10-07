namespace StageFlow.Core.Osc;

public enum ControlAction { Play, Pause, TogglePlay, Stop, SeekSeconds, SeekNormalized, NextMarker, PreviousMarker,
    LocalVolume, StreamVolume, LocalMute, StreamMute, LayerOpacity, LayerVisible, StartStream, StopStream, ToggleStream, StartDiagnostics, StopDiagnostics }
public enum OscTrigger { Press, RisingEdge, EveryMessage, Value }
public enum OscFeedbackFormat { Float32, Int32, Boolean }
public sealed record OscMapping
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "/transport/play";
    public ControlAction Action { get; init; } = ControlAction.Play;
    public OscTrigger Trigger { get; init; } = OscTrigger.Press;
    public int Argument { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1;
    public Guid? LayerId { get; init; }
    public bool FeedbackEnabled { get; init; } = true;
    public string FeedbackAddress { get; init; } = "";
    public OscFeedbackFormat FeedbackFormat { get; init; }
    public int FeedbackArgument { get; init; }
}
public sealed record OscSettings
{
    public bool Enabled { get; init; }
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 9000;
    public bool FeedbackEnabled { get; init; }
    public string FeedbackHost { get; init; } = "127.0.0.1";
    public int FeedbackPort { get; init; } = 9001;
    public int FeedbackIntervalMilliseconds { get; init; } = 100;
    public int FeedbackRefreshSeconds { get; init; } = 2;
    public List<OscMapping> Mappings { get; init; } = [];
    public void Validate()
    {
        if (!System.Net.IPAddress.TryParse(BindAddress, out _) || Port is < 1 or > 65535 || Mappings == null || Mappings.Count > 2048)
            throw new InvalidDataException("Invalid OSC listener settings.");
        if (Mappings.Any(m => m == null) || Mappings.Select(m => m.Id).Distinct().Count() != Mappings.Count)
            throw new InvalidDataException("Invalid OSC mapping IDs.");
        if (!System.Net.IPAddress.TryParse(FeedbackHost, out var destination) || destination.Equals(System.Net.IPAddress.Any) || destination.Equals(System.Net.IPAddress.IPv6Any) ||
            FeedbackPort is < 1 or > 65535 || FeedbackIntervalMilliseconds is < 20 or > 2000 || FeedbackRefreshSeconds is < 1 or > 60)
            throw new InvalidDataException("OSC feedback needs a destination IP, port 1–65535, update interval 20–2000 ms and refresh interval 1–60 seconds.");
        if (Enabled && FeedbackEnabled && Port == FeedbackPort && (System.Net.IPAddress.IsLoopback(destination) || destination.Equals(System.Net.IPAddress.Parse(BindAddress))))
            throw new InvalidDataException("Use different OSC command and feedback ports on this computer.");
        foreach (var mapping in Mappings)
            if (!ValidAddress(mapping.Address) || !Enum.IsDefined(mapping.Action) || !Enum.IsDefined(mapping.Trigger) ||
                mapping.Argument is < 0 or > 63 || !double.IsFinite(mapping.Minimum) || !double.IsFinite(mapping.Maximum) ||
                mapping.FeedbackArgument is < 0 or > 63 || !Enum.IsDefined(mapping.FeedbackFormat) || mapping.FeedbackAddress == null ||
                mapping.FeedbackAddress.Length > 0 && !ValidAddress(mapping.FeedbackAddress) ||
                mapping.Trigger == OscTrigger.Value && (mapping.Minimum == mapping.Maximum || !double.IsFinite(mapping.Maximum - mapping.Minimum)))
                throw new InvalidDataException("OSC mappings need an address beginning with /, a valid action and finite limits.");
        if (FeedbackEnabled && Mappings.Where(m => m.Enabled && m.FeedbackEnabled).GroupBy(OscMappingDefaults.FeedbackAddress).Any(g => g.Count() > 1))
            throw new InvalidDataException("Each enabled feedback mapping needs a unique feedback address. Set a separate return address when commands share an address.");
    }
    public static bool ValidAddress(string? address) => address is { Length: > 1 and <= 1024 } && address[0] == '/' &&
        !address.Any(c => c < 33 || c > 126 || "#*,?[]{}".Contains(c));
}

public static class OscMappingDefaults
{
    public static string FeedbackAddress(OscMapping mapping) => string.IsNullOrEmpty(mapping.FeedbackAddress) ? mapping.Address : mapping.FeedbackAddress;
    public static OscMapping ForAction(ControlAction action) => new()
    {
        Action = action,
        Address = action switch
        {
            ControlAction.Play => "/transport/play", ControlAction.Pause => "/transport/pause", ControlAction.TogglePlay => "/transport/toggle",
            ControlAction.Stop => "/transport/stop", ControlAction.SeekSeconds => "/transport/seconds", ControlAction.SeekNormalized => "/transport/position",
            ControlAction.NextMarker => "/markers/next", ControlAction.PreviousMarker => "/markers/previous",
            ControlAction.LocalVolume => "/audio/local/volume", ControlAction.StreamVolume => "/audio/program/volume",
            ControlAction.LocalMute => "/audio/local/mute", ControlAction.StreamMute => "/audio/program/mute",
            ControlAction.LayerOpacity => "/layer/opacity", ControlAction.LayerVisible => "/layer/visible",
            ControlAction.StartStream => "/stream/start", ControlAction.StopStream => "/stream/stop", ControlAction.ToggleStream => "/stream/toggle",
            ControlAction.StartDiagnostics => "/diagnostics/start", ControlAction.StopDiagnostics => "/diagnostics/stop", _ => "/control"
        },
        Trigger = action switch
        {
            ControlAction.SeekSeconds or ControlAction.SeekNormalized or ControlAction.LocalVolume or ControlAction.StreamVolume or ControlAction.LayerOpacity => OscTrigger.Value,
            ControlAction.LocalMute or ControlAction.StreamMute or ControlAction.LayerVisible => OscTrigger.EveryMessage,
            ControlAction.TogglePlay or ControlAction.ToggleStream => OscTrigger.RisingEdge, _ => OscTrigger.Press
        },
        Maximum = action == ControlAction.SeekSeconds ? 60 : 1
    };
    public static OscMapping Create(ControlAction action, IEnumerable<OscMapping> existing)
    {
        var mapping = ForAction(action); var used = existing.Select(m => m.Address).ToHashSet(StringComparer.Ordinal);
        string address = mapping.Address; int suffix = 2;
        while (used.Contains(address)) address = mapping.Address + "/" + suffix++;
        return mapping with { Address = address };
    }
    public static OscMapping ChangeAction(OscMapping mapping, ControlAction action)
    {
        var oldDefaults = ForAction(mapping.Action); var defaults = ForAction(action);
        return mapping with { Action = action, Address = mapping.Address == oldDefaults.Address ? defaults.Address : mapping.Address,
            Trigger = defaults.Trigger, Minimum = defaults.Minimum, Maximum = defaults.Maximum };
    }
}
public readonly record struct ControlInvocation(ControlAction Action, double Value, Guid? LayerId);

/// <summary>Compiled address lookup; state belongs to mapping + sender, not a global button.</summary>
public sealed class OscRouter
{
    private Dictionary<string, OscMapping[]> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid, string), bool> _pressed = [];
    public void Configure(IEnumerable<OscMapping> mappings)
    { _routes = mappings.Where(m => m.Enabled).GroupBy(m => m.Address).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal); _pressed.Clear(); }
    public IEnumerable<ControlInvocation> Route(OscMessage message, string sender)
    {
        if (!_routes.TryGetValue(message.Address, out var mappings)) yield break;
        foreach (var mapping in mappings)
        {
            double value = message.Arguments.Length == 0 ? 1 : mapping.Argument < message.Arguments.Length
                ? Number(message.Arguments[mapping.Argument]) : double.NaN;
            if (!double.IsFinite(value)) continue;
            bool pressed = value > 0;
            var key = (mapping.Id, sender);
            bool wasPressed = _pressed.GetValueOrDefault(key);
            if (_pressed.Count > 4096) _pressed.Clear();
            _pressed[key] = pressed;
            if (mapping.Trigger == OscTrigger.Press && !pressed || mapping.Trigger == OscTrigger.RisingEdge && (!pressed || wasPressed)) continue;
            double mapped = mapping.Trigger == OscTrigger.Value
                ? mapping.Minimum + Math.Clamp(value, 0, 1) * (mapping.Maximum - mapping.Minimum) : value;
            yield return new(mapping.Action, mapped, mapping.LayerId);
        }
    }
    private static double Number(object? value) => value switch
    { int i => i, long i => i, float f => f, double d => d, bool b => b ? 1 : 0,
      string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) => n, _ => double.NaN };
}
