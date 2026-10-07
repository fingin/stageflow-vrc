using System.Runtime.InteropServices;
namespace StageFlow.Engine.Network;

public static unsafe class NdiNative
{
    [StructLayout(LayoutKind.Sequential)] public struct Source { public nint Name, Url; }
    [StructLayout(LayoutKind.Sequential)] public struct SendCreate { public nint Name, Groups; [MarshalAs(UnmanagedType.U1)] public bool ClockVideo; [MarshalAs(UnmanagedType.U1)] public bool ClockAudio; }
    [StructLayout(LayoutKind.Sequential)] public struct FindCreate { [MarshalAs(UnmanagedType.U1)] public bool ShowLocal; public nint Groups, ExtraIps; }
    [StructLayout(LayoutKind.Sequential)] public struct ReceiveCreate { public Source Source; public int ColorFormat, Bandwidth; [MarshalAs(UnmanagedType.U1)] public bool AllowFields; public nint Name; }
    [StructLayout(LayoutKind.Sequential)] public struct VideoFrame
    { public int Width, Height, FourCC, FpsNum, FpsDen; public float Aspect; public int Format; public long Timecode; public nint Data; public int Stride; public nint Metadata; public long Timestamp; }
    [StructLayout(LayoutKind.Sequential)] public struct AudioFrame
    { public int SampleRate, Channels, Samples; public long Timecode; public nint Data; public int Stride; public nint Metadata; public long Timestamp; }
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool NDIlib_initialize();
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern nint NDIlib_send_create(ref SendCreate settings);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_send_destroy(nint sender);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_send_send_video_v2(nint sender, ref VideoFrame frame);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_send_send_audio_v2(nint sender, ref AudioFrame frame);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern Source* NDIlib_send_get_source_name(nint sender);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern nint NDIlib_recv_create_v3(ref ReceiveCreate settings);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_destroy(nint receiver);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern int NDIlib_recv_capture_v2(nint receiver, VideoFrame* video, AudioFrame* audio, nint metadata, uint timeout);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_free_video_v2(nint receiver, ref VideoFrame frame);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_recv_free_audio_v2(nint receiver, ref AudioFrame frame);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern nint NDIlib_find_create_v2(ref FindCreate settings);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern Source* NDIlib_find_get_current_sources(nint finder, out uint count);
    [DllImport("stageflow_ndi", CallingConvention = CallingConvention.Cdecl)] public static extern void NDIlib_find_destroy(nint finder);
    public static string[] Sources()
    {
        if (!NDIlib_initialize()) throw new IOException("NDI could not initialize.");
        var settings = new FindCreate { ShowLocal = true }; nint finder = NDIlib_find_create_v2(ref settings);
        if (finder == 0) throw new IOException("NDI discovery could not start.");
        try
        {
            Thread.Sleep(800); var sources = NDIlib_find_get_current_sources(finder, out uint count);
            if (sources == null || count > 4096) return [];
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = Marshal.PtrToStringUTF8(sources[i].Name) ?? "";
            return names;
        }
        finally { NDIlib_find_destroy(finder); }
    }
}
internal sealed unsafe class NdiSender : ILanSender
{
    private nint _sender;
    public string Name { get; }
    public NdiSender(string name)
    {
        Name = "NDI " + name;
        if (!NdiNative.NDIlib_initialize()) throw new InvalidOperationException("NDI initialization failed.");
        nint text = Marshal.StringToCoTaskMemUTF8(name);
        try { var settings = new NdiNative.SendCreate { Name = text }; _sender = NdiNative.NDIlib_send_create(ref settings); }
        finally { Marshal.FreeCoTaskMem(text); }
        if (_sender == 0) throw new InvalidOperationException("NDI sender could not start.");
    }
    public void Send(LanFrame frame)
    {
        fixed (byte* pixels = frame.Pixels)
        fixed (float* audio = frame.Audio)
        {
            var video = new NdiNative.VideoFrame { Width = frame.Width, Height = frame.Height, FourCC = 0x58524742, FpsNum = frame.FpsNum, FpsDen = frame.FpsDen,
                Aspect = (float)frame.Width / frame.Height, Format = 1, Timecode = frame.Timestamp, Data = (nint)pixels, Stride = frame.Width * 4 };
            // Synchronous SDK calls on this output's worker keep ownership explicit.
            NdiNative.NDIlib_send_send_video_v2(_sender, ref video);
            if (frame.Samples > 0)
            {
                var sound = new NdiNative.AudioFrame { SampleRate = 48000, Channels = 2, Samples = frame.Samples, Timecode = frame.AudioTimestamp, Data = (nint)audio, Stride = frame.Samples * 4 };
                NdiNative.NDIlib_send_send_audio_v2(_sender, ref sound);
            }
        }
    }
    public void Dispose() { if (_sender != 0) { NdiNative.NDIlib_send_destroy(_sender); _sender = 0; } }
}
