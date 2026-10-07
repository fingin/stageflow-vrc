using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;

namespace StageFlow.Core.Osc;

public sealed record OscFeedbackState(TransportSnapshot Transport, AudioSettings Audio, IReadOnlyList<Layer> Layers,
    Guid? SelectedLayer, bool Streaming, bool Recording);
public sealed record OscFeedbackValue(string Address, object[] Arguments);

// Pure state projection; never echoes a command as though it were the applied state.
public sealed class OscFeedbackProjector(OscSettings settings)
{
    private readonly OscMapping[] _mappings = settings.Mappings.Where(m => m.Enabled && m.FeedbackEnabled).ToArray();
    private readonly Dictionary<Guid, object> _previous = [];
    private IReadOnlyList<Layer>? _layers;
    private Dictionary<Guid, Layer> _byId = [];
    private long _lastRefresh = long.MinValue;
    public IEnumerable<OscFeedbackValue> Updates(OscFeedbackState state, long milliseconds, bool force = false)
    {
        bool refresh = force || _lastRefresh == long.MinValue || milliseconds - _lastRefresh >= settings.FeedbackRefreshSeconds * 1000L;
        if (refresh) _lastRefresh = milliseconds;
        if (!ReferenceEquals(_layers, state.Layers)) { _layers = state.Layers; _byId = state.Layers.ToDictionary(l => l.Id); }
        foreach (var mapping in _mappings)
        {
            Layer? layer = (mapping.LayerId ?? state.SelectedLayer) is Guid id ? _byId.GetValueOrDefault(id) : null;
            double value = mapping.Action switch
            {
                ControlAction.Play or ControlAction.TogglePlay => state.Transport.State == TransportState.Playing ? 1 : 0,
                ControlAction.Pause => state.Transport.State == TransportState.Paused ? 1 : 0,
                ControlAction.Stop => state.Transport.State == TransportState.Stopped ? 1 : 0,
                ControlAction.SeekSeconds => state.Transport.Position.TotalSeconds,
                ControlAction.SeekNormalized => state.Transport.Duration.Ticks > 0 ? state.Transport.Position.Ticks / (double)state.Transport.Duration.Ticks : 0,
                ControlAction.LocalVolume => state.Audio.LocalVolume, ControlAction.StreamVolume => state.Audio.StreamVolume,
                ControlAction.LocalMute => state.Audio.LocalMute ? 1 : 0, ControlAction.StreamMute => state.Audio.StreamMute ? 1 : 0,
                ControlAction.LayerOpacity => layer?.Opacity ?? 0, ControlAction.LayerVisible => layer?.Visible == true ? 1 : 0,
                ControlAction.StartStream or ControlAction.ToggleStream => state.Streaming ? 1 : 0, ControlAction.StopStream => state.Streaming ? 0 : 1,
                ControlAction.StartDiagnostics => state.Recording ? 1 : 0, ControlAction.StopDiagnostics => state.Recording ? 0 : 1,
                _ => 0 // Momentary next/previous cue controls return to idle.
            };
            if (mapping.Trigger == OscTrigger.Value) value = Math.Clamp((value - mapping.Minimum) / (mapping.Maximum - mapping.Minimum), 0, 1);
            if (!double.IsFinite(value)) continue;
            object wireValue = mapping.FeedbackFormat switch
            {
                OscFeedbackFormat.Int32 => (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue),
                OscFeedbackFormat.Boolean => value > 0,
                _ => (float)Math.Clamp(value, -float.MaxValue, float.MaxValue)
            };
            if (!refresh && _previous.TryGetValue(mapping.Id, out var previous) && previous.Equals(wireValue)) continue;
            _previous[mapping.Id] = wireValue;
            var arguments = new object[mapping.FeedbackArgument + 1]; Array.Fill<object>(arguments, 0f); arguments[^1] = wireValue;
            yield return new(OscMappingDefaults.FeedbackAddress(mapping), arguments);
        }
    }
}

// One latest-state mailbox; network I/O and message encoding never run on UI/render threads.
public sealed class OscFeedbackSender : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Channel<OscFeedbackState> _states = Channel.CreateBounded<OscFeedbackState>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly OscFeedbackProjector _projector;
    private readonly Task _send;
    private int _force;
    private long _sent;
    private string? _error;
    public long Sent => Interlocked.Read(ref _sent);
    public string? Error => Volatile.Read(ref _error);
    public IPEndPoint LocalEndpoint { get; }
    public OscFeedbackSender(OscSettings settings)
    {
        settings.Validate(); _projector = new(settings);
        var address = IPAddress.Parse(settings.FeedbackHost); _udp = new UdpClient(address.AddressFamily);
        try { _udp.Connect(new IPEndPoint(address, settings.FeedbackPort)); LocalEndpoint = (IPEndPoint)_udp.Client.LocalEndPoint!; }
        catch { _udp.Dispose(); _cancel.Dispose(); throw; }
        _send = Task.Run(Send);
    }
    public void Publish(OscFeedbackState state, bool force = false)
    { if (force) Interlocked.Exchange(ref _force, 1); _states.Writer.TryWrite(state); }
    private async Task Send()
    {
        try
        {
            await foreach (var state in _states.Reader.ReadAllAsync(_cancel.Token).ConfigureAwait(false))
            {
                bool force = Interlocked.Exchange(ref _force, 0) != 0;
                foreach (var value in _projector.Updates(state, Environment.TickCount64, force))
                {
                    try
                    {
                        byte[] packet = OscCodec.Encode(value.Address, value.Arguments);
                        await _udp.SendAsync(packet, _cancel.Token).ConfigureAwait(false);
                        Interlocked.Increment(ref _sent); Volatile.Write(ref _error, null);
                    }
                    catch (SocketException ex) { Volatile.Write(ref _error, ex.Message); break; }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_cancel.IsCancellationRequested) Volatile.Write(ref _error, ex.Message); }
    }
    public void Dispose() { _states.Writer.TryComplete(); _cancel.Cancel(); _udp.Dispose(); _send.GetAwaiter().GetResult(); _cancel.Dispose(); }
}
