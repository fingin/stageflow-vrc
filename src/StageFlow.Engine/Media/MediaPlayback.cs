using System.Threading.Channels;
using FFmpeg.AutoGen;
using StageFlow.Core.Timing;
using StageFlow.Engine.Graphics;

namespace StageFlow.Engine.Media;

public readonly unsafe struct QueuedVideoFrame(nint address, TimeSpan position, long revision) : IDisposable
{
    public AVFrame* Native => (AVFrame*)address;
    public TimeSpan Position { get; } = position;
    public long Revision { get; } = revision;
    public void Dispose() { var frame = Native; if (frame != null) ffmpeg.av_frame_free(&frame); }
}

/// <summary>Bounded prefetch keeps file I/O and codec waits off the presentation/encoding thread.</summary>
public sealed class MediaPlayback : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly Transport _transport;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Channel<QueuedVideoFrame> _video = Channel.CreateBounded<QueuedVideoFrame>(
        new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Thread _videoThread, _audioThread;
    private readonly PcmTimelineBuffer _pcm = new(96000);
    private string? _error;
    public MediaInfo Info { get; }
    public string? Error => Volatile.Read(ref _error);
    public long AudioUnderruns => _pcm.Underruns;
    public MediaPlayback(GpuDevice gpu, Transport transport, MediaInfo info)
    {
        _gpu = gpu; _transport = transport; Info = info;
        _videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "StageFlow video decode" };
        _audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "StageFlow audio decode" };
        _videoThread.Start(); _audioThread.Start();
    }
    private unsafe void VideoLoop()
    {
        try
        {
            using var decoder = new GpuVideoDecoder(_gpu, Info.Path);
            long revision = -1;
            bool ended = false, sentPaused = false;
            TimeSpan seekPosition = default;
            double frameSeconds = Info.VideoFps > 0 ? 1 / Info.VideoFps : 1 / 30.0;
            while (!_cancel.IsCancellationRequested)
            {
                var snapshot = _transport.Snapshot();
                if (snapshot.TimelineRevision != revision)
                {
                    revision = snapshot.TimelineRevision; seekPosition = snapshot.Position;
                    decoder.Seek(seekPosition); ended = sentPaused = false;
                }
                if (ended || (snapshot.State != TransportState.Playing && sentPaused)) { Wait(5); continue; }
                if (!decoder.Read()) { ended = true; continue; }
                if (decoder.Position.TotalSeconds + frameSeconds <= seekPosition.TotalSeconds + 0.000001) continue;
                while (!_cancel.IsCancellationRequested && _transport.Snapshot() is var current && current.TimelineRevision == revision &&
                    decoder.Position > current.Position + TimeSpan.FromMilliseconds(150)) Wait(2);
                if (_cancel.IsCancellationRequested) break;
                if (_transport.Snapshot().TimelineRevision != revision) continue;
                var clone = ffmpeg.av_frame_clone(decoder.Frame);
                if (clone == null) throw new OutOfMemoryException();
                var queued = new QueuedVideoFrame((nint)clone, decoder.Position, revision);
                try { _video.Writer.WriteAsync(queued, _cancel.Token).AsTask().GetAwaiter().GetResult(); }
                catch { queued.Dispose(); throw; }
                sentPaused = true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); }
        finally { _video.Writer.TryComplete(); }
    }
    private void AudioLoop()
    {
        if (Info.AudioCodec == null) return;
        try
        {
            using var decoder = new AudioDecoder(Info.Path);
            float[] samples = new float[2048];
            long revision = -1, cursor = 0;
            while (!_cancel.IsCancellationRequested)
            {
                var snapshot = _transport.Snapshot();
                long position = (long)(snapshot.Position.TotalSeconds * 48000);
                if (snapshot.TimelineRevision != revision)
                {
                    revision = snapshot.TimelineRevision; cursor = position;
                    decoder.Seek(position); _pcm.Reset(revision, position);
                }
                if (cursor > position + 24000) { Wait(3); continue; }
                decoder.ReadAt(cursor, samples);
                _pcm.Write(revision, cursor, samples); cursor += samples.Length / 2;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); }
    }
    private void Wait(int milliseconds) => _cancel.Token.WaitHandle.WaitOne(milliseconds);
    public bool TryGetVideo(TransportSnapshot snapshot, out QueuedVideoFrame frame)
    {
        frame = default; bool found = false;
        while (_video.Reader.TryPeek(out var next))
        {
            if (next.Revision != snapshot.TimelineRevision)
            { if (_video.Reader.TryRead(out var obsolete)) obsolete.Dispose(); continue; }
            if (next.Position > snapshot.Position + TimeSpan.FromMilliseconds(2)) break;
            if (!_video.Reader.TryRead(out next)) break;
            if (found) frame.Dispose(); frame = next; found = true;
        }
        return found;
    }
    public void ReadAudio(TransportSnapshot snapshot, Span<float> stereo, int offsetMilliseconds = 0)
    {
        if (snapshot.State != TransportState.Playing || Info.AudioCodec == null) { stereo.Clear(); return; }
        _pcm.Read(snapshot.TimelineRevision, (long)(snapshot.Position.TotalSeconds * 48000) + offsetMilliseconds * 48L, stereo);
    }
    public void Dispose()
    {
        _cancel.Cancel(); _videoThread.Join(); _audioThread.Join();
        while (_video.Reader.TryRead(out var frame)) frame.Dispose();
        _cancel.Dispose();
    }
}

/// <summary>Fixed capacity sample-addressed ring; readers never wait for a decoder.</summary>
internal sealed class PcmTimelineBuffer(int capacity)
{
    private readonly float[] _samples = new float[capacity * 2];
    private readonly object _gate = new();
    private long _revision = -1, _start, _end, _underruns;
    public long Underruns => Interlocked.Read(ref _underruns);
    public void Reset(long revision, long start) { lock (_gate) { _revision = revision; _start = _end = start; } }
    public void Write(long revision, long start, ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            if (revision != _revision) return;
            if (start != _end) _start = _end = start;
            for (int i = 0; i < samples.Length / 2; i++)
            {
                int index = (int)((start + i) % capacity) * 2;
                _samples[index] = samples[2 * i]; _samples[index + 1] = samples[2 * i + 1];
            }
            _end = start + samples.Length / 2; _start = Math.Max(_start, _end - capacity);
        }
    }
    public void Read(long revision, long start, Span<float> samples)
    {
        lock (_gate)
        {
            samples.Clear();
            if (revision != _revision) { Interlocked.Increment(ref _underruns); return; }
            long from = Math.Max(start, _start), to = Math.Min(start + samples.Length / 2, _end);
            if (from > start || to < start + samples.Length / 2) Interlocked.Increment(ref _underruns);
            for (long i = from; i < to; i++)
            {
                int source = (int)(i % capacity) * 2, dest = (int)(i - start) * 2;
                samples[dest] = _samples[source]; samples[dest + 1] = _samples[source + 1];
            }
        }
    }
}
