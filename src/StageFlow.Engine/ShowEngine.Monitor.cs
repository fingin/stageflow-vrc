using StageFlow.Core.Monitoring;

namespace StageFlow.Engine;

public sealed partial class ShowEngine
{
    private string? _streamError;
    private long _monitorUpdated;
    private VideoTelemetry? _videoTelemetry;
    // No discovery, network queries or UI work on the render thread. Reuse the
    // immutable snapshot between updates; each sender reports lock-free counters.
    private VideoTelemetry CaptureVideoTelemetry()
    {
        long now = Environment.TickCount64;
        if (_videoTelemetry != null && now - _monitorUpdated < 500) return _videoTelemetry;
        var routes = new List<VideoRouteTelemetry>();
        if (_encoder != null) routes.AddRange(_encoder.NetworkTelemetry());
        if (_server != null && _project.Stream.HlsEnabled)
            routes.Add(new("hls", _segments?.SegmentCount > 0 ? VideoRouteState.Live : VideoRouteState.Waiting,
                Frames: _segments?.SegmentCount ?? 0, AudioPackets: _server.Requests, Bytes: _server.BytesServed));
        if (_spoutOut != null) routes.Add(new("spout", VideoRouteState.Live, Frames: _spoutOut.SentFrames, Dropped: _spoutOut.SkippedFrames, Address: _spoutOut.Name));
        if (_lanOutput != null) routes.AddRange(_lanOutput.Snapshot());
        foreach (var input in _networkInputs) routes.Add(input.Value.Snapshot(input.Key));
        foreach (var input in _spoutInputs)
            routes.Add(new(input.Key.ToString(), input.Value.Error != null ? VideoRouteState.Waiting : input.Value.HasFrame ? VideoRouteState.Live : VideoRouteState.Connecting,
                Error: input.Value.Error, Width: input.Value.Width, Height: input.Value.Height));
        _monitorUpdated = now;
        return _videoTelemetry = new(_encoder != null, _streamError, _encoder?.VideoFrames ?? 0, _encoder?.AudioSamples ?? 0, routes.ToArray(),
            Playing: _lastState == StageFlow.Core.Timing.TransportState.Playing);
    }
}
