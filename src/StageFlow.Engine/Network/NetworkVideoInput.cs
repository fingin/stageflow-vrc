using System.Buffers;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using StageFlow.Core.Projects;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Media;

namespace StageFlow.Engine.Network;

/// <summary>Network capture runs independently. Rendering consumes only the most recent complete frame.</summary>
public sealed unsafe class NetworkVideoInput : IDisposable
{
    private sealed class Pixels(byte[] bytes, int width, int height) { public byte[] Bytes = bytes; public int Width = width, Height = height; }
    private readonly GpuDevice _gpu;
    private readonly Thread _thread;
    private readonly GpuVideoConverter? _converter;
    private volatile bool _cancel;
    private Pixels? _latest;
    private nint _latestGpu;
    private GpuSurface? _surface;
    private readonly object _audioGate = new();
    private readonly float[] _audio = new float[96000];
    private int _audioRead, _audioCount;
    public NetworkSourceProtocol Protocol { get; }
    public string Source { get; }
    public string? Error { get; private set; }
    public long Frames { get; private set; }
    public NetworkVideoInput(GpuDevice gpu, NetworkSourceProtocol protocol, string source)
    {
        _gpu = gpu; Protocol = protocol; Source = source;
        if (protocol == NetworkSourceProtocol.Srt) _converter = new(gpu);
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow " + protocol + " input" }; _thread.Start();
    }
    private void Run()
    {
        while (!_cancel)
        {
            try { if (Protocol == NetworkSourceProtocol.Srt) RunSrt(); else if (Protocol == NetworkSourceProtocol.Omt) RunOmt(); else RunNdi(); }
            catch (Exception ex) { if (!_cancel) Error = ex.Message; }
            if (!_cancel) Error ??= "Source ended; reconnecting";
            for (int i = 0; i < 20 && !_cancel; i++) Thread.Sleep(100);
        }
    }
    private void RunSrt()
    {
        using var decoder = new GpuVideoDecoder(_gpu, Source, () => _cancel, PushInterleaved);
        while (!_cancel && decoder.Read())
        {
            var frame = ffmpeg.av_frame_clone(decoder.Frame); if (frame == null) throw new OutOfMemoryException();
            nint old = Interlocked.Exchange(ref _latestGpu, (nint)frame); if (old != 0) { var previous = (AVFrame*)old; ffmpeg.av_frame_free(&previous); }
            Frames++; Error = null;
        }
    }
    private void RunOmt()
    {
        nint receiver = OmtNative.omt_receive_create(Source, 6, 2, 0); if (receiver == 0) throw new IOException("OMT input could not start.");
        try
        {
            while (!_cancel)
            {
                var video = OmtNative.omt_receive(receiver, 2, 15);
                if (video != null && video->Type == 2) Publish(video->Data, video->Width, video->Height, video->Stride, video->Codec == 0x58524742);
                var audio = OmtNative.omt_receive(receiver, 4, 0);
                if (audio != null && audio->Type == 4) PushAudio((float*)audio->Data, audio->SampleRate, audio->Channels, audio->SamplesPerChannel, audio->SamplesPerChannel * 4);
            }
        }
        finally { OmtNative.omt_receive_destroy(receiver); }
    }
    private void RunNdi()
    {
        if (!NdiNative.NDIlib_initialize()) throw new IOException("NDI initialization failed.");
        nint name = Marshal.StringToCoTaskMemUTF8(Source), receiver;
        try
        {
            var create = new NdiNative.ReceiveCreate { Source = new() { Name = name }, ColorFormat = 0, Bandwidth = 100 };
            receiver = NdiNative.NDIlib_recv_create_v3(ref create);
        }
        finally { Marshal.FreeCoTaskMem(name); }
        if (receiver == 0) throw new IOException("NDI input could not start.");
        try
        {
            while (!_cancel)
            {
                NdiNative.VideoFrame video; NdiNative.AudioFrame audio;
                int type = NdiNative.NDIlib_recv_capture_v2(receiver, &video, &audio, 0, 25);
                if (type == 1)
                {
                    try { if (video.FourCC is 0x41524742 or 0x58524742) Publish(video.Data, video.Width, video.Height, video.Stride, video.FourCC == 0x58524742); }
                    finally { NdiNative.NDIlib_recv_free_video_v2(receiver, ref video); }
                }
                if (type == 2)
                {
                    try { PushAudio((float*)audio.Data, audio.SampleRate, audio.Channels, audio.Samples, audio.Stride); }
                    finally { NdiNative.NDIlib_recv_free_audio_v2(receiver, ref audio); }
                }
                if (type == -1) throw new IOException("NDI source connection failed.");
            }
        }
        finally { NdiNative.NDIlib_recv_destroy(receiver); }
    }
    private void Publish(nint data, int width, int height, int stride, bool opaque)
    {
        if (data == 0 || width is < 16 or > 8192 || height is < 16 or > 8192 || stride < width * 4) return;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
        fixed (byte* target = bytes)
            for (int y = 0; y < height; y++) Buffer.MemoryCopy((byte*)data + y * stride, target + y * width * 4, width * 4, width * 4);
        if (opaque) for (int i = 3; i < width * height * 4; i += 4) bytes[i] = 255;
        var old = Interlocked.Exchange(ref _latest, new(bytes, width, height)); if (old != null) ArrayPool<byte>.Shared.Return(old.Bytes);
        Frames++; Error = null;
    }
    private void PushAudio(float* data, int rate, int channels, int samples, int stride)
    {
        if (data == null || rate is < 8000 or > 192000 || channels is < 1 or > 32 || samples is < 1 or > 192000 || stride < samples * 4) return;
        int output = Math.Min(48000, (int)((long)samples * 48000 / rate));
        lock (_audioGate)
        {
            for (int i = 0; i < output; i++)
            {
                if (_audioCount > _audio.Length - 2) { _audioRead = (_audioRead + 2) % _audio.Length; _audioCount -= 2; }
                int sample = Math.Min(samples - 1, (int)((long)i * rate / 48000));
                _audio[(_audioRead + _audioCount) % _audio.Length] = data[sample];
                _audio[(_audioRead + _audioCount + 1) % _audio.Length] = channels == 1 ? data[sample] : ((float*)((byte*)data + stride))[sample]; _audioCount += 2;
            }
        }
    }
    public void MixAudio(Span<float> stereo, float gain)
    {
        lock (_audioGate)
        {
            if (_audioCount > stereo.Length * 4) { int discard = _audioCount - stereo.Length * 2; _audioRead = (_audioRead + discard) % _audio.Length; _audioCount -= discard; }
            int count = Math.Min(_audioCount, stereo.Length);
            for (int i = 0; i < count; i++) stereo[i] = Math.Clamp(stereo[i] + _audio[(_audioRead + i) % _audio.Length] * gain, -1, 1);
            _audioRead = (_audioRead + count) % _audio.Length; _audioCount -= count;
        }
    }
    private void PushInterleaved(ReadOnlySpan<float> samples)
    {
        lock (_audioGate)
        {
            foreach (float value in samples)
            {
                if (_audioCount == _audio.Length) { _audioRead = (_audioRead + 1) % _audio.Length; _audioCount--; }
                _audio[(_audioRead + _audioCount) % _audio.Length] = value; _audioCount++;
            }
        }
    }
    public GpuSurface? GetFrame()
    {
        if (_converter != null)
        {
            nint address = Interlocked.Exchange(ref _latestGpu, 0);
            if (address != 0) { var frame = (AVFrame*)address; try { _converter.Convert(frame); } finally { ffmpeg.av_frame_free(&frame); } }
            return _converter.Output;
        }
        var pixels = Interlocked.Exchange(ref _latest, null);
        if (pixels != null)
        {
            try
            {
                if (_surface == null || _surface.Width != pixels.Width || _surface.Height != pixels.Height)
                { _surface?.Dispose(); _surface = new(_gpu, pixels.Width, pixels.Height); }
                fixed (byte* data = pixels.Bytes) _gpu.Context.UpdateSubresource(_surface.Texture, 0, null, (nint)data, (uint)pixels.Width * 4, 0);
            }
            finally { ArrayPool<byte>.Shared.Return(pixels.Bytes); }
        }
        return _surface;
    }
    public void Dispose()
    {
        _cancel = true; _thread.Join();
        var latest = Interlocked.Exchange(ref _latest, null); if (latest != null) ArrayPool<byte>.Shared.Return(latest.Bytes);
        var frame = (AVFrame*)Interlocked.Exchange(ref _latestGpu, 0); if (frame != null) ffmpeg.av_frame_free(&frame);
        _surface?.Dispose(); _converter?.Dispose();
    }
}
