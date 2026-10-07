using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace StageFlow.Engine.Graphics;

/// <summary>Rasterizes a font atlas only when its character set/style changes, then draws instanced GPU quads.</summary>
public sealed class GlyphRenderer : IDisposable
{
    private sealed class Atlas : IDisposable
    {
        public required ID3D11Texture2D Texture;
        public required ID3D11ShaderResourceView View;
        public required Dictionary<char, (Vector4 Uv, float Advance)> Glyphs;
        public int CellWidth, CellHeight;
        public void Dispose() { View.Dispose(); Texture.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphInstance { public Vector4 Rect, Uv, Color; }
    private readonly GpuDevice _gpu;
    private readonly Dictionary<(string Font, int Size), Atlas> _atlases = [];
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11InputLayout _layout;
    private readonly ID3D11Buffer _constants;
    private ID3D11Buffer _instances;
    private int _capacity = 512;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _raster;
    private const string BaseCharacters = "0123456789:; .-_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz/[]()";
    private const string Shader = """
        cbuffer Canvas : register(b0) { float2 canvas; float2 pad; };
        Texture2D atlas : register(t0); SamplerState sampleState : register(s0);
        struct I { float4 rect:RECT; float4 uv:UV; float4 color:COLOR; };
        struct V { float4 pos:SV_Position; float2 uv:TEXCOORD0; float4 color:COLOR; };
        V VS(I i, uint id:SV_VertexID) {
            float2 corners[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
            float2 corner = corners[id], xy = (i.rect.xy + corner*i.rect.zw)/canvas;
            V o; o.pos=float4(xy.x*2-1,1-xy.y*2,0,1); o.uv=i.uv.xy+corner*i.uv.zw; o.color=i.color; return o;
        }
        float4 PS(V i):SV_Target { float4 c=atlas.Sample(sampleState,i.uv); return float4(c.rgb*i.color.rgb*i.color.a,c.a*i.color.a); }
        """;
    public GlyphRenderer(GpuDevice gpu)
    {
        _gpu = gpu;
        var vs = Compiler.Compile(Shader, "VS", "Glyphs.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
        _vertex = gpu.Device.CreateVertexShader(vs.Span);
        _pixel = gpu.Device.CreatePixelShader(Compiler.Compile(Shader, "PS", "Glyphs.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3).Span);
        _layout = gpu.Device.CreateInputLayout([
            new("RECT", 0, Format.R32G32B32A32_Float, 0, 0, InputClassification.PerInstanceData, 1),
            new("UV", 0, Format.R32G32B32A32_Float, 16, 0, InputClassification.PerInstanceData, 1),
            new("COLOR", 0, Format.R32G32B32A32_Float, 32, 0, InputClassification.PerInstanceData, 1)], vs.Span);
        _constants = gpu.Device.CreateBuffer(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _instances = gpu.Device.CreateBuffer((uint)(_capacity * 48), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _blend = gpu.Device.CreateBlendState(BlendDescription.AlphaBlend);
        _raster = gpu.Device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
    }
    public unsafe Vector2 Draw(GpuSurface target, string text, float x, float y, string fontName, int size, Vector4 color)
    {
        if (text.Length == 0) return Vector2.Zero;
        size = Math.Clamp(size, 8, 256);
        var key = (fontName, size);
        if (!_atlases.TryGetValue(key, out var atlas) || text.Any(c => !atlas.Glyphs.ContainsKey(c)))
        {
            string chars = new((BaseCharacters + text + (atlas == null ? "" : new string(atlas.Glyphs.Keys.ToArray()))).Distinct().ToArray());
            atlas?.Dispose(); atlas = BuildAtlas(fontName, size, chars); _atlases[key] = atlas;
        }
        if (text.Length > _capacity)
        {
            _capacity = Math.Max(_capacity * 2, text.Length); _instances.Dispose();
            _instances = _gpu.Device.CreateBuffer((uint)(_capacity * 48), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        }
        var c = _gpu.Context;
        var mapped = c.Map(_instances, 0, MapMode.WriteDiscard);
        var instances = mapped.AsSpan<GlyphInstance>(text.Length);
        float cursor = x;
        for (int i = 0; i < text.Length; i++)
        {
            var glyph = atlas.Glyphs[text[i]];
            instances[i] = new() { Rect = new(cursor, y, atlas.CellWidth, atlas.CellHeight), Uv = glyph.Uv, Color = color };
            cursor += glyph.Advance;
        }
        c.Unmap(_instances, 0);
        mapped = c.Map(_constants, 0, MapMode.WriteDiscard); mapped.AsSpan<Vector4>(1)[0] = new(target.Width, target.Height, 0, 0); c.Unmap(_constants, 0);
        c.OMSetRenderTargets(target.Target); c.RSSetViewport(new Viewport(target.Width, target.Height));
        c.RSSetState(_raster); c.OMSetBlendState(_blend); c.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        c.IASetInputLayout(_layout); c.IASetVertexBuffer(0, _instances, 48);
        c.VSSetShader(_vertex); c.PSSetShader(_pixel); c.VSSetConstantBuffer(0, _constants);
        c.PSSetShaderResource(0, atlas.View); c.PSSetSampler(0, _sampler);
        c.DrawInstanced(6, (uint)text.Length, 0, 0); c.PSSetShaderResource(0, null!);
        return new(cursor - x, atlas.CellHeight);
    }
    private unsafe Atlas BuildAtlas(string name, int size, string characters)
    {
        int cellWidth = size * 2 + 4, cellHeight = size * 2 + 4;
        int columns = Math.Max(1, Math.Min(16, 4096 / cellWidth));
        int width = columns * cellWidth, height = ((characters.Length + columns - 1) / columns) * cellHeight;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent); graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(name, size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip };
        var glyphs = new Dictionary<char, (Vector4 Uv, float Advance)>();
        for (int i = 0; i < characters.Length; i++)
        {
            int x = (i % columns) * cellWidth, y = i / columns * cellHeight;
            string s = characters[i].ToString();
            var measured = graphics.MeasureString(s, font, int.MaxValue, format);
            graphics.DrawString(s, font, Brushes.White, x + 2, y + 2, format);
            glyphs[characters[i]] = (new((float)x / width, (float)y / height, (float)cellWidth / width, (float)cellHeight / height), measured.Width);
        }
        var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        ID3D11Texture2D texture;
        try
        {
            texture = _gpu.Device.CreateTexture2D(new Texture2DDescription
            { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = SampleDescription.Default, Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource },
                new SubresourceData(bits.Scan0, (uint)bits.Stride));
        }
        finally { bitmap.UnlockBits(bits); }
        return new() { Texture = texture, View = _gpu.Device.CreateShaderResourceView(texture), Glyphs = glyphs, CellWidth = cellWidth, CellHeight = cellHeight };
    }
    public void Dispose()
    {
        foreach (var atlas in _atlases.Values) atlas.Dispose(); _atlases.Clear();
        _raster.Dispose(); _blend.Dispose(); _sampler.Dispose(); _instances.Dispose(); _constants.Dispose(); _layout.Dispose(); _pixel.Dispose(); _vertex.Dispose();
    }
}
