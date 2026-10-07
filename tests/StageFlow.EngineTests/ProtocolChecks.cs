using System.Diagnostics;
using System.Runtime.InteropServices;
using StageFlow.Core.Projects;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Media;
using StageFlow.Engine.Network;
using StageFlow.Engine.Streaming;
using Vortice.Mathematics;

internal static unsafe class ProtocolChecks
{
    public static void Run(string nativeDirectory)
    {
        FFmpegRuntime.Initialize(nativeDirectory); ProtocolRuntime.Initialize(nativeDirectory);
        Console.WriteLine($"ABI: OMT frame {Marshal.SizeOf<OmtNative.Frame>()}, NDI video {Marshal.SizeOf<NdiNative.VideoFrame>()}, audio {Marshal.SizeOf<NdiNative.AudioFrame>()}");
        using var gpu = new GpuDevice(); using var surface = new GpuSurface(gpu, 640, 360);
        gpu.Context.ClearRenderTargetView(surface.Target, new Color4(0.75f, 0.25f, 0.1f, 1));
        foreach (string codec in new[] { "h264_nvenc", "hevc_nvenc", "libx264" })
        {
            var store = new HlsSegmentStore(); using var encoder = new HardwareStreamEncoder(gpu, store, 640, 360, 30, 1, new() { Encoder = codec, VideoBitrateKbps = 2000 });
            float[] audio = new float[3200];
            for (int i = 0; i < 90; i++) { for (int s = 0; s < 1600; s++) audio[s * 2] = audio[s * 2 + 1] = (float)Math.Sin((i * 1600 + s) * Math.PI * 880 / 48000) * 0.1f; encoder.SubmitVideo(surface); encoder.SubmitAudio(audio); }
            encoder.Complete(); string playlist = store.Playlist() ?? throw new Exception(codec + " produced no playlist.");
            if (!playlist.Contains(".ts")) throw new Exception(codec + " produced no HLS segments.");
            Console.WriteLine("PASS codec " + codec + " produced HLS video/audio packets");
        }
        CheckOmt(); CheckNdi();
        using var output = new LanVideoOutput(gpu, surface, 30, 1, new() { NdiEnabled = true, OmtEnabled = true, NdiName = "StageFlow v2 QA GPU", OmtName = "StageFlow v2 QA GPU" });
        using var ndiInput = new NetworkVideoInput(gpu, NetworkSourceProtocol.Ndi, Environment.MachineName + " (StageFlow v2 QA GPU)");
        using var omtInput = new NetworkVideoInput(gpu, NetworkSourceProtocol.Omt, Environment.MachineName + " (StageFlow v2 QA GPU)");
        float[] pcm = Enumerable.Repeat(0.1f, 3200).ToArray(), mix = new float[3200]; bool ndiVideo = false, omtVideo = false, ndiAudio = false, omtAudio = false;
        for (int i = 0; i < 160; i++)
        {
            output.Submit(surface, i, pcm, 1); Thread.Sleep(33);
            ndiVideo |= ndiInput.GetFrame() != null; omtVideo |= omtInput.GetFrame() != null;
            Array.Clear(mix); ndiInput.MixAudio(mix, 1); ndiAudio |= mix.Any(s => Math.Abs(s) > 0.01f);
            Array.Clear(mix); omtInput.MixAudio(mix, 1); omtAudio |= mix.Any(s => Math.Abs(s) > 0.01f);
        }
        if (!(ndiVideo && omtVideo && ndiAudio && omtAudio)) throw new Exception($"Live inputs failed NDI={ndiVideo}/{ndiAudio} {ndiInput.Error}; OMT={omtVideo}/{omtAudio} {omtInput.Error}");
        Console.WriteLine("PASS NDI and OMT inputs → GPU layers + live audio mixing");
        Console.WriteLine("PASS GPU staging ring: " + output.Status);
    }
    public static void Network(string nativeDirectory, string url)
        => NetworkOutput(nativeDirectory, new() { Enabled = true, Url = url, Protocol = url.StartsWith("srt:") ? NetworkStreamProtocol.Srt : NetworkStreamProtocol.Rtmp }, new() { HlsEnabled = false, VideoBitrateKbps = 2000 });
    public static void NetworkProject(string nativeDirectory, string path)
    {
        var project = ProjectStore.Load(path); NetworkOutput(nativeDirectory, project.NetworkOutputs.Single(), project.Stream);
    }
    private static void NetworkOutput(string nativeDirectory, NetworkStreamOutput output, StreamSettings settings)
    {
        FFmpegRuntime.Initialize(nativeDirectory); ProtocolRuntime.Initialize(nativeDirectory);
        using var gpu = new GpuDevice(); using var canvas = new GpuSurface(gpu, 640, 360);
        using var encoder = new HardwareStreamEncoder(gpu, new HlsSegmentStore(), 640, 360, 30, 1, settings, [output]);
        float[] audio = new float[3200]; bool connected = false;
        for (int frame = 0; frame < 210; frame++)
        {
            gpu.Context.ClearRenderTargetView(canvas.Target, new Color4((frame % 60) / 60f, 0.3f, 0.7f, 1));
            for (int i = 0; i < 1600; i++) audio[i * 2] = audio[i * 2 + 1] = (float)Math.Sin((frame * 1600L + i) * Math.PI * 880 / 48000) * 0.1f;
            encoder.SubmitVideo(canvas); encoder.SubmitAudio(audio); Thread.Sleep(33); connected |= encoder.NetworkStatus.Contains("Live");
        }
        encoder.Complete(); Console.WriteLine("Network final: " + encoder.NetworkStatus);
        if (!connected) throw new Exception("Network output never became live.");
        Console.WriteLine("PASS network output connected and submitted 210 video frames with AAC audio");
    }
    public static void SrtLoopback(string nativeDirectory)
    {
        FFmpegRuntime.Initialize(nativeDirectory); ProtocolRuntime.Initialize(nativeDirectory);
        using var gpu = new GpuDevice(); using var surface = new GpuSurface(gpu, 640, 360);
        using var receiver = new NetworkVideoInput(gpu, NetworkSourceProtocol.Srt, "srt://127.0.0.1:19002?mode=listener&latency=20000");
        using var encoder = new HardwareStreamEncoder(gpu, new HlsSegmentStore(), 640, 360, 30, 1, new() { HlsEnabled = false, VideoBitrateKbps = 2000 },
            [new() { Enabled = true, Protocol = NetworkStreamProtocol.Srt, Url = "srt://127.0.0.1:19002?mode=caller&latency=20000" }]);
        gpu.Context.ClearRenderTargetView(surface.Target, new Color4(0.6f, 0.3f, 0.1f, 1));
        float[] sound = Enumerable.Repeat(0.1f, 3200).ToArray(), mixed = new float[3200]; bool video = false, audio = false;
        for (int frame = 0; frame < 240 && !(video && audio); frame++)
        {
            encoder.SubmitVideo(surface); encoder.SubmitAudio(sound); Thread.Sleep(33);
            video |= receiver.GetFrame() is { Width: 640, Height: 360 }; Array.Clear(mixed); receiver.MixAudio(mixed, 1); audio |= mixed.Any(s => Math.Abs(s) > 0.01f);
            if (receiver.Error != null) throw new Exception(receiver.Error);
        }
        if (!(video && audio)) throw new Exception($"SRT live input/output failed video={video}, audio={audio}, {encoder.NetworkStatus}");
        Console.WriteLine("PASS SRT H.264/AAC network loopback → GPU video layer + live audio mix");
    }
    private static void CheckOmt()
    {
        nint sender = OmtNative.omt_send_create("StageFlow v2 QA", 100); if (sender == 0) throw new Exception("OMT create failed.");
        nint receiver = 0;
        try
        {
            byte[] name = new byte[1024]; fixed (byte* p = name) OmtNative.omt_send_getaddress(sender, p, name.Length);
            string address = System.Text.Encoding.UTF8.GetString(name).TrimEnd('\0'); Console.WriteLine("OMT address: " + address);
            receiver = OmtNative.omt_receive_create(address, 6, 2, 0); if (receiver == 0) throw new Exception("OMT receive create failed.");
            byte[] pixels = new byte[640 * 360 * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 20; pixels[i + 1] = 64; pixels[i + 2] = 190; pixels[i + 3] = 255; }
            float[] pcm = Enumerable.Repeat(0.1f, 3200).ToArray(); bool video = false, audio = false;
            fixed (byte* p = pixels) fixed (float* a = pcm)
            {
                for (int i = 0; i < 200 && !(video && audio); i++)
                {
                    var frame = new OmtNative.Frame { Type = 2, Codec = 0x41524742, Width = 640, Height = 360, Stride = 2560, FrameRateN = 30, FrameRateD = 1, AspectRatio = 640f / 360, Timestamp = i * 333333L, Data = (nint)p, DataLength = pixels.Length };
                    OmtNative.omt_send(sender, ref frame);
                    var sound = new OmtNative.Frame { Type = 4, Codec = 0x31415046, SampleRate = 48000, Channels = 2, SamplesPerChannel = 1600, Timestamp = i * 333333L, Data = (nint)a, DataLength = pcm.Length * 4 }; OmtNative.omt_send(sender, ref sound);
                    var received = OmtNative.omt_receive(receiver, 2, 30);
                    if (received != null) video |= received->Type == 2 && received->Width == 640 && received->DataLength > 0;
                    received = OmtNative.omt_receive(receiver, 4, 0);
                    if (received != null) audio |= received->Type == 4 && received->SamplesPerChannel > 0;
                    Thread.Sleep(10);
                }
            }
            if (!video || !audio) throw new Exception($"OMT loopback missing video={video} audio={audio}");
            Console.WriteLine("PASS OMT video + audio loopback");
        }
        finally { if (receiver != 0) OmtNative.omt_receive_destroy(receiver); OmtNative.omt_send_destroy(sender); }
    }
    private static void CheckNdi()
    {
        if (!NdiNative.NDIlib_initialize()) throw new Exception("NDI unavailable.");
        nint text = Marshal.StringToCoTaskMemUTF8("StageFlow v2 QA"); nint sender = 0, receiver = 0;
        try
        {
            var create = new NdiNative.SendCreate { Name = text }; sender = NdiNative.NDIlib_send_create(ref create); if (sender == 0) throw new Exception("NDI sender failed.");
            var settings = new NdiNative.ReceiveCreate { Source = *NdiNative.NDIlib_send_get_source_name(sender), ColorFormat = 0, Bandwidth = 100 };
            receiver = NdiNative.NDIlib_recv_create_v3(ref settings); if (receiver == 0) throw new Exception("NDI receiver failed.");
            byte[] pixels = new byte[640 * 360 * 4]; Array.Fill(pixels, (byte)128); float[] pcm = Enumerable.Repeat(0.1f, 3200).ToArray(); bool video = false, audio = false;
            fixed (byte* p = pixels) fixed (float* a = pcm)
            {
                for (int i = 0; i < 200 && !(video && audio); i++)
                {
                    var frame = new NdiNative.VideoFrame { Width = 640, Height = 360, FourCC = 0x58524742, FpsNum = 30, FpsDen = 1, Aspect = 640f / 360, Format = 1, Timecode = i * 333333L, Data = (nint)p, Stride = 2560 };
                    NdiNative.NDIlib_send_send_video_v2(sender, ref frame);
                    var sound = new NdiNative.AudioFrame { SampleRate = 48000, Channels = 2, Samples = 1600, Timecode = frame.Timecode, Data = (nint)a, Stride = 6400 }; NdiNative.NDIlib_send_send_audio_v2(sender, ref sound);
                    NdiNative.VideoFrame vf; NdiNative.AudioFrame af; int kind = NdiNative.NDIlib_recv_capture_v2(receiver, &vf, &af, 0, 30);
                    if (kind == 1) { video |= vf.Width == 640 && vf.Data != 0; NdiNative.NDIlib_recv_free_video_v2(receiver, ref vf); }
                    if (kind == 2) { audio |= af.Samples > 0; NdiNative.NDIlib_recv_free_audio_v2(receiver, ref af); }
                    Thread.Sleep(10);
                }
            }
            if (!video || !audio) throw new Exception($"NDI loopback missing video={video} audio={audio}");
            Console.WriteLine("PASS NDI video + audio loopback");
        }
        finally { if (receiver != 0) NdiNative.NDIlib_recv_destroy(receiver); if (sender != 0) NdiNative.NDIlib_send_destroy(sender); Marshal.FreeCoTaskMem(text); }
    }
}
