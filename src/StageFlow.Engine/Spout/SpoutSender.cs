using System.IO.MemoryMappedFiles;
using System.Text;
using StageFlow.Engine.Graphics;
using Vortice.DXGI;

namespace StageFlow.Engine.Spout;

/// <summary>Spout2 Direct3D11 texture sharing; one GPU copy, no pixel readback, no transport process.</summary>
public sealed class SpoutSender : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly GpuSurface _surface;
    private readonly MemoryMappedFile _registry, _metadata;
    private readonly Mutex _access;
    private readonly Semaphore _counter;
    private readonly IDXGIKeyedMutex _keyed;
    private int _disposed;
    public string Name { get; }
    public long SkippedFrames { get; private set; }
    public long SentFrames { get; private set; }
    public SpoutSender(GpuDevice gpu, string name, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(c => c < 32 || c > 126 || c == '\\'))
            throw new ArgumentException("Use a Spout name of 1–200 plain characters without backslashes.");
        _gpu = gpu;
        _surface = new(gpu, width, height, shared: true);
        _keyed = _surface.Texture.QueryInterface<IDXGIKeyedMutex>();
        _registry = MemoryMappedFile.CreateOrOpen(SpoutRegistry.MapName, SpoutRegistry.MaxSenders * 256L);
        using var mutex = new Mutex(false, SpoutRegistry.MapName + "_mutex");
        if (!SpoutRegistry.Enter(mutex, 200)) { _keyed.Dispose(); _surface.Dispose(); _registry.Dispose(); throw new TimeoutException("Spout registry is busy."); }
        MemoryMappedFile? metadata = null;
        try
        {
            using var view = _registry.CreateViewAccessor(); var names = SpoutRegistry.ReadNames(view);
            Name = name; int suffix = 1;
            while (names.Contains(Name)) Name = $"{name} ({suffix++})";
            metadata = MemoryMappedFile.CreateNew(Name, 280);
            using var info = metadata.CreateViewAccessor();
            using var resource = _surface.Texture.QueryInterface<IDXGIResource>();
            info.Write(0, unchecked((uint)(long)resource.SharedHandle)); info.Write(4, width); info.Write(8, height);
            info.Write(12, (uint)_surface.Texture.Description.Format);
            byte[] description = Encoding.Latin1.GetBytes("StageFlow Direct3D11\0"); info.WriteArray(20, description, 0, description.Length);
            _access = new Mutex(false, Name + "_SpoutAccessMutex");
            _counter = new Semaphore(1, int.MaxValue, Name + "_Count_Semaphore");
            names.Add(Name); SpoutRegistry.WriteNames(view, names); _metadata = metadata;
        }
        catch { metadata?.Dispose(); _registry.Dispose(); _keyed.Dispose(); _surface.Dispose(); throw; }
        finally { mutex.ReleaseMutex(); }
    }
    public void Send(GpuSurface surface)
    {
        if (surface.Width != _surface.Width || surface.Height != _surface.Height) throw new ArgumentException("Spout dimensions changed.");
        if (!SpoutRegistry.Enter(_access)) { SkippedFrames++; return; }
        try
        {
            if (!SpoutReceiver.TryAcquireKeyed(_keyed)) { SkippedFrames++; return; }
            try
            {
                _gpu.Context.CopyResource(_surface.Texture, surface.Texture); _gpu.Context.Flush();
                if (_counter.WaitOne(0)) _counter.Release(2);
                SentFrames++;
            }
            finally { _keyed.ReleaseSync(0); }
        }
        finally { _access.ReleaseMutex(); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        using var mutex = new Mutex(false, SpoutRegistry.MapName + "_mutex");
        if (SpoutRegistry.Enter(mutex, 200))
        {
            try { using var view = _registry.CreateViewAccessor(); var names = SpoutRegistry.ReadNames(view); names.Remove(Name); SpoutRegistry.WriteNames(view, names); }
            finally { mutex.ReleaseMutex(); }
        }
        _metadata.Dispose(); _registry.Dispose(); _counter.Dispose(); _access.Dispose(); _keyed.Dispose(); _surface.Dispose();
    }
}
