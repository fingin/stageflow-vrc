using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace StageFlow.Engine.Graphics;

public sealed record GpuAdapter(uint Index, string Name, ulong DedicatedBytes, long Luid);

/// <summary>One hardware device shared by decode, compositor, Spout and encoder.</summary>
public sealed class GpuDevice : IDisposable
{
    public IDXGIFactory2 Factory { get; }
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public GpuAdapter Adapter { get; }
    public static IReadOnlyList<GpuAdapter> EnumerateAdapters()
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory2>();
        var result = new List<GpuAdapter>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var desc = adapter.Description1;
                if ((desc.Flags & AdapterFlags.Software) == 0)
                    result.Add(new(i, desc.Description, (ulong)desc.DedicatedVideoMemory, desc.Luid));
            }
        }
        return result;
    }
    public GpuDevice(uint? adapterIndex = null)
    {
        var adapters = EnumerateAdapters();
        Adapter = adapterIndex.HasValue ? adapters.First(a => a.Index == adapterIndex.Value) :
            adapters.OrderByDescending(a => a.DedicatedBytes).FirstOrDefault() ?? throw new InvalidOperationException("No hardware GPU found.");
        Factory = CreateDXGIFactory1<IDXGIFactory2>();
        Factory.EnumAdapters1(Adapter.Index, out var adapter).CheckError();
        using (adapter)
        {
            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var device, out _, out var context).CheckError();
            Device = device; Context = context;
        }
        using var multithread = Context.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
    }
    public void Dispose()
    {
        Context.ClearState(); Context.Flush(); Context.Dispose(); Device.Dispose(); Factory.Dispose();
    }
}
