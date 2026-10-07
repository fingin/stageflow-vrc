using System.Diagnostics;
using StageFlow.Core.Timing;

namespace StageFlow.Engine.Midi;

/// <summary>MTC scheduling runs independently of rendering, streaming and the UI.</summary>
public sealed class MtcOutput : IDisposable
{
    private readonly Transport _transport;
    private readonly uint _device;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private readonly int _offsetMilliseconds;
    private string? _error;
    private long _messages, _late;
    public volatile bool Suspended;
    public string? Error => Volatile.Read(ref _error);
    public long Messages => Interlocked.Read(ref _messages);
    public long LateCycles => Interlocked.Read(ref _late);
    public MtcOutput(Transport transport, uint device, int offsetMilliseconds = 0)
    {
        _transport = transport; _device = device; _offsetMilliseconds = offsetMilliseconds;
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow MTC output", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    private void Run()
    {
        WinMidi.timeBeginPeriod(1);
        try
        {
            using var output = new MidiOutput(_device);
            Span<byte> fullFrame = stackalloc byte[10];
            long revision = -1;
            int piece = 0;
            double next = Stopwatch.GetTimestamp();
            Timecode cycle = default;
            while (!_stop.IsSet)
            {
                if (Suspended) { _stop.Wait(2); continue; }
                var snapshot = _transport.Snapshot();
                var tc = snapshot.Timecode.AddFrames(snapshot.Rate.ToFrames(TimeSpan.FromMilliseconds(_offsetMilliseconds)));
                // Incoming clock mode never echoes into the output port, preventing MIDI feedback.
                if (snapshot.Mode != ClockMode.Internal) { _stop.Wait(10); revision = -1; continue; }
                if (snapshot.Revision != revision)
                {
                    MtcCodec.WriteFullFrame(tc, fullFrame); output.SendSysEx(fullFrame);
                    Interlocked.Increment(ref _messages);
                    revision = snapshot.Revision; piece = 0; next = Stopwatch.GetTimestamp();
                }
                if (snapshot.State != TransportState.Playing) { _stop.Wait(2); continue; }
                double interval = Stopwatch.Frequency / (snapshot.Rate.FramesPerSecond() * 4);
                double now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    if (next - now > Stopwatch.Frequency * 0.0015) _stop.Wait(1);
                    else Thread.SpinWait(64);
                    continue;
                }
                if (now - next > interval * 2)
                {
                    // Discard a late cycle rather than bursting stale quarter-frame messages.
                    Interlocked.Increment(ref _late); piece = 0; next = now;
                    MtcCodec.WriteFullFrame(tc, fullFrame); output.SendSysEx(fullFrame);
                }
                if (piece == 0) cycle = tc;
                output.SendShort(0xf1u | ((uint)MtcCodec.QuarterFrame(cycle, piece) << 8));
                Interlocked.Increment(ref _messages);
                piece = (piece + 1) & 7; next += interval;
            }
        }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); }
        finally { WinMidi.timeEndPeriod(1); }
    }
    public void Dispose() { _stop.Set(); _thread.Join(); _stop.Dispose(); }
}
