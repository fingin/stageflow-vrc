using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StageFlow.Engine.Graphics;

public sealed class GpuSurface : IDisposable
{
    public ID3D11Texture2D Texture { get; }
    public ID3D11RenderTargetView Target { get; }
    public ID3D11ShaderResourceView View { get; }
    public int Width { get; }
    public int Height { get; }
    public GpuSurface(GpuDevice gpu, int width, int height, bool shared = false)
    {
        Width = width; Height = height;
        Texture = gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = SampleDescription.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = shared ? ResourceOptionFlags.SharedKeyedMutex : ResourceOptionFlags.None
        });
        Target = gpu.Device.CreateRenderTargetView(Texture);
        View = gpu.Device.CreateShaderResourceView(Texture);
    }
    public void Dispose() { View.Dispose(); Target.Dispose(); Texture.Dispose(); }
}
