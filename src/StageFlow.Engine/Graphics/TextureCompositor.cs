using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace StageFlow.Engine.Graphics;

public sealed class TextureCompositor : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _linear, _point;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _raster;
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawConstants
    {
        public Vector4 Rect;
        public Vector4 Uv;
        public Vector2 Canvas;
        public float Opacity, Premultiplied;
    }
    private const string Shader = """
        cbuffer Draw : register(b0) { float4 rect; float4 uv; float2 canvas; float opacity; float premultiplied; };
        Texture2D image : register(t0); SamplerState imageSampler : register(s0);
        struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        V VS(uint id : SV_VertexID) {
            float2 corners[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
            float2 corner = corners[id]; V o;
            float2 xy = (rect.xy + corner * rect.zw) / canvas;
            o.pos = float4(xy.x * 2 - 1, 1 - xy.y * 2, 0, 1);
            o.uv = uv.xy + corner * uv.zw; return o;
        }
        float4 PS(V i) : SV_Target {
            float4 c = image.Sample(imageSampler, i.uv);
            c.rgb *= (premultiplied > 0 ? 1 : c.a) * opacity;
            c.a *= opacity; return c;
        }
        """;
    public TextureCompositor(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertex = gpu.Device.CreateVertexShader(Compiler.Compile(Shader, "VS", "Composite.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3).Span);
        _pixel = gpu.Device.CreatePixelShader(Compiler.Compile(Shader, "PS", "Composite.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3).Span);
        _constants = gpu.Device.CreateBuffer(48, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _linear = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _point = gpu.Device.CreateSamplerState(SamplerDescription.PointClamp);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _raster = gpu.Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
    }
    public void Draw(GpuSurface target, ID3D11ShaderResourceView image, Vector4 rect, Vector4 uv,
        float opacity = 1, bool point = false, bool premultiplied = false)
        => Draw(target.Target, target.Width, target.Height, image, rect, uv, opacity, point, premultiplied);
    public void Draw(ID3D11RenderTargetView target, int width, int height, ID3D11ShaderResourceView image, Vector4 rect, Vector4 uv,
        float opacity = 1, bool point = false, bool premultiplied = false)
    {
        var constants = new DrawConstants { Rect = rect, Uv = uv, Canvas = new(width, height),
            Opacity = Math.Clamp(opacity, 0, 1), Premultiplied = premultiplied ? 1 : 0 };
        var c = _gpu.Context;
        var map = c.Map(_constants, 0, MapMode.WriteDiscard); map.AsSpan<DrawConstants>(1)[0] = constants; c.Unmap(_constants, 0);
        c.OMSetRenderTargets(target); c.RSSetViewport(new Viewport(width, height));
        c.RSSetState(_raster); c.OMSetBlendState(_blend);
        c.IASetInputLayout(null); c.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        c.VSSetShader(_vertex); c.PSSetShader(_pixel); c.VSSetConstantBuffer(0, _constants); c.PSSetConstantBuffer(0, _constants);
        c.PSSetShaderResource(0, image); c.PSSetSampler(0, point ? _point : _linear);
        c.Draw(6, 0); c.PSSetShaderResource(0, null!);
    }
    public void Dispose()
    { _raster.Dispose(); _blend.Dispose(); _point.Dispose(); _linear.Dispose(); _constants.Dispose(); _pixel.Dispose(); _vertex.Dispose(); }
}
