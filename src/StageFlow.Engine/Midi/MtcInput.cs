using System.Runtime.InteropServices;
using System.Threading.Channels;
using StageFlow.Core.Timing;

namespace StageFlow.Engine.Midi;

public sealed unsafe class MtcInput : IDisposable
{
    private readonly Transport _transport;
    private readonly WinMidi.InputCallback _callback;
    private readonly Channel<(uint Message, nuint Data)> _queue = Channel.CreateBounded<(uint, nuint)>(
        new BoundedChannelOptions(512) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly List<nint> _headers = [];
    private readonly Thread _thread;
    private nint _handle;
    private volatile bool _closing;
    private long _overruns, _received;
    private string? _error;
    public long Overruns => Interlocked.Read(ref _overruns);
    public long Received => Interlocked.Read(ref _received);
    public string? Error => Volatile.Read(ref _error);

    public MtcInput(Transport transport, uint device)
    {
        _transport = transport; _callback = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow MTC input", Priority = ThreadPriority.AboveNormal };
        WinMidi.Check(WinMidi.midiInOpen(out _handle, device, _callback, 0, WinMidi.CallbackFunction), "Open MIDI input");
        try
        {
            for (int i = 0; i < 4; i++)
            {
                nint header = Marshal.AllocHGlobal(sizeof(WinMidi.Header));
                *(WinMidi.Header*)header = new() { Data = Marshal.AllocHGlobal(1024), BufferLength = 1024 };
                _headers.Add(header);
                WinMidi.Check(WinMidi.midiInPrepareHeader(_handle, header, (uint)sizeof(WinMidi.Header)), "Prepare MIDI input");
                WinMidi.Check(WinMidi.midiInAddBuffer(_handle, header, (uint)sizeof(WinMidi.Header)), "Queue MIDI input");
            }
            _thread.Start();
            WinMidi.Check(WinMidi.midiInStart(_handle), "Start MIDI input");
        }
        catch { Dispose(); throw; }
    }
    private void Callback(nint handle, uint message, nuint instance, nuint data, nuint timestamp)
    {
        // Never call WinMM, render, allocate a message buffer, or touch the UI in this callback.
        if (message is 0x3c3 or 0x3cc or 0x3c4 or 0x3c6)
            if (!_queue.Writer.TryWrite((message, data))) Interlocked.Increment(ref _overruns);
    }
    private void Run()
    {
        var decoder = new MtcQuarterFrameDecoder();
        Span<byte> sysex = stackalloc byte[1024];
        int sysexLength = 0;
        try
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                while (_queue.Reader.TryRead(out var item))
                {
                    if (item.Message is 0x3c3 or 0x3cc)
                    {
                        if ((item.Data & 255) == 0xf1 && decoder.Push((byte)((item.Data >> 8) & 127), out var timecode))
                        {
                            // Reverse incoming timecode is a locate until reverse media playback is supported.
                            _transport.ReceiveMtc(timecode, decoder.Direction > 0); Interlocked.Increment(ref _received);
                        }
                    }
                    else
                    {
                        var header = (WinMidi.Header*)item.Data;
                        if (item.Message == 0x3c4)
                        {
                            foreach (byte value in new ReadOnlySpan<byte>((void*)header->Data, (int)header->BytesRecorded))
                            {
                                if (value >= 0xf8) continue; // Realtime can be interleaved in SysEx.
                                if (value == 0xf0) sysexLength = 0;
                                if (sysexLength == sysex.Length) sysexLength = 0;
                                sysex[sysexLength++] = value;
                                if (value == 0xf7)
                                {
                                    if (MtcCodec.TryReadFullFrame(sysex[..sysexLength], out var tc))
                                    { decoder.Reset(); _transport.ReceiveMtc(tc, false); Interlocked.Increment(ref _received); }
                                    sysexLength = 0;
                                }
                            }
                        }
                        if (!_closing)
                        { header->BytesRecorded = 0; WinMidi.Check(WinMidi.midiInAddBuffer(_handle, (nint)header, (uint)sizeof(WinMidi.Header)), "Requeue MIDI input"); }
                    }
                }
        }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); }
    }
    public void Dispose()
    {
        if (_closing) return;
        _closing = true;
        if (_handle != 0) { WinMidi.midiInStop(_handle); WinMidi.midiInReset(_handle); }
        _queue.Writer.TryComplete();
        if (_thread.IsAlive) _thread.Join();
        foreach (nint address in _headers)
        {
            if (_handle != 0) WinMidi.midiInUnprepareHeader(_handle, address, (uint)sizeof(WinMidi.Header));
            Marshal.FreeHGlobal(((WinMidi.Header*)address)->Data); Marshal.FreeHGlobal(address);
        }
        _headers.Clear();
        if (_handle != 0) { WinMidi.midiInClose(_handle); _handle = 0; }
        GC.KeepAlive(_callback);
    }
}
