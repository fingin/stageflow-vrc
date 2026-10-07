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
    private sealed record LoopAudio(long Start, int Offset, float[] Samples);
    private LoopAudio? _loopAudio;
    private readonly TaskCompletionSource _videoReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _audioReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready { get; }
    private string? _error;
    public MediaInfo Info { get; }
    public string? Error => Volatile.Read(ref _error);
    public long AudioUnderruns => _pcm.Underruns;
    public volatile int AudioOffsetMilliseconds;
    public MediaPlayback(GpuDevice gpu, Transport transport, MediaInfo info)
    {
        _gpu = gpu; _transport = transport; Info = info;
        Ready = Task.WhenAll(_videoReady.Task, _audioReady.Task);
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
                    revision = snapshot.TimelineRevision;
                    seekPosition = TimeSpan.FromSeconds(Math.Clamp(snapshot.Position.TotalSeconds, 0, Math.Max(0, Info.Duration.TotalSeconds - frameSeconds * 0.5)));
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
                _videoReady.TrySetResult();
                sentPaused = true;
            }
        }
        catch (OperationCanceledException) { _videoReady.TrySetCanceled(); }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); _videoReady.TrySetException(ex); }
        finally { _video.Writer.TryComplete(); _videoReady.TrySetCanceled(); }
    }
    private void AudioLoop()
    {
        if (Info.AudioCodec == null) { _audioReady.TrySetResult(); return; }
        try
        {
            using var decoder = new AudioDecoder(Info.Path);
            using var loopDecoder = new AudioDecoder(Info.Path);
            float[] samples = new float[2048];
            long revision = -1, cursor = 0; int oldOffset = int.MinValue;
            while (!_cancel.IsCancellationRequested)
            {
                var snapshot = _transport.Snapshot();
                int offset = AudioOffsetMilliseconds;
                long position = Math.Max(0, (long)(snapshot.Position.TotalSeconds * 48000) + offset * 48L);
                if (snapshot.TimelineRevision != revision || oldOffset != offset || position > cursor + 48000)
                {
                    oldOffset = offset; revision = snapshot.TimelineRevision; cursor = position;
                    decoder.Seek(position); _pcm.Reset(revision, position);
                }
                if (cursor > position + 24000)
                {
                    _audioReady.TrySetResult();
                    // Prime the loop head while normal playback has half a second queued.
                    // This immutable, bounded cache bridges the decoder's seek at wraparound.
                    long loopStart = snapshot.LoopIn.Ticks * 48000 / TimeSpan.TicksPerSecond;
                    var cached = Volatile.Read(ref _loopAudio);
                    if (snapshot.LoopEnabled && (cached == null || cached.Start != loopStart || cached.Offset != offset))
                    {
                        float[] head = new float[48000];
                        loopDecoder.Seek(Math.Max(0, loopStart + offset * 48L));
                        loopDecoder.ReadAt(loopStart + offset * 48L, head);
                        Volatile.Write(ref _loopAudio, new(loopStart, offset, head));
                    }
                    Wait(3); continue;
                }
                decoder.ReadAt(cursor, samples);
                _pcm.Write(revision, cursor, samples); cursor += samples.Length / 2;
            }
        }
        catch (OperationCanceledException) { _audioReady.TrySetCanceled(); }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); _audioReady.TrySetException(ex); }
        finally { _audioReady.TrySetCanceled(); }
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
        long start = snapshot.Position.Ticks * 48000 / TimeSpan.TicksPerSecond;
        long loopStart = snapshot.LoopIn.Ticks * 48000 / TimeSpan.TicksPerSecond;
        long loopEnd = snapshot.LoopOut.Ticks * 48000 / TimeSpan.TicksPerSecond;
        var cache = Volatile.Read(ref _loopAudio);
        while (!stereo.IsEmpty)
        {
            bool looping = snapshot.Mode == ClockMode.Internal && snapshot.LoopEnabled && loopEnd > loopStart;
            if (looping && start >= loopEnd) start = loopStart + (start - loopStart) % (loopEnd - loopStart);
            int frames = stereo.Length / 2;
            if (looping) frames = (int)Math.Min(frames, loopEnd - start);
            var chunk = stereo[..(frames * 2)];
            if (looping && cache != null && cache.Start == loopStart && cache.Offset == offsetMilliseconds &&
                start >= cache.Start && start + frames <= cache.Start + cache.Samples.Length / 2)
                cache.Samples.AsSpan((int)(start - cache.Start) * 2, chunk.Length).CopyTo(chunk);
            else _pcm.Read(snapshot.TimelineRevision, start + offsetMilliseconds * 48L, chunk);
            stereo = stereo[chunk.Length..]; start += frames;
        }
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
