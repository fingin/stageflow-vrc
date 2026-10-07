using System.Diagnostics;

namespace StageFlow.Core.Timing;

public interface IMonotonicClock { long Ticks { get; } long Frequency { get; } }
public sealed class SystemMonotonicClock : IMonotonicClock
{
    public long Ticks => Stopwatch.GetTimestamp();
    public long Frequency => Stopwatch.Frequency;
}
public enum ClockMode { Internal, IncomingMtc }
public enum TransportState { Stopped, Paused, Playing, WaitingForSync, SyncLost }
public readonly record struct TransportSnapshot(TimeSpan Position, TimeSpan Duration, TransportState State,
    ClockMode Mode, MtcRate Rate, Timecode Timecode, long Revision, long TimelineRevision, bool LoopEnabled, TimeSpan LoopIn, TimeSpan LoopOut);

/// <summary>One coherent clock for video, audio, overlays and MTC. No UI dependency.</summary>
public sealed class Transport(IMonotonicClock? clock = null)
{
    private readonly object _gate = new();
    private readonly IMonotonicClock _clock = clock ?? new SystemMonotonicClock();
    private long _anchor, _positionTicks, _durationTicks, _revision, _offsetFrames;
    private long _timelineRevision;
    private long _loopIn, _loopOut, _lastMtc;
    private bool _loop;
    private MtcRate _rate = MtcRate.Fps30;
    private ClockMode _mode;
    private TransportState _state = TransportState.Stopped;
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    private long NowPosition(long now)
    {
        if (_state != TransportState.Playing) return _positionTicks;
        long elapsed = checked((long)((Int128)(now - _anchor) * TimeSpan.TicksPerSecond / _clock.Frequency));
        long position = _positionTicks + elapsed;
        if (_mode == ClockMode.IncomingMtc && now - _lastMtc > SyncTimeout.TotalSeconds * _clock.Frequency)
        {
            // Stop at the last extrapolated timeout, not at an arbitrarily late UI poll.
            position = _positionTicks + SyncTimeout.Ticks;
            _state = TransportState.SyncLost;
            _positionTicks = Math.Clamp(position, 0, _durationTicks);
            _revision++;
            return _positionTicks;
        }
        if (_mode == ClockMode.Internal && _loop && position >= _loopOut)
        {
            position = _loopIn + (position - _loopIn) % (_loopOut - _loopIn);
            _positionTicks = position; _anchor = now; _revision++; _timelineRevision++;
        }
        if (position >= _durationTicks)
        {
            position = _durationTicks; _positionTicks = position;
            _state = TransportState.Paused; _revision++;
        }
        return Math.Clamp(position, 0, _durationTicks);
    }

    public TransportSnapshot Snapshot()
    {
        lock (_gate)
        {
            long position = NowPosition(_clock.Ticks);
            var time = TimeSpan.FromTicks(position);
            return new(time, TimeSpan.FromTicks(_durationTicks), _state, _mode, _rate,
                Timecode.FromFrameNumber(_rate.ToFrames(time) + _offsetFrames, _rate), _revision, _timelineRevision,
                _loop, TimeSpan.FromTicks(_loopIn), TimeSpan.FromTicks(_loopOut));
        }
    }

    public void Load(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_gate)
        {
            _durationTicks = duration.Ticks; _positionTicks = 0; _loop = false;
            _state = _mode == ClockMode.Internal ? TransportState.Paused : TransportState.WaitingForSync;
            _anchor = _clock.Ticks; _revision++; _timelineRevision++;
        }
    }
    public void ConfigureClock(ClockMode mode, MtcRate rate, long offsetFrames)
    {
        _ = rate.Nominal();
        lock (_gate)
        {
            _positionTicks = NowPosition(_clock.Ticks); _mode = mode; _rate = rate; _offsetFrames = offsetFrames;
            _state = mode == ClockMode.Internal ? TransportState.Paused : TransportState.WaitingForSync;
            _revision++; _timelineRevision++;
        }
    }
    public void Play()
    {
        lock (_gate)
        {
            if (_mode != ClockMode.Internal) return;
            if (_state == TransportState.Playing || _durationTicks == 0) return;
            if (_positionTicks >= _durationTicks) { _positionTicks = _loop ? _loopIn : 0; _timelineRevision++; }
            _anchor = _clock.Ticks; _state = TransportState.Playing; _revision++;
        }
    }
    public void Pause()
    {
        lock (_gate)
        {
            if (_mode != ClockMode.Internal) return;
            _positionTicks = NowPosition(_clock.Ticks); _state = TransportState.Paused; _revision++;
        }
    }
    public void Stop()
    {
        lock (_gate)
        {
            _positionTicks = 0; _state = _mode == ClockMode.Internal ? TransportState.Stopped : TransportState.WaitingForSync;
            _revision++; _timelineRevision++;
        }
    }
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_mode != ClockMode.Internal) return;
            _positionTicks = Math.Clamp(position.Ticks, 0, _durationTicks); _anchor = _clock.Ticks; _revision++; _timelineRevision++;
        }
    }
    public void Step(int frames)
    { lock (_gate) Step(frames, _rate.Numerator(), _rate.Denominator()); }
    public void Step(int frames, int fpsNumerator, int fpsDenominator)
    {
        if (fpsNumerator <= 0 || fpsDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(fpsNumerator));
        lock (_gate)
        {
            if (_mode != ClockMode.Internal) return;
            long frame = (long)((Int128)NowPosition(_clock.Ticks) * fpsNumerator / (TimeSpan.TicksPerSecond * (long)fpsDenominator));
            // Ceiling a frame boundary prevents 29.97/24 fps integer tick truncation stepping back a frame.
            Int128 n = (Int128)(frame + frames) * TimeSpan.TicksPerSecond * fpsDenominator;
            long ticks = checked((long)((n + fpsNumerator - 1) / fpsNumerator));
            _positionTicks = Math.Clamp(ticks, 0, _durationTicks);
            _state = TransportState.Paused; _anchor = _clock.Ticks; _revision++; _timelineRevision++;
        }
    }
    public void SetLoop(bool enabled, TimeSpan start, TimeSpan end)
    {
        lock (_gate)
        {
            if (enabled && (start < TimeSpan.Zero || end <= start || end.Ticks > _durationTicks))
                throw new ArgumentException("Loop requires 0 <= in < out <= duration.");
            _loop = enabled; _loopIn = start.Ticks; _loopOut = end.Ticks;
        }
    }
    public void ReceiveMtc(Timecode incoming, bool running)
    {
        if (!incoming.IsValid) return;
        lock (_gate)
        {
            if (_mode != ClockMode.IncomingMtc) return;
            var now = _clock.Ticks;
            long expected = NowPosition(now);
            _rate = incoming.Rate;
            long frames = incoming.ToFrameNumber() - _offsetFrames;
            _positionTicks = Math.Clamp(_rate.ToTime(frames).Ticks, 0, _durationTicks);
            if (!running || Math.Abs(_positionTicks - expected) > TimeSpan.FromMilliseconds(100).Ticks ||
                _state != TransportState.Playing) { _revision++; _timelineRevision++; }
            _anchor = _lastMtc = now;
            _state = running ? TransportState.Playing : TransportState.Paused;
        }
    }
}
