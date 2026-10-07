using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Spout;
using Vortice.Direct3D11;
using Vortice.Mathematics;

internal static class SpoutChecks
{
    public static unsafe void Run(GpuDevice gpu, string[] args)
    {
        string name;
        using (var source = new GpuSurface(gpu, 128, 64))
        using (var sender = new SpoutSender(gpu, "StageFlow integration " + Environment.ProcessId, 128, 64))
        using (var otherDevice = new GpuDevice(gpu.Adapter.Index))
        using (var receiver = new SpoutReceiver(otherDevice, sender.Name))
        using (var destination = new GpuSurface(otherDevice, 128, 64))
        using (var compositor = new TextureCompositor(otherDevice))
        {
            name = sender.Name;
            if (!SpoutRegistry.Senders().Contains(name)) throw new Exception("Spout sender not registered.");
            gpu.Context.ClearRenderTargetView(source.Target, new Color4(0.25f, 0.5f, 1, 1)); sender.Send(source);
            ID3D11ShaderResourceView? view = null;
            long until = Environment.TickCount64 + 2000;
            while (view == null && Environment.TickCount64 < until) { view = receiver.Receive(); if (view == null) Thread.Sleep(1); }
            if (view == null) throw new Exception(receiver.Error ?? "Timed out waiting for initial Spout texture.");
            compositor.Draw(destination, view, new(0, 0, 128, 64), new(0, 0, 1, 1));
            var description = destination.Texture.Description; description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None; description.CPUAccessFlags = CpuAccessFlags.Read;
            using var staging = otherDevice.Device.CreateTexture2D(description);
            otherDevice.Context.CopyResource(staging, destination.Texture);
            var mapped = otherDevice.Context.Map(staging, 0, MapMode.Read);
            try
            {
                byte* pixel = (byte*)mapped.DataPointer + 10 * mapped.RowPitch + 10 * 4;
                if (pixel[0] != 255 || Math.Abs(pixel[1] - 128) > 1 || Math.Abs(pixel[2] - 64) > 1 || pixel[3] != 255)
                    throw new Exception($"Spout shared texture mismatch: {pixel[0]}, {pixel[1]}, {pixel[2]}, {pixel[3]}");
            }
            finally { otherDevice.Context.Unmap(staging, 0); }
            Console.WriteLine("PASS Spout discovery and GPU texture sharing across two Direct3D devices");
            if (args.Length == 3 && args[0] == "--spout-library") CheckNativeLibrary(args[1], args[2], name);
        }
        if (SpoutRegistry.Senders().Contains(name)) throw new Exception("Spout sender did not unregister.");
        Console.WriteLine("PASS Spout sender cleanup");
    }
    private static unsafe void CheckNativeLibrary(string dll, string header, string name)
    {
        // Derive vtable slots from the matching public SDK header, only in this opt-in
        // compatibility test. The application has no dependency on SpoutLibrary.dll.
        var declarations = File.ReadAllLines(header).Where(l => Regex.IsMatch(l, @"^\s*virtual\s+")).ToArray();
        int getInfo = Array.FindIndex(declarations, l => l.Contains("GetSenderInfo("));
        int release = Array.FindIndex(declarations, l => l.Contains("void Release()"));
        if (getInfo < 0 || release < 0) throw new Exception("Spout header does not expose the required interface.");
        nint module = NativeLibrary.Load(dll);
        var factory = (delegate* unmanaged[Stdcall]<nint>)NativeLibrary.GetExport(module, "GetSpout");
        nint instance = factory(); if (instance == 0) throw new Exception("Native Spout factory failed.");
        nint* table = *(nint**)instance;
        try
        {
            var info = (delegate* unmanaged[Stdcall]<nint, byte*, uint*, uint*, nint*, uint*, byte>)table[getInfo];
            uint width = 0, height = 0, format = 0; nint handle = 0; byte[] encoded = Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = encoded)
                if (info(instance, p, &width, &height, &handle, &format) == 0 || width != 128 || height != 64 || handle == 0 || format != 87)
                    throw new Exception("Official Spout SDK could not discover StageFlow's shared texture.");
            Console.WriteLine("PASS official SpoutLibrary.dll independently discovers StageFlow texture metadata");
        }
        finally { ((delegate* unmanaged[Stdcall]<nint, void>)table[release])(instance); NativeLibrary.Free(module); }
    }
}
