using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StageFlow.Engine.Audio;

public sealed record AudioDevice(string Id, string Name);

public sealed class LocalAudioOutput : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeSampleProvider _gain;
    private readonly byte[] _scratch = new byte[65536];
    private long _overruns;
    private string? _error;
    public string? Error => Volatile.Read(ref _error);
    public long Overruns => Interlocked.Read(ref _overruns);
    public TimeSpan BufferedDuration => _buffer.BufferedDuration;
    public float Volume { get => _gain.Volume; set => _gain.Volume = Math.Clamp(value, 0, 2); }
    public static IReadOnlyList<AudioDevice> Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        { using (device) result.Add(new(device.ID, device.FriendlyName)); }
        return result;
    }
    public LocalAudioOutput(string? deviceId = null, int latencyMilliseconds = 50)
    {
        using var enumerator = new MMDeviceEnumerator();
        _device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId);
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
        { BufferDuration = TimeSpan.FromMilliseconds(250), DiscardOnBufferOverflow = false, ReadFully = true };
        _gain = new VolumeSampleProvider(_buffer.ToSampleProvider());
        _output = new WasapiOut(_device, AudioClientShareMode.Shared, true, latencyMilliseconds);
        _output.PlaybackStopped += (_, e) => { if (e.Exception != null) Volatile.Write(ref _error, e.Exception.Message); };
        try { _output.Init(_gain); _output.Play(); }
        catch { _output.Dispose(); _device.Dispose(); throw; }
    }
    public void Write(ReadOnlySpan<float> stereo)
    {
        var bytes = MemoryMarshal.AsBytes(stereo);
        while (!bytes.IsEmpty)
        {
            int size = Math.Min(_scratch.Length, bytes.Length);
            if (_buffer.BufferedBytes + size > _buffer.BufferLength)
            { Interlocked.Increment(ref _overruns); _buffer.ClearBuffer(); }
            bytes[..size].CopyTo(_scratch); _buffer.AddSamples(_scratch, 0, size); bytes = bytes[size..];
        }
    }
    public void Flush() => _buffer.ClearBuffer();
    public void Dispose() { _output.Stop(); _output.Dispose(); _device.Dispose(); }
}
