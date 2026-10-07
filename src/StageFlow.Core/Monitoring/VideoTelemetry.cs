namespace StageFlow.Core.Monitoring;

public enum VideoRouteState { Disabled, Idle, Connecting, Live, Waiting, Stalled, Error, Hidden, NotSelected }
public sealed record VideoRouteTelemetry(string Id, VideoRouteState State, long Frames = 0, long AudioPackets = 0,
    long Bytes = 0, int Queued = 0, long Dropped = 0, string? Error = null, string? Address = null, int Width = 0, int Height = 0);
// Excludes destination configuration. Presentation code must redact native error text.
public sealed record VideoTelemetry(bool EncoderRunning, string? EncoderError, long EncodedFrames, long EncodedAudioSamples,
    IReadOnlyList<VideoRouteTelemetry> Routes, string? FatalError = null, bool Playing = false);
