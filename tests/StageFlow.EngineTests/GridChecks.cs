using StageFlow.Core.Grid;
using StageFlow.Engine.Graphics;
using Vortice.Direct3D11;
using Vortice.Mathematics;

internal static class GridChecks
{
    public static unsafe void Run(GpuDevice gpu)
    {
        using var renderer = new GridRenderer(gpu); var reference = new GridEncoder();
        byte[] values = new byte[4608]; new Random(624).NextBytes(values); values[0] = values[2] = values[1536] = 0;
        int cases = 0, pixels = 0;
        foreach (GridEncoding encoding in Enum.GetValues<GridEncoding>())
        foreach (GridOrientation orientation in encoding == GridEncoding.Vrsl ? Enum.GetValues<GridOrientation>() : new[] { GridOrientation.HorizontalTop })
        foreach (bool rgb in encoding == GridEncoding.Vrsl ? new[] { false, true } : new[] { false })
        foreach (bool mask in new[] { false, true })
        {
            var layout = new GridLayout { Width = 1920, Height = 1080, UniverseCount = 9, Encoding = encoding,
                Orientation = orientation, RgbPacking = rgb, MaskZero = mask, Masks = mask ? [new(20, 50), new(1550, 1570)] : [] };
            using var surface = new GpuSurface(gpu, layout.Width, layout.Height);
            gpu.Context.ClearRenderTargetView(surface.Target, new Color4(0, 0, 0, 0)); renderer.Upload(layout, values); renderer.Draw(surface);
            var description = surface.Texture.Description; description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None; description.CPUAccessFlags = CpuAccessFlags.Read;
            using var staging = gpu.Device.CreateTexture2D(description); gpu.Context.CopyResource(staging, surface.Texture);
            var mapped = gpu.Context.Map(staging, 0, MapMode.Read);
            try
            {
                var cells = new GridCell[GridEncoder.RequiredCellCapacity(layout)]; int count = reference.Encode(layout, values, cells);
                foreach (var cell in cells.AsSpan(0, count))
                {
                    int x = cell.X + cell.Size / 2, y = cell.Y + cell.Size / 2;
                    if (x < 0 || y < 0 || x >= layout.Width || y >= layout.Height) continue;
                    byte* p = (byte*)mapped.DataPointer + y * mapped.RowPitch + x * 4;
                    if (p[3] != cell.A || (cell.A != 0 && (p[0] != cell.B || p[1] != cell.G || p[2] != cell.R)))
                        throw new Exception($"Grid {encoding}/{orientation}/RGB={rgb}/mask={mask} channel {cell.Channel} pixel {x},{y}: RGBA {p[2]},{p[1]},{p[0]},{p[3]} expected {cell.R},{cell.G},{cell.B},{cell.A}");
                    pixels++;
                }
            }
            finally { gpu.Context.Unmap(staging, 0); }
            cases++;
        }
        Console.WriteLine($"PASS {cases} GPU grid encoding/orientation/mask cases, {pixels:N0} sampled cells");
    }
}
