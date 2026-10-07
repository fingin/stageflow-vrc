using System.Buffers;
using System.Threading.Channels;
using StageFlow.Core.Projects;
using StageFlow.Core.Monitoring;
using StageFlow.Engine.Graphics;
using Vortice.Direct3D11;

namespace StageFlow.Engine.Network;

public sealed class LanFrame
{
    public byte[] Pixels = [];
    public float[] Audio = [];
    public int Width, Height, FpsNum, FpsDen, Samples;
    public long Timestamp, AudioTimestamp;
    private int _references = 1;
    public void Retain() => Interlocked.Increment(ref _references);
    public void Release() { if (Interlocked.Decrement(ref _references) == 0) { ArrayPool<byte>.Shared.Return(Pixels); ArrayPool<float>.Shared.Return(Audio); } }
}
public sealed unsafe class LanVideoOutput : IDisposable
{
    private sealed class Slot(ID3D11Texture2D texture) { public ID3D11Texture2D Texture = texture; public LanFrame? Frame; }
    private sealed class SenderWorker : IDisposable
    {
        private readonly ILanSender _sender;
        private readonly Channel<LanFrame> _frames = Channel.CreateBounded<LanFrame>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
        private readonly Thread _thread;
        public long Sent, Dropped;
        public string? Error;
        public string Status => _sender.Name + (Error == null ? $": {Interlocked.Read(ref Sent)} frames / {Interlocked.Read(ref Dropped)} dropped" : ": " + Error);
        public VideoRouteTelemetry Snapshot() => new(_sender.Protocol, Error != null ? VideoRouteState.Error : Interlocked.Read(ref Sent) > 0 ? VideoRouteState.Live : VideoRouteState.Connecting,
            Frames: Interlocked.Read(ref Sent), Dropped: Interlocked.Read(ref Dropped), Error: Error, Address: _sender.Address);
        public SenderWorker(ILanSender sender) { _sender = sender; _thread = new Thread(Run) { IsBackground = true, Name = sender.Name }; _thread.Start(); }
        public void Submit(LanFrame frame) { frame.Retain(); if (Error != null || !_frames.Writer.TryWrite(frame)) { frame.Release(); Interlocked.Increment(ref Dropped); } }
        private void Run()
        {
            try
            {
                while (_frames.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                    while (_frames.Reader.TryRead(out var frame)) { try { _sender.Send(frame); Interlocked.Increment(ref Sent); } finally { frame.Release(); } }
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { _frames.Writer.TryComplete(); while (_frames.Reader.TryRead(out var frame)) frame.Release(); _sender.Dispose(); }
        }
        public void Dispose() { _frames.Writer.TryComplete(); _thread.Join(); }
    }
    private readonly GpuDevice _gpu;
    private readonly List<Slot> _slots = [];
    private readonly List<SenderWorker> _senders = [];
    private readonly int _width, _height, _fpsNum, _fpsDen;
    private int _cursor;
    private long _dropped;
    public string Status => string.Join(" · ", _senders.Select(s => s.Status)) + $" · GPU skips {_dropped}";
    public VideoRouteTelemetry[] Snapshot() => _senders.Select(s => { var status = s.Snapshot(); return status with { Dropped = status.Dropped + _dropped }; }).ToArray();
    public LanVideoOutput(GpuDevice gpu, GpuSurface canvas, int fpsNum, int fpsDen, LanVideoSettings settings)
    {
        _gpu = gpu; _width = canvas.Width; _height = canvas.Height; _fpsNum = fpsNum; _fpsDen = fpsDen;
        var desc = canvas.Texture.Description; desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None; desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
        try
        {
            for (int i = 0; i < 3; i++) _slots.Add(new(gpu.Device.CreateTexture2D(desc)));
            if (settings.NdiEnabled) _senders.Add(new(new NdiSender(settings.NdiName))); if (settings.OmtEnabled) _senders.Add(new(new OmtSender(settings.OmtName)));
        }
        catch { Dispose(); throw; }
    }
    public void Submit(GpuSurface surface, long tick, ReadOnlySpan<float> stereo, float gain)
    {
        // Read the oldest staging slot without waiting for the GPU. A busy device skips this output only.
        var slot = _slots[_cursor];
        if (slot.Frame is { } ready)
        {
            var result = _gpu.Context.Map(slot.Texture, 0, MapMode.Read, MapFlags.DoNotWait, out var mapped);
            if (result.Failure) { _dropped++; return; }
            try
            {
                fixed (byte* destination = ready.Pixels)
                    for (int y = 0; y < _height; y++) Buffer.MemoryCopy((byte*)mapped.DataPointer + y * mapped.RowPitch, destination + y * _width * 4, _width * 4, _width * 4);
            }
            finally { _gpu.Context.Unmap(slot.Texture, 0); }
            foreach (var sender in _senders) sender.Submit(ready);
            ready.Release(); slot.Frame = null;
        }
        int samples = stereo.Length / 2;
        var frame = new LanFrame { Pixels = ArrayPool<byte>.Shared.Rent(_width * _height * 4), Audio = ArrayPool<float>.Shared.Rent(Math.Max(1, stereo.Length)),
            Width = _width, Height = _height, FpsNum = _fpsNum, FpsDen = _fpsDen, Samples = samples,
            Timestamp = tick * TimeSpan.TicksPerSecond * _fpsDen / _fpsNum,
            AudioTimestamp = (tick + 1) * TimeSpan.TicksPerSecond * _fpsDen / _fpsNum - samples * TimeSpan.TicksPerSecond / 48000 };
        for (int i = 0; i < samples; i++) { frame.Audio[i] = Math.Clamp(stereo[i * 2] * gain, -1, 1); frame.Audio[samples + i] = Math.Clamp(stereo[i * 2 + 1] * gain, -1, 1); }
        _gpu.Context.CopyResource(slot.Texture, surface.Texture); _gpu.Context.Flush(); slot.Frame = frame; _cursor = (_cursor + 1) % _slots.Count;
    }
    public void Dispose() { foreach (var sender in _senders) sender.Dispose(); _senders.Clear(); foreach (var slot in _slots) { slot.Frame?.Release(); slot.Frame = null; slot.Texture.Dispose(); } }
}
