using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D11;

namespace StageFlow.Engine.Graphics;

/// <summary>Explicit still-image export only. Never used in the playback or encoding path.</summary>
public static class FrameCapture
{
    public static unsafe void SavePng(GpuDevice gpu, GpuSurface surface, string path)
    {
        var description = surface.Texture.Description;
        description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read; description.MiscFlags = ResourceOptionFlags.None;
        using var staging = gpu.Device.CreateTexture2D(description);
        gpu.Context.CopyResource(staging, surface.Texture);
        var mapped = gpu.Context.Map(staging, 0, MapMode.Read);
        try
        {
            using var bitmap = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb);
            var bits = bitmap.LockBits(new Rectangle(0, 0, surface.Width, surface.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < surface.Height; row++)
                    Buffer.MemoryCopy((byte*)mapped.DataPointer + row * mapped.RowPitch,
                        (byte*)bits.Scan0 + row * bits.Stride, bits.Stride, surface.Width * 4);
            }
            finally { bitmap.UnlockBits(bits); }
            bitmap.Save(path, ImageFormat.Png);
        }
        finally { gpu.Context.Unmap(staging, 0); }
    }
}
