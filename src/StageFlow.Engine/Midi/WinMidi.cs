using System.Runtime.InteropServices;

namespace StageFlow.Engine.Midi;

public sealed record MidiDevice(uint Id, string Name);

internal static class WinMidi
{
    internal const uint CallbackFunction = 0x30000;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OutCaps
    {
        public ushort Manufacturer, Product;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology, Voices, Notes, ChannelMask;
        public uint Support;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InCaps
    {
        public ushort Manufacturer, Product;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Header
    {
        public nint Data;
        public uint BufferLength, BytesRecorded;
        public nuint User;
        public uint Flags;
        public nint Next;
        public nuint Reserved;
        public uint Offset;
        public fixed ulong ReservedArray[8];
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void InputCallback(nint input, uint message, nuint instance, nuint param1, nuint param2);
    [DllImport("winmm.dll")] internal static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiOutGetDevCapsW(nuint id, out OutCaps caps, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutOpen(out nint handle, uint id, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] internal static extern uint midiOutShortMsg(nint handle, uint message);
    [DllImport("winmm.dll")] internal static extern uint midiOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutLongMsg(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutReset(nint handle);
    [DllImport("winmm.dll")] internal static extern uint midiOutClose(nint handle);
    [DllImport("winmm.dll")] internal static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiInGetDevCapsW(nuint id, out InCaps caps, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInOpen(out nint handle, uint id, InputCallback callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] internal static extern uint midiInPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInAddBuffer(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInStart(nint handle);
    [DllImport("winmm.dll")] internal static extern uint midiInStop(nint handle);
    [DllImport("winmm.dll")] internal static extern uint midiInReset(nint handle);
    [DllImport("winmm.dll")] internal static extern uint midiInClose(nint handle);
    [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
    internal static void Check(uint result, string operation)
    { if (result != 0) throw new InvalidOperationException($"{operation} failed (Windows MIDI error {result})."); }
}

public static class MidiDevices
{
    public static IReadOnlyList<MidiDevice> Outputs()
    {
        var list = new List<MidiDevice>();
        for (uint id = 0; id < WinMidi.midiOutGetNumDevs(); id++)
            if (WinMidi.midiOutGetDevCapsW(id, out var caps, (uint)Marshal.SizeOf<WinMidi.OutCaps>()) == 0) list.Add(new(id, caps.Name));
        return list;
    }
    public static IReadOnlyList<MidiDevice> Inputs()
    {
        var list = new List<MidiDevice>();
        for (uint id = 0; id < WinMidi.midiInGetNumDevs(); id++)
            if (WinMidi.midiInGetDevCapsW(id, out var caps, (uint)Marshal.SizeOf<WinMidi.InCaps>()) == 0) list.Add(new(id, caps.Name));
        return list;
    }
}

/// <summary>Owns one output handle and one prepared SysEx buffer. Use from its owner thread.</summary>
public sealed unsafe class MidiOutput : IDisposable
{
    private nint _handle, _header, _data;
    public MidiOutput(uint device)
    {
        WinMidi.Check(WinMidi.midiOutOpen(out _handle, device, 0, 0, 0), "Open MIDI output");
        try
        {
            _data = Marshal.AllocHGlobal(1024); _header = Marshal.AllocHGlobal(sizeof(WinMidi.Header));
            *(WinMidi.Header*)_header = new() { Data = _data, BufferLength = 1024 };
            WinMidi.Check(WinMidi.midiOutPrepareHeader(_handle, _header, (uint)sizeof(WinMidi.Header)), "Prepare MIDI buffer");
        }
        catch { Dispose(); throw; }
    }
    public void SendShort(uint packed) => WinMidi.Check(WinMidi.midiOutShortMsg(_handle, packed), "Send MIDI");
    public void SendSysEx(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 1024) throw new ArgumentException("SysEx message exceeds prepared buffer.");
        bytes.CopyTo(new Span<byte>((void*)_data, bytes.Length));
        ((WinMidi.Header*)_header)->BufferLength = (uint)bytes.Length;
        WinMidi.Check(WinMidi.midiOutLongMsg(_handle, _header, (uint)sizeof(WinMidi.Header)), "Send SysEx");
        var deadline = Environment.TickCount64 + 500;
        while ((((WinMidi.Header*)_header)->Flags & 1) == 0)
        {
            if (Environment.TickCount64 >= deadline)
            { WinMidi.midiOutReset(_handle); throw new TimeoutException("MIDI device did not finish sending SysEx."); }
            Thread.Sleep(1);
        }
    }
    public void Dispose()
    {
        if (_handle != 0)
        {
            WinMidi.midiOutReset(_handle);
            if (_header != 0) WinMidi.midiOutUnprepareHeader(_handle, _header, (uint)sizeof(WinMidi.Header));
            WinMidi.midiOutClose(_handle); _handle = 0;
        }
        if (_header != 0) { Marshal.FreeHGlobal(_header); _header = 0; }
        if (_data != 0) { Marshal.FreeHGlobal(_data); _data = 0; }
    }
}
