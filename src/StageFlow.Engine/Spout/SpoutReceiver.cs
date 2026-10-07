using StageFlow.Engine.Graphics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StageFlow.Engine.Spout;

public sealed class SpoutReceiver(GpuDevice gpu, string sender) : IDisposable
{
    private ID3D11Texture2D? _shared, _copy;
    private ID3D11ShaderResourceView? _view;
    private IDXGIKeyedMutex? _keyed;
    private Mutex? _access;
    private uint _handle;
    private long _refresh;
    private bool _hasFrame;
    public string Sender { get; } = sender;
    public string? Error { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public ID3D11ShaderResourceView? Receive()
    {
        if (Environment.TickCount64 >= _refresh)
        {
            _refresh = Environment.TickCount64 + 500;
            try
            {
                var info = SpoutRegistry.ReadInfo(Sender);
                if (info is not { Handle: not 0 } metadata) { Disconnect(); Error = $"Waiting for Spout sender '{Sender}'."; return null; }
                if (_shared == null || _handle != metadata.Handle || Width != metadata.Width || Height != metadata.Height)
                {
                    Disconnect();
                    if (metadata.Width is < 1 or > 16384 || metadata.Height is < 1 or > 16384) throw new InvalidDataException("Invalid Spout dimensions.");
                    _shared = gpu.Device.OpenSharedResource<ID3D11Texture2D>(unchecked((nint)(int)metadata.Handle));
                    var description = _shared.Description;
                    description.MiscFlags = ResourceOptionFlags.None; description.BindFlags = BindFlags.ShaderResource;
                    _copy = gpu.Device.CreateTexture2D(description); _view = gpu.Device.CreateShaderResourceView(_copy);
                    if (((uint)_shared.Description.MiscFlags & 0x100) != 0) _keyed = _shared.QueryInterface<IDXGIKeyedMutex>();
                    _access = new Mutex(false, Sender + "_SpoutAccessMutex");
                    _handle = metadata.Handle; Width = metadata.Width; Height = metadata.Height; Error = null;
                }
            }
            catch (Exception ex) { Disconnect(); Error = $"Spout '{Sender}': {ex.Message} Ensure both applications use the same GPU."; return null; }
        }
        if (_shared == null) return null;
        bool acquired = _keyed != null ? TryAcquireKeyed(_keyed) : SpoutRegistry.Enter(_access!);
        if (!acquired) return _hasFrame ? _view : null;
        try { gpu.Context.CopyResource(_copy!, _shared); gpu.Context.Flush(); _hasFrame = true; }
        finally { if (_keyed != null) _keyed.ReleaseSync(0); else _access!.ReleaseMutex(); }
        return _view;
    }
    private void Disconnect()
    {
        _view?.Dispose(); _view = null; _copy?.Dispose(); _copy = null; _keyed?.Dispose(); _keyed = null;
        _shared?.Dispose(); _shared = null; _access?.Dispose(); _access = null; _handle = 0; Width = Height = 0; _hasFrame = false;
    }
    internal static unsafe bool TryAcquireKeyed(IDXGIKeyedMutex mutex)
    {
        // IDXGIKeyedMutex::AcquireSync is slot 8 of the stable COM ABI. Preserve
        // WAIT_TIMEOUT (a positive HRESULT); the generated void wrapper discards it.
        nint pointer = mutex.NativePointer;
        var acquire = (delegate* unmanaged[Stdcall]<nint, ulong, uint, int>)(*(nint**)pointer)[8];
        return acquire(pointer, 0, 0) == 0;
    }
    public void Dispose() => Disconnect();
}
