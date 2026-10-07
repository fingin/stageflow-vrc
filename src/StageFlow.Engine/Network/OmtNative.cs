using System.Runtime.InteropServices;
namespace StageFlow.Engine.Network;

// ABI from OpenMediaTransport v1.0.0.19 libomt.h, shipped under licenses/omt.
public static unsafe class OmtNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Frame
    {
        public int Type; public long Timestamp; public int Codec, Width, Height, Stride, Flags, FrameRateN, FrameRateD;
        public float AspectRatio; public int ColorSpace, SampleRate, Channels, SamplesPerChannel;
        public nint Data; public int DataLength; public nint CompressedData; public int CompressedLength;
        public nint FrameMetadata; public int FrameMetadataLength;
    }
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern nint omt_send_create([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int quality);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern void omt_send_destroy(nint sender);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern int omt_send(nint sender, ref Frame frame);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern int omt_send_getaddress(nint sender, byte* address, int length);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern nint omt_receive_create([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int types, int format, int flags);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern void omt_receive_destroy(nint receiver);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern Frame* omt_receive(nint receiver, int types, int timeout);
    [DllImport("stageflow_omt", CallingConvention = CallingConvention.Cdecl)] public static extern nint omt_discovery_getaddresses(out int count);
    public static string[] Sources()
    {
        nint data = omt_discovery_getaddresses(out int count);
        if (count < 0 || count > 4096) return [];
        var names = new string[count]; for (int i = 0; i < count; i++) names[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, i * IntPtr.Size)) ?? "";
        return names;
    }
}
internal sealed unsafe class OmtSender : ILanSender
{
    private nint _sender;
    public string Name { get; }
    public OmtSender(string name) { Name = "OMT " + name; _sender = OmtNative.omt_send_create(name, 100); if (_sender == 0) throw new InvalidOperationException("OMT sender could not start."); }
    public void Send(LanFrame frame)
    {
        fixed (byte* pixels = frame.Pixels)
        fixed (float* audio = frame.Audio)
        {
            var video = new OmtNative.Frame { Type = 2, Timestamp = frame.Timestamp, Codec = 0x41524742, Width = frame.Width, Height = frame.Height,
                Stride = frame.Width * 4, FrameRateN = frame.FpsNum, FrameRateD = frame.FpsDen, AspectRatio = (float)frame.Width / frame.Height, ColorSpace = 709,
                Data = (nint)pixels, DataLength = frame.Width * frame.Height * 4 };
            if (OmtNative.omt_send(_sender, ref video) < 0) throw new IOException("OMT video send failed.");
            if (frame.Samples > 0)
            {
                var sound = new OmtNative.Frame { Type = 4, Timestamp = frame.AudioTimestamp, Codec = 0x31415046, SampleRate = 48000, Channels = 2,
                    SamplesPerChannel = frame.Samples, Data = (nint)audio, DataLength = frame.Samples * 8 };
                if (OmtNative.omt_send(_sender, ref sound) < 0) throw new IOException("OMT audio send failed.");
            }
        }
    }
    public void Dispose() { if (_sender != 0) { OmtNative.omt_send_destroy(_sender); _sender = 0; } }
}
