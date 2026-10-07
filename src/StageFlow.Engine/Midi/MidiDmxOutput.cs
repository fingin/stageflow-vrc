using StageFlow.Core.ArtNet;
using StageFlow.Core.Projects;

namespace StageFlow.Engine.Midi;

public sealed class MidiDmxOutput : IDisposable
{
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private readonly DmxUniverseStore _store;
    private readonly MidiDmxSettings _settings;
    private readonly uint _device;
    private string _status = "Opening MIDI-DMX", _error = "";
    public string Status => Volatile.Read(ref _status);
    public string? Error => Volatile.Read(ref _error) is { Length: > 0 } error ? error : null;
    public MidiDmxOutput(DmxUniverseStore store, uint device, MidiDmxSettings settings)
    {
        _store = store; _device = device; _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow MIDI-DMX" }; _thread.Start();
    }
    private void Run()
    {
        try
        {
            using var output = new MidiOutput(_device);
            using var log = new MidiReadyLog(_settings.UseEditorLog, _settings.LogPath);
            var scheduler = new MidiDmxScheduler();
            byte[] channels = new byte[_store.Count * 512]; uint[] messages = new uint[_settings.ChannelsPerUpdate];
            long lastSend = 0;
            while (!_stop.IsSet)
            {
                bool ready = log.Poll();
                if (ready)
                {
                    _store.CopyTo(channels);
                    int count = scheduler.Build(channels.AsSpan(0, Math.Min(channels.Length, _store.ActiveChannelCount)), messages,
                        _settings.ChannelsPerUpdate, _settings.IdleScanChannels);
                    for (int i = 0; i < count; i++) output.SendShort(messages[i]);
                    // An empty show must still acknowledge the watchdog.
                    if (count == 0) output.SendShort(MidiDmxCodec.Control(127));
                    lastSend = Environment.TickCount64; Volatile.Write(ref _status, "MIDI-DMX connected");
                }
                else if (Environment.TickCount64 - lastSend >= 1000)
                {
                    scheduler.Reset();
                    foreach (int code in new[] { 0, 101, 120, 107, 127 }) output.SendShort(MidiDmxCodec.Control(code));
                    lastSend = Environment.TickCount64; Volatile.Write(ref _status, "MIDI-DMX waiting for world / MIDIREADY");
                }
                _stop.Wait(4);
            }
        }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); Volatile.Write(ref _status, "MIDI-DMX disconnected"); }
    }
    public void Dispose() { _stop.Set(); _thread.Join(); _stop.Dispose(); }
}

/// <summary>Bounded incremental log tail, with a match carried across reads and periodic rotation detection.</summary>
internal sealed class MidiReadyLog(bool editor, string? explicitPath) : IDisposable
{
    private FileStream? _file;
    private string? _path;
    private long _check;
    private int _matched;
    private readonly byte[] _buffer = new byte[32768];
    public bool Poll()
    {
        try
        {
            if (Environment.TickCount64 >= _check)
            {
                _check = Environment.TickCount64 + 2000;
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string directory = Path.GetFullPath(Path.Combine(local, "../LocalLow/VRChat/VRChat"));
                string? path = explicitPath ?? (editor ? Path.Combine(local, "Unity/Editor/Editor.log") :
                    Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "output_log_*.txt").Order(StringComparer.Ordinal).LastOrDefault() : null);
                if (path != _path || (_file == null && path != null))
                {
                    _file?.Dispose(); _file = null; _path = path; _matched = 0;
                    if (path != null && File.Exists(path))
                    { _file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); _file.Position = _file.Length; }
                }
            }
            if (_file == null) return false;
            if (_file.Length < _file.Position) { _file.Position = 0; _matched = 0; }
            if (_file.Length - _file.Position > 1024 * 1024) { _file.Position = _file.Length - 1024 * 1024; _matched = 0; }
            bool ready = false; int count = _file.Read(_buffer);
            ReadOnlySpan<byte> word = "MIDIREADY"u8;
            for (int i = 0; i < count; i++)
            {
                byte c = _buffer[i]; _matched = c == word[_matched] ? _matched + 1 : c == word[0] ? 1 : 0;
                if (_matched == word.Length) { _matched = 0; ready = true; }
            }
            return ready;
        }
        catch (IOException) { _file?.Dispose(); _file = null; return false; }
        catch (UnauthorizedAccessException) { _file?.Dispose(); _file = null; return false; }
    }
    public void Dispose() => _file?.Dispose();
}
