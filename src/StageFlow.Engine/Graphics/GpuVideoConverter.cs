using System.Numerics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace StageFlow.Engine.Graphics;

/// <summary>Copies a decoder array slice on GPU and converts NV12/P010 to BGRA with a shader.</summary>
public sealed unsafe class GpuVideoConverter : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11RasterizerState _raster;
    private ID3D11Texture2D? _planar;
    private ID3D11ShaderResourceView? _y, _uv;
    private int _width, _height;
    private Format _format;
    public GpuSurface? Output { get; private set; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ColorConstants { public Vector4 Red, Green, Blue, Offsets, UvScale; }
    private const string Shader = """
        cbuffer Conversion : register(b0) { float4 r; float4 g; float4 b; float4 offsets; float4 uvScale; };
        Texture2D luma : register(t0); Texture2D chroma : register(t1); SamplerState sampleState : register(s0);
        struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        V VS(uint id : SV_VertexID) {
            V o; o.uv = float2(id == 2 ? 2 : 0, id == 1 ? 2 : 0);
            o.pos = float4(o.uv.x * 2 - 1, 1 - o.uv.y * 2, 0, 1); return o;
        }
        float4 PS(V i) : SV_Target {
            float2 uv = i.uv * uvScale.xy;
            float3 yuv = float3(luma.Sample(sampleState, uv).r, chroma.Sample(sampleState, uv).rg) + offsets.xyz;
            return float4(saturate(float3(dot(yuv,r.xyz), dot(yuv,g.xyz), dot(yuv,b.xyz))), 1);
        }
        """;
    public GpuVideoConverter(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertex = gpu.Device.CreateVertexShader(Compiler.Compile(Shader, "VS", "Yuv.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3).Span);
        _pixel = gpu.Device.CreatePixelShader(Compiler.Compile(Shader, "PS", "Yuv.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3).Span);
        _constants = gpu.Device.CreateBuffer(80, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _raster = gpu.Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
    }
    public GpuSurface Convert(AVFrame* frame)
    {
        if (frame->format != (int)AVPixelFormat.AV_PIX_FMT_D3D11) throw new ArgumentException("A D3D11 frame is required.");
        nint pointer = (nint)frame->data[0]; Marshal.AddRef(pointer);
        using var source = new ID3D11Texture2D(pointer);
        var description = source.Description;
        if (description.Format is not (Format.NV12 or Format.P010)) throw new NotSupportedException($"GPU video format {description.Format} is not supported.");
        // HDR requires tone mapping; never silently process PQ/HLG as SDR.
        if (frame->color_trc is AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 or AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67)
            throw new NotSupportedException("HDR media needs an explicit tone mapping path. Use an SDR source for this build.");
        if (_planar == null || _width != frame->width || _height != frame->height || _format != description.Format)
        {
            ReleaseTextures();
            _width = frame->width; _height = frame->height; _format = description.Format;
            // Keep coded dimensions for full subresource copies (decoder surfaces may have padding).
            description.ArraySize = 1; description.MipLevels = 1; description.BindFlags = BindFlags.ShaderResource;
            description.MiscFlags = ResourceOptionFlags.None; description.CPUAccessFlags = CpuAccessFlags.None;
            _planar = _gpu.Device.CreateTexture2D(description);
            _y = _gpu.Device.CreateShaderResourceView(_planar, new ShaderResourceViewDescription
            { Format = _format == Format.NV12 ? Format.R8_UNorm : Format.R16_UNorm, ViewDimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1 } });
            _uv = _gpu.Device.CreateShaderResourceView(_planar, new ShaderResourceViewDescription
            { Format = _format == Format.NV12 ? Format.R8G8_UNorm : Format.R16G16_UNorm, ViewDimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1 } });
            Output = new GpuSurface(_gpu, _width, _height);
        }
        var c = _gpu.Context;
        c.CopySubresourceRegion(_planar!, 0, 0, 0, 0, source, (uint)(nint)frame->data[1]);
        bool full = frame->color_range == AVColorRange.AVCOL_RANGE_JPEG;
        bool bt709 = frame->colorspace == AVColorSpace.AVCOL_SPC_BT709 ||
            (frame->colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED && _height > 576);
        bool tenBit = _format == Format.P010;
        float y = tenBit ? 65535f / (64 * (full ? 1023 : 876)) : full ? 1 : 255f / 219;
        float chroma = tenBit ? 65535f / (64 * (full ? 1023 : 896)) : full ? 1 : 255f / 224;
        bool bt2020 = frame->colorspace == AVColorSpace.AVCOL_SPC_BT2020_NCL;
        float kr = bt2020 ? 0.2627f : bt709 ? 0.2126f : 0.299f, kb = bt2020 ? 0.0593f : bt709 ? 0.0722f : 0.114f, kg = 1 - kr - kb;
        var constants = new ColorConstants
        {
            Red = new(y, 0, 2 * (1 - kr) * chroma, 0),
            Green = new(y, -2 * kb * (1 - kb) / kg * chroma, -2 * kr * (1 - kr) / kg * chroma, 0),
            Blue = new(y, 2 * (1 - kb) * chroma, 0, 0),
            Offsets = new(full ? 0 : tenBit ? -4096f / 65535 : -16f / 255,
                tenBit ? -32768f / 65535 : -128f / 255, tenBit ? -32768f / 65535 : -128f / 255, 0),
            UvScale = new((float)_width / description.Width, (float)_height / description.Height, 0, 0)
        };
        var map = c.Map(_constants, 0, MapMode.WriteDiscard); map.AsSpan<ColorConstants>(1)[0] = constants; c.Unmap(_constants, 0);
        c.OMSetRenderTargets(Output!.Target); c.RSSetViewport(new Viewport(Output.Width, Output.Height));
        c.RSSetState(_raster); c.OMSetBlendState(null); c.IASetInputLayout(null); c.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        c.VSSetShader(_vertex); c.PSSetShader(_pixel); c.PSSetConstantBuffer(0, _constants);
        c.PSSetSampler(0, _sampler); c.PSSetShaderResource(0, _y!); c.PSSetShaderResource(1, _uv!);
        c.Draw(3, 0); c.PSSetShaderResource(0, null!); c.PSSetShaderResource(1, null!);
        return Output;
    }
    private void ReleaseTextures()
    { Output?.Dispose(); Output = null; _uv?.Dispose(); _uv = null; _y?.Dispose(); _y = null; _planar?.Dispose(); _planar = null; }
    public void Dispose() { ReleaseTextures(); _raster.Dispose(); _sampler.Dispose(); _constants.Dispose(); _pixel.Dispose(); _vertex.Dispose(); }
}
