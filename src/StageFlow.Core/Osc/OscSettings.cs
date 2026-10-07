namespace StageFlow.Core.Osc;

public enum ControlAction { Play, Pause, TogglePlay, Stop, SeekSeconds, SeekNormalized, NextMarker, PreviousMarker,
    LocalVolume, StreamVolume, LocalMute, StreamMute, LayerOpacity, LayerVisible, StartStream, StopStream, ToggleStream, StartDiagnostics, StopDiagnostics }
public enum OscTrigger { Press, RisingEdge, EveryMessage, Value }
public sealed record OscMapping
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "/project/record";
    public ControlAction Action { get; init; } = ControlAction.Play;
    public OscTrigger Trigger { get; init; } = OscTrigger.Press;
    public int Argument { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1;
    public Guid? LayerId { get; init; }
}
public sealed record OscSettings
{
    public bool Enabled { get; init; }
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 9000;
    public List<OscMapping> Mappings { get; init; } = [];
    public void Validate()
    {
        if (!System.Net.IPAddress.TryParse(BindAddress, out _) || Port is < 1 or > 65535 || Mappings == null || Mappings.Count > 2048)
            throw new InvalidDataException("Invalid OSC listener settings.");
        if (Mappings.Any(m => m == null) || Mappings.Select(m => m.Id).Distinct().Count() != Mappings.Count)
            throw new InvalidDataException("Invalid OSC mapping IDs.");
        foreach (var mapping in Mappings)
            if (!ValidAddress(mapping.Address) || !Enum.IsDefined(mapping.Action) || !Enum.IsDefined(mapping.Trigger) ||
                mapping.Argument is < 0 or > 63 || !double.IsFinite(mapping.Minimum) || !double.IsFinite(mapping.Maximum))
                throw new InvalidDataException("OSC mappings need an address beginning with /, a valid action and finite limits.");
    }
    public static bool ValidAddress(string? address) => address is { Length: > 1 and <= 1024 } && address[0] == '/' &&
        !address.Any(c => c < 33 || c > 126 || "#*,?[]{}".Contains(c));
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
