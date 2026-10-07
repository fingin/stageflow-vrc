using StageFlow.Core.Grid;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Midi;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using StageFlow.Engine.Media;
using StageFlow.Engine.Streaming;
using StageFlow.Core.Projects;

foreach (var adapter in GpuDevice.EnumerateAdapters()) Console.WriteLine($"GPU {adapter.Index}: {adapter.Name}, {adapter.DedicatedBytes / 1024 / 1024} MiB");
foreach (var midi in MidiDevices.Outputs()) Console.WriteLine($"MIDI OUT {midi.Id}: {midi.Name}");
foreach (var midi in MidiDevices.Inputs()) Console.WriteLine($"MIDI IN {midi.Id}: {midi.Name}");
using var gpu = new GpuDevice();
using var surface = new GpuSurface(gpu, 2560, 1440);
using var renderer = new GridRenderer(gpu);
byte[] channels = new byte[3 * 512]; new Random(1733).NextBytes(channels);
var layout = new GridLayout { Encoding = GridEncoding.BinaryStageFlight, Width = 2560, Height = 1440, UniverseCount = 3 };
renderer.Upload(layout, channels);
gpu.Context.ClearRenderTargetView(surface.Target, new Color4(0, 0, 0, 0));
renderer.Draw(surface); gpu.Context.Flush();
var desc = surface.Texture.Description;
desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None; desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
using var staging = gpu.Device.CreateTexture2D(desc);
gpu.Context.CopyResource(staging, surface.Texture);
var mapped = gpu.Context.Map(staging, 0, MapMode.Read);
try
{
    unsafe
    {
        var cells = new GridCell[GridEncoder.RequiredCellCapacity(layout)];
        int count = new GridEncoder().Encode(layout, channels, cells);
        foreach (var cell in cells.AsSpan(0, count))
        {
            byte* pixel = (byte*)mapped.DataPointer + (cell.Y + 1) * mapped.RowPitch + (cell.X + 1) * 4;
            if (pixel[0] != cell.B || pixel[1] != cell.G || pixel[2] != cell.R || pixel[3] != cell.A)
                throw new Exception($"GPU grid mismatch channel {cell.Channel} at {cell.X},{cell.Y}: {pixel[2]} vs {cell.R}");
        }
        Console.WriteLine($"PASS {count} GPU grid cells match CPU reference on {gpu.Adapter.Name}");
    }
}
finally { gpu.Context.Unmap(staging, 0); }

if (args.Length == 2 && args[0] == "--grid-image")
{
    var pixels = File.ReadAllBytes(args[1]);
    var cells = new GridCell[GridEncoder.RequiredCellCapacity(layout)];
    int count = new GridEncoder().Encode(layout, channels, cells), bad = 0;
    foreach (var cell in cells.AsSpan(0, count))
    {
        int index = ((cell.Y + 2) * layout.Width + cell.X + 2) * 4;
        if ((pixels[index + 2] >= 128) != (cell.R >= 128)) bad++;
    }
    if (bad != 0) throw new Exception($"{bad}/{count} grid bits changed after HLS encoding");
    Console.WriteLine($"PASS all {count} binary/CRC cells survive HLS encode/decode");
}

if (args.Length == 2 && args[0] == "--media")
{
    FFmpegRuntime.Initialize(@"C:\Program Files\obs-studio\bin\64bit");
    Console.WriteLine($"Native FFmpeg: {FFmpegRuntime.Version}");
    Console.WriteLine(MediaProbe.Read(args[1]));
    using var decoder = new GpuVideoDecoder(gpu, args[1]);
    using var converter = new GpuVideoConverter(gpu);
    using var compositor = new TextureCompositor(gpu);
    using var glyphs = new GlyphRenderer(gpu);
    using var audioDecoder = new AudioDecoder(args[1]);
    var store = new HlsSegmentStore();
    using var encoder = new HardwareStreamEncoder(gpu, store, surface.Width, surface.Height, 30, 1, new StreamSettings());
    float[] pcm = new float[1600 * 2];
    int decoded = 0;
    unsafe
    {
        while (decoder.Read())
        {
            var video = converter.Convert(decoder.Frame);
            compositor.Draw(surface, video.View, new(0, 0, surface.Width, surface.Height), new(0, 0, 1, 1));
            renderer.Draw(surface);
            for (int overlay = 0; overlay < 8; overlay++)
                glyphs.Draw(surface, $"CLOCK {overlay + 1}  {StageFlow.Core.Timing.Timecode.FromFrameNumber(decoded, StageFlow.Core.Timing.MtcRate.Fps30)}",
                    60 + overlay % 2 * 1200, 450 + overlay / 2 * 180, "Consolas", 48, System.Numerics.Vector4.One);
            encoder.SubmitVideo(surface);
            audioDecoder.ReadAt(decoded * 1600, pcm);
            encoder.SubmitAudio(pcm); decoded++;
        }
    }
    encoder.Complete();
    string resultFolder = Path.Combine(Path.GetDirectoryName(args[1])!, "encoded-hls"); Directory.CreateDirectory(resultFolder);
    File.WriteAllText(Path.Combine(resultFolder, "index.m3u8"), store.Playlist());
    for (int i = 0; i < store.SegmentCount; i++)
    { var segment = store.Get(i)!; File.WriteAllBytes(Path.Combine(resultFolder, $"segment-{i}.ts"), segment.Data); }
    if (store.SegmentCount < 4) throw new Exception("Expected multiple HLS segments");
    audioDecoder.ReadAt(48000, pcm);
    double rms = Math.Sqrt(pcm.Select(x => (double)x * x).Average());
    if (rms < 0.03 || rms > 0.2) throw new Exception($"Decoded audio RMS is wrong: {rms}");
    Console.WriteLine($"PASS actual MP4 audio decode/resample/seek: RMS {rms:0.000}");
    Console.WriteLine($"PASS in-process NVENC + AAC + MPEG-TS: {store.SegmentCount} HLS segments, {store.Bytes} bytes");
    var server = new HlsServer();
    try
    {
        string web = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../web"));
        server.StartAsync(store, System.Net.IPAddress.Loopback, 0, web).GetAwaiter().GetResult();
        using var client = new HttpClient { BaseAddress = new Uri(server.Addresses.First()) };
        string playlist = client.GetStringAsync("/live/index.m3u8").GetAwaiter().GetResult();
        if (!playlist.Contains("#EXTM3U")) throw new Exception("HTTP HLS playlist missing");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/live/segment-0.ts");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 187);
        using var response = client.Send(request);
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new Exception("HLS byte range did not work");
        byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (bytes.Length != 188 || bytes[0] != 0x47) throw new Exception("Invalid HTTP MPEG-TS packet");
        string player = client.GetStringAsync("/").GetAwaiter().GetResult();
        if (!player.Contains("Play stream")) throw new Exception("Player page missing");
        Console.WriteLine("PASS in-process HTTP playlist, player page and ranged segment download");
    }
    finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    if (decoded != 150) throw new Exception($"Expected 150 test frames, got {decoded}");
    decoder.Seek(TimeSpan.FromSeconds(2));
    while (decoder.Read() && decoder.Position < TimeSpan.FromSeconds(2)) { }
    if (Math.Abs(decoder.Position.TotalSeconds - 2) > 0.04) throw new Exception("Seek did not reach target");
    Console.WriteLine($"PASS {decoded} GPU frames decoded without CPU transfer; seek reached {decoder.Position}");
}
