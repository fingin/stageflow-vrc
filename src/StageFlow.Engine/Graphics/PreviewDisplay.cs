using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using StageFlow.Core.Projects;
using System.Numerics;

namespace StageFlow.Engine.Graphics;

public sealed class PreviewDisplay : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly IDXGISwapChain1 _swapChain;
    private ID3D11Texture2D _buffer;
    private ID3D11RenderTargetView _target;
    private int _width, _height;
    public PreviewDisplay(GpuDevice gpu, nint hwnd, int width, int height)
    {
        _gpu = gpu; _width = Math.Max(1, width); _height = Math.Max(1, height);
        _swapChain = gpu.Factory.CreateSwapChainForHwnd(gpu.Device, hwnd, new SwapChainDescription1
        {
            Width = (uint)_width, Height = (uint)_height, Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = SampleDescription.Default,
            Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore
        });
        gpu.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        _buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0); _target = gpu.Device.CreateRenderTargetView(_buffer);
    }
    public void Resize(int width, int height)
    {
        if (width < 1 || height < 1 || (width == _width && height == _height)) return;
        _gpu.Context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        _target.Dispose(); _buffer.Dispose();
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        _width = width; _height = height;
        _buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0); _target = _gpu.Device.CreateRenderTargetView(_buffer);
    }
    public void Present(TextureCompositor compositor, GpuSurface source, Layer? selected = null)
    {
        float scale = Math.Min((float)_width / source.Width, (float)_height / source.Height);
        float width = source.Width * scale, height = source.Height * scale;
        _gpu.Context.ClearRenderTargetView(_target, new Color4(0, 0, 0, 1));
        compositor.Draw(_target, _width, _height, source.View, new((_width - width) / 2, (_height - height) / 2, width, height), new(0, 0, 1, 1));
        if (selected != null)
        {
            float x = (_width - width) / 2 + (float)selected.X * scale, y = (_height - height) / 2 + (float)selected.Y * scale;
            float w = (float)selected.Width * scale, h = (float)selected.Height * scale;
            Vector4 color = selected.Locked ? new(0.65f, 0.7f, 0.8f, 1) : new(0.44f, 0.84f, 0.73f, 1);
            compositor.Fill(_target, _width, _height, new(x, y, w, 2), color);
            compositor.Fill(_target, _width, _height, new(x, y + h - 2, w, 2), color);
            compositor.Fill(_target, _width, _height, new(x, y, 2, h), color);
            compositor.Fill(_target, _width, _height, new(x + w - 2, y, 2, h), color);
            if (!selected.Locked && selected.Kind != LayerKind.Grid) compositor.Fill(_target, _width, _height, new(x + w - 7, y + h - 7, 10, 10), color);
        }
        var result = _swapChain.Present(0, PresentFlags.DoNotWait);
        if (result.Failure && result != Vortice.DXGI.ResultCode.WasStillDrawing) result.CheckError();
    }
    public void Dispose() { _target.Dispose(); _buffer.Dispose(); _swapChain.Dispose(); }
}
