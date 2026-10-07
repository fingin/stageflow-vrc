using System.Numerics;
using System.Runtime.InteropServices;
using StageFlow.Core.Grid;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace StageFlow.Engine.Graphics;

/// <summary>Uploads DMX bytes, then encodes cells directly in a GPU pixel shader.</summary>
public sealed class GridRenderer : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11Buffer _values;
    private readonly ID3D11ShaderResourceView _view;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _raster;
    private readonly uint[] _upload;
    private readonly uint[] _mask;
    private GridLayout? _maskLayout;
    private readonly int _capacity;
    [StructLayout(LayoutKind.Sequential)]
    private struct GridConstants
    {
        public Vector2 Size;
        public uint Encoding, ChannelCount;
        public uint Orientation, Gamma, Rgb, MaskZero;
    }
    private const string Shader = """
        cbuffer Grid : register(b0) {
            float2 canvas; uint encoding; uint count;
            uint orientation; uint gammaCorrection; uint rgb; uint maskZero;
        };
        Buffer<uint> values : register(t0);
        struct VSOut { float4 pos : SV_Position; };
        VSOut VS(uint id : SV_VertexID) {
            VSOut o;
            o.pos = float4(id == 2 ? 3 : -1, id == 1 ? -3 : 1, 0, 1);
            return o;
        }
        float linearize(float s) { return s <= 0.04045 ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4); }
        float channelValue(uint i) {
            if (i >= count || (values[i] & 256)) return 0;
            float v = (values[i] & 255) / 255.0;
            return gammaCorrection != 0 ? round(linearize(v) * 255) / 255.0 : v;
        }
        float4 PS(VSOut p) : SV_Target {
            uint2 pixel = (uint2)p.pos.xy;
            uint ch, bit, x = pixel.x, y = pixel.y;
            if (encoding == 0) {
                if (orientation == 3) { if (y < (uint)canvas.y - 208) discard; y -= (uint)canvas.y - 208; }
                if (orientation == 1 || orientation == 2) {
                    if (orientation == 2) { if (x < (uint)canvas.x - 208) discard; x -= (uint)canvas.x - 208; }
                    uint oldX = x; x = (uint)canvas.y - 1 - y; y = oldX;
                }
                if (y >= 208) discard;
                uint universe = x / 640;
                uint within = ((x % 640) / 16) * 13 + y / 16;
                if (within >= 512 || (rgb != 0 && universe >= 3)) discard;
                ch = universe * 512 + within;
                if (ch >= count) discard;
                if (rgb != 0) return float4(channelValue(ch), channelValue(ch+1536), channelValue(ch+3072), 1);
                if ((values[ch] & 256) || (maskZero != 0 && (values[ch] & 255) == 0)) discard;
                return float4(channelValue(ch).xxx, 1);
            }
            if (encoding == 1) {
                uint column = (y / 208) * ((uint)canvas.x / 4) + x / 4;
                uint row = (y % 208) / 4;
                if (column * 6 >= count) discard;
                if (row >= 48) {
                    uint crc = values[count + column];
                    return float4(((crc >> (7 - (row - 48))) & 1).xxx, 1);
                }
                ch = column * 6 + row / 8; bit = 7 - row % 8;
            } else if (encoding == 2) {
                if (y >= 208) discard;
                uint index = (x / 4) * 52 + y / 4;
                ch = index / 8; bit = index % 8;
            } else {
                if (y >= 192) discard;
                uint index = (x / 4) * 48 + y / 4;
                ch = index / 6;
                if (ch >= count || (values[ch] & 256) || (maskZero != 0 && (values[ch] & 255) == 0)) discard;
                uint divisor = (uint)round(pow(3, 5 - index % 6));
                float v = floor(((values[ch] & 255) / divisor % 3) * 127.5) / 255.0;
                return float4(v.xxx, 1);
            }
            if (ch >= count || (values[ch] & 256) || (maskZero != 0 && (values[ch] & 255) == 0)) discard;
            float v = (values[ch] >> bit) & 1;
            return float4(v.xxx, 1);
        }
        """;
    public GridRenderer(GpuDevice gpu, int maxUniverses = 512)
    {
        _gpu = gpu; _capacity = maxUniverses * 512;
        _upload = new uint[_capacity + (_capacity + 5) / 6];
        _mask = new uint[_capacity];
        var vs = Compiler.Compile(Shader, "VS", "Grid.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
        var ps = Compiler.Compile(Shader, "PS", "Grid.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
        _vertex = gpu.Device.CreateVertexShader(vs.Span); _pixel = gpu.Device.CreatePixelShader(ps.Span);
        _constants = gpu.Device.CreateBuffer(32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _values = gpu.Device.CreateBuffer((uint)(_upload.Length * 4), BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _view = gpu.Device.CreateShaderResourceView(_values, new ShaderResourceViewDescription
        {
            Format = Format.R32_UInt, ViewDimension = ShaderResourceViewDimension.Buffer,
            Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = (uint)_upload.Length }
        });
        _blend = gpu.Device.CreateBlendState(BlendDescription.Opaque);
        _raster = gpu.Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
    }
    public void Upload(GridLayout layout, ReadOnlySpan<byte> channels)
    {
        int count = Math.Min(channels.Length, layout.UniverseCount * 512);
        if (count > _capacity) throw new ArgumentException("GPU grid capacity exceeded.");
        if (!ReferenceEquals(_maskLayout, layout))
        {
            for (int i = 0; i < _capacity; i++) _mask[i] = layout.IsMasked(i) ? 256u : 0;
            _maskLayout = layout;
        }
        for (int i = 0; i < count; i++) _upload[i] = channels[i] | _mask[i];
        Span<byte> group = stackalloc byte[6];
        if (layout.Encoding == GridEncoding.BinaryStageFlight)
            for (int i = 0; i < count; i += 6)
            { group.Clear(); channels.Slice(i, Math.Min(count - i, 6)).CopyTo(group); _upload[count + i / 6] = GridEncoder.Crc4(group); }
        var map = _gpu.Context.Map(_values, 0, MapMode.WriteDiscard);
        _upload.AsSpan(0, count + (count + 5) / 6).CopyTo(map.AsSpan<uint>(_upload.Length));
        _gpu.Context.Unmap(_values, 0);
        var constants = new GridConstants
        {
            Size = new(layout.Width, layout.Height), Encoding = (uint)layout.Encoding, ChannelCount = (uint)count,
            Orientation = (uint)layout.Orientation, Gamma = layout.GammaCorrection ? 1u : 0,
            Rgb = layout.RgbPacking ? 1u : 0, MaskZero = layout.MaskZero ? 1u : 0
        };
        map = _gpu.Context.Map(_constants, 0, MapMode.WriteDiscard);
        map.AsSpan<GridConstants>(1)[0] = constants; _gpu.Context.Unmap(_constants, 0);
    }
    public void Draw(GpuSurface target)
    {
        var c = _gpu.Context;
        c.OMSetRenderTargets(target.Target); c.RSSetViewport(new Viewport(target.Width, target.Height));
        c.RSSetState(_raster); c.OMSetBlendState(_blend);
        c.IASetInputLayout(null); c.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        c.VSSetShader(_vertex); c.PSSetShader(_pixel); c.PSSetConstantBuffer(0, _constants); c.PSSetShaderResource(0, _view);
        c.Draw(3, 0); c.PSSetShaderResource(0, null!);
    }
    public void Dispose()
    { _raster.Dispose(); _blend.Dispose(); _view.Dispose(); _values.Dispose(); _constants.Dispose(); _pixel.Dispose(); _vertex.Dispose(); }
}
