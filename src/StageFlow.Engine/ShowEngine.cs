using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using StageFlow.Core.ArtNet;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;
using StageFlow.Engine.Audio;
using StageFlow.Engine.Graphics;
using StageFlow.Engine.Media;
using StageFlow.Engine.Midi;
using StageFlow.Engine.Streaming;
using StageFlow.Engine.Spout;
using StageFlow.Engine.Network;
using Vortice.Mathematics;

namespace StageFlow.Engine;

public sealed record EngineStatus(string Gpu, double RenderMilliseconds, long RenderedFrames, long MissedDeadlines,
    bool Streaming, string? StreamUrl, long ArtNetPackets, long AudioUnderruns, string? Error, string? MediaError,
    string MidiDmx = "Off", string? SpoutOutput = null, string? SpoutStatus = null, double MeanRenderMilliseconds = 0, double PeakRenderMilliseconds = 0,
    string? PhaseMilliseconds = null, string? NetworkStatus = null);
public sealed record SlowFrame(DateTime Utc, double Milliseconds, int MissedBefore, double[] PhaseMilliseconds, int Gc0, int Gc1, int Gc2);

/// <summary>Owns native resource lifetime and presentation. GUI communicates through commands and immutable projects.</summary>
public sealed class ShowEngine : IDisposable
{
    private sealed record WorkerCommand(Action Action, TaskCompletionSource Completion);
    private readonly ConcurrentQueue<WorkerCommand> _commands = new();
    private readonly object _lifecycle = new();
    private volatile bool _terminated;
    private int _disposed;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private readonly nint _window;
    private readonly string _runtime, _webAssets;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _previewWidth, _previewHeight;
    private ShowProject _project;
    private EngineStatus _status = new("Starting", 0, 0, 0, false, null, 0, 0, null, null);
    private GpuDevice? _gpu;
    private GpuSurface? _canvas;
    private TextureCompositor? _compositor;
    private GlyphRenderer? _glyphs;
    private GpuVideoConverter? _converter;
    private PreviewDisplay? _preview;
    private MediaPlayback? _media;
    private GpuSurface? _video;
    private DmxUniverseStore? _dmx;
    private ArtNetReceiver? _artnet;
    private readonly Dictionary<Guid, (GridRenderer Renderer, GpuSurface Surface, long Revision, StageFlow.Core.Grid.GridLayout Layout)> _grids = [];
    private byte[] _dmxBytes = [];
    private LocalAudioOutput? _localAudio;
    private MtcOutput? _mtcOut;
    private MtcInput? _mtcIn;
    private MidiDmxOutput? _midiDmx;
    private SpoutSender? _spoutOut;
    private readonly Dictionary<Guid, SpoutReceiver> _spoutInputs = [];
    private LanVideoOutput? _lanOutput;
    private readonly Dictionary<Guid, NetworkVideoInput> _networkInputs = [];
    private HlsServer? _server;
    private HardwareStreamEncoder? _encoder;
    private HlsSegmentStore? _segments;
    private string? _streamUrl, _error;
    private string? _artNetError, _localAudioError;
    private long _frames, _missed, _audioCursorRevision = -1;
    private long _outputTick, _streamStartTick;
    private double _renderTotal, _renderPeak;
    private readonly double[] _phaseTotal = new double[6];
    private readonly double[] _phaseCurrent = new double[6];
    private readonly ConcurrentQueue<SlowFrame> _slowFrames = new();
    public SlowFrame[] DrainSlowFrames()
    { var frames = new List<SlowFrame>(); while (_slowFrames.TryDequeue(out var frame)) frames.Add(frame); return frames.ToArray(); }
    private double _audioCursor;
    private bool _audioWrapped;
    private TransportState _lastState;
    private readonly float[] _audioScratch = new float[96000];
    private bool _midiArmed;
    private Layer? _selection;
    public Transport Transport { get; } = new();
    public MediaInfo? MediaInfo => _media?.Info;
    public void SelectLayer(Guid? id) => Volatile.Write(ref _selection, Project.Layers.FirstOrDefault(l => l.Id == id));
    public void StepFrame(int direction)
    {
        var info = MediaInfo;
        if (info is { FpsNumerator: > 0, FpsDenominator: > 0 }) Transport.Step(direction, info.FpsNumerator, info.FpsDenominator);
        else Transport.Step(direction);
    }
    public Task Ready => _ready.Task;
    public EngineStatus Status => Volatile.Read(ref _status);
    public ShowProject Project => Volatile.Read(ref _project);
    public ShowEngine(nint previewHwnd, int width, int height, ShowProject project, string runtimeDirectory, string webAssetsDirectory)
    {
        _window = previewHwnd; _previewWidth = width; _previewHeight = height; _project = project;
        _runtime = runtimeDirectory; _webAssets = webAssetsDirectory;
        Transport.Load(TimeSpan.FromHours(24));
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow render", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    private Task Command(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lifecycle)
        {
            if (_disposed != 0 || _terminated || _ready.Task.IsFaulted) return Task.FromException(new InvalidOperationException("Engine is stopped."));
            _commands.Enqueue(new(action, done));
        }
        return done.Task;
    }
    public void ResizePreview(int width, int height) { Volatile.Write(ref _previewWidth, width); Volatile.Write(ref _previewHeight, height); }
    public async Task LoadMediaAsync(string path)
    {
        await Ready.ConfigureAwait(false);
        var info = await Task.Run(() => MediaProbe.Read(path)).ConfigureAwait(false);
        MediaPlayback? loaded = null;
        await Command(() =>
        {
            _media?.Dispose(); _media = null; _video = null;
            Transport.Load(info.Duration);
            _media = loaded = new MediaPlayback(_gpu!, Transport, info);
            _media.AudioOffsetMilliseconds = _project.Audio.OffsetMilliseconds;
            _project = _project with { MediaPath = path };
            _localAudio?.Flush(); _audioCursorRevision = -1; _error = null;
        }).ConfigureAwait(false);
        await loaded!.Ready.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
    }
    public Task SetProjectAsync(ShowProject project) => Command(() => ApplyProject(project));
    public Task SetMidiArmedAsync(bool armed) => Command(() =>
    {
        _midiArmed = armed;
        try { ConfigureMidi(); }
        catch { _midiArmed = false; _mtcOut?.Dispose(); _mtcOut = null; _midiDmx?.Dispose(); _midiDmx = null; throw; }
    });
    public Task BlackoutDmxAsync() => Command(() => _dmx?.Blackout());
    public Task ResetDiagnosticsAsync() => Command(() => { _frames = _missed = 0; _renderTotal = _renderPeak = 0; Array.Clear(_phaseTotal); });
    public Task ReconnectRoutesAsync() => Command(() =>
    {
        _error = null; ConfigureMidi();
        foreach (var input in _networkInputs.Values) input.Dispose(); _networkInputs.Clear();
        foreach (var layer in _project.Layers.Where(l => l.Kind == LayerKind.Network))
            _networkInputs[layer.Id] = new(_gpu!, layer.NetworkProtocol, layer.NetworkSource!);
        _artnet?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _artnet = null;
        _artNetError = null;
        if (_project.ArtNet.Enabled)
        {
            try { _artnet = new(_dmx!, IPAddress.Parse(_project.ArtNet.BindAddress), _project.ArtNet.Port); _artnet.Start(); }
            catch (Exception ex) { _artNetError = "Art-Net: " + ex.Message; }
        }
        _localAudio?.Dispose(); _localAudio = null; _localAudio = new(_project.Audio.DeviceId);
        _localAudioError = null;
        _localAudio.Volume = _project.Audio.LocalMute ? 0 : _project.Audio.LocalVolume;
    });
    public void SetScrubbing(bool scrubbing) { if (_mtcOut is { } output) output.Suspended = scrubbing && !_project.Clock.SendLocateDuringScrub; }
    public Task ClearMediaAsync() => Command(() =>
    { _media?.Dispose(); _media = null; _video = null; _project = _project with { MediaPath = null }; Transport.Load(TimeSpan.FromHours(24)); _localAudio?.Flush(); });
    public Task StartStreamAsync() => Command(() =>
    {
        if (_encoder != null) return;
        var settings = _project.Stream;
        var store = new HlsSegmentStore(settings.PlaylistSegments, targetSeconds: settings.HlsTargetSeconds(_project.FpsNumerator, _project.FpsDenominator));
        if (!settings.HlsEnabled && !_project.NetworkOutputs.Any(o => o.Enabled)) throw new InvalidOperationException("Enable HLS or a network stream destination first.");
        var encoder = new HardwareStreamEncoder(_gpu!, store, _project.Width, _project.Height, _project.FpsNumerator, _project.FpsDenominator, settings, _project.NetworkOutputs);
        var server = new HlsServer();
        try { if (settings.HlsEnabled) server.StartAsync(store, IPAddress.Parse(settings.BindAddress), settings.Port, _webAssets).GetAwaiter().GetResult(); }
        catch { encoder.Dispose(); server.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        _segments = store; _encoder = encoder; _server = server; _streamStartTick = -1;
        _streamUrl = settings.HlsEnabled ? $"http://127.0.0.1:{settings.Port}/live/index.m3u8" : null; _error = null;
    });
    public Task StopStreamAsync() => Command(StopStreaming);
    public Task SaveFrameAsync(string path) => Command(() => FrameCapture.SavePng(_gpu!, _canvas!, path));
    private void StopStreaming()
    {
        try { _encoder?.Complete(); }
        finally
        {
            _encoder?.Dispose(); _encoder = null; _segments = null;
            _server?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _server = null; _streamUrl = null;
        }
    }
    private void ApplyProject(ShowProject project)
    {
        project.Validate();
        var previous = _project;
        try { ApplyProjectCore(project); }
        catch (Exception error)
        {
            if (_project != previous)
            {
                try { ApplyProjectCore(previous); }
                catch (Exception rollback) { throw new InvalidOperationException($"{error.Message} Previous routes also need reconnecting: {rollback.Message}", error); }
            }
            throw;
        }
    }
    private void ApplyProjectCore(ShowProject project)
    {
        var old = _project;
        bool canvasChanged = _canvas == null || old.Width != project.Width || old.Height != project.Height;
        if (_encoder != null && (canvasChanged || old.FpsNumerator != project.FpsNumerator || old.FpsDenominator != project.FpsDenominator || old.Stream != project.Stream || !old.NetworkOutputs.SequenceEqual(project.NetworkOutputs)))
            throw new InvalidOperationException("Stop the stream before changing output resolution, frame rate or encoder settings.");
        _project = project;
        SelectLayer(_selection?.Id);
        if (canvasChanged) { var replacement = new GpuSurface(_gpu!, project.Width, project.Height); _canvas?.Dispose(); _canvas = replacement; }
        bool dmxChanged = _dmx == null || old.ArtNet != project.ArtNet;
        if (dmxChanged)
        {
            _midiDmx?.Dispose(); _midiDmx = null;
            _artnet?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _artnet = null;
            _artNetError = null;
            _dmx = new DmxUniverseStore(project.ArtNet.FirstUniverse, project.ArtNet.UniverseCount); _dmxBytes = new byte[_dmx.Count * 512];
            foreach (var item in _grids.Values) { item.Renderer.Dispose(); item.Surface.Dispose(); } _grids.Clear();
            if (project.ArtNet.Enabled)
            {
                try { _artnet = new ArtNetReceiver(_dmx, IPAddress.Parse(project.ArtNet.BindAddress), project.ArtNet.Port); _artnet.Start(); }
                catch (Exception ex) { _artNetError = $"Art-Net: {ex.Message}"; }
            }
        }
        if (_localAudio == null || old.Audio.DeviceId != project.Audio.DeviceId)
        {
            _localAudio?.Dispose(); _localAudio = null;
            _localAudioError = null;
            try { _localAudio = new LocalAudioOutput(project.Audio.DeviceId); }
            catch (Exception ex) { _localAudioError = $"Local audio: {ex.Message}"; }
        }
        if (_localAudio != null) _localAudio.Volume = project.Audio.LocalMute ? 0 : project.Audio.LocalVolume;
        if (_media != null) _media.AudioOffsetMilliseconds = project.Audio.OffsetMilliseconds;
        if (old.Clock != project.Clock || !_ready.Task.IsCompleted)
        {
            if (old.Clock.Mode != project.Clock.Mode || old.Clock.Rate != project.Clock.Rate || old.Clock.OffsetFrames != project.Clock.OffsetFrames || !_ready.Task.IsCompleted)
                Transport.ConfigureClock(project.Clock.Mode, project.Clock.Rate, project.Clock.OffsetFrames);
            ConfigureMidi();
        }
        else if (dmxChanged || old.MidiDmx != project.MidiDmx) ConfigureMidiDmx();
        if (project.LoopEnabled && Transport.Snapshot().Duration.Ticks >= project.LoopOutTicks)
            Transport.SetLoop(true, TimeSpan.FromTicks(project.LoopInTicks), TimeSpan.FromTicks(project.LoopOutTicks));
        else Transport.SetLoop(false, TimeSpan.FromTicks(project.LoopInTicks), TimeSpan.FromTicks(project.LoopOutTicks));
        if (old.Spout != project.Spout || canvasChanged)
        {
            _spoutOut?.Dispose(); _spoutOut = null;
            if (project.Spout.OutputEnabled) _spoutOut = new(_gpu!, project.Spout.OutputName, project.Width, project.Height);
        }
        if (old.LanVideo != project.LanVideo || canvasChanged || old.FpsNumerator != project.FpsNumerator || old.FpsDenominator != project.FpsDenominator)
        {
            _lanOutput?.Dispose(); _lanOutput = null;
            if (project.LanVideo.NdiEnabled || project.LanVideo.OmtEnabled)
                _lanOutput = new(_gpu!, _canvas!, project.FpsNumerator, project.FpsDenominator, project.LanVideo);
        }
        foreach (var id in _spoutInputs.Keys.ToArray())
        {
            var layer = project.Layers.FirstOrDefault(l => l.Id == id);
            if (layer?.Kind != LayerKind.Spout || layer.SpoutSender != _spoutInputs[id].Sender)
            { _spoutInputs[id].Dispose(); _spoutInputs.Remove(id); }
        }
        foreach (var id in _networkInputs.Keys.ToArray())
        {
            var layer = project.Layers.FirstOrDefault(l => l.Id == id);
            if (layer?.Kind != LayerKind.Network || layer.NetworkSource != _networkInputs[id].Source || layer.NetworkProtocol != _networkInputs[id].Protocol)
            { _networkInputs[id].Dispose(); _networkInputs.Remove(id); }
        }
        foreach (var layer in project.Layers.Where(l => l.Kind == LayerKind.Network))
            if (!_networkInputs.ContainsKey(layer.Id)) _networkInputs[layer.Id] = new(_gpu!, layer.NetworkProtocol, layer.NetworkSource!);
        var ids = project.Layers.Where(l => l.Kind == LayerKind.Grid).Select(l => l.Id).ToHashSet();
        foreach (var id in _grids.Keys.Where(k => !ids.Contains(k)).ToArray())
        { var item = _grids[id]; item.Renderer.Dispose(); item.Surface.Dispose(); _grids.Remove(id); }
    }
    private void ConfigureMidi()
    {
        _mtcOut?.Dispose(); _mtcOut = null; _mtcIn?.Dispose(); _mtcIn = null;
        var clock = _project.Clock;
        if (clock.Mode == ClockMode.IncomingMtc && !string.IsNullOrEmpty(clock.MidiInput))
        {
            var input = MidiDevices.Inputs().FirstOrDefault(d => d.Name == clock.MidiInput) ?? throw new InvalidOperationException("Selected MTC input is disconnected.");
            _mtcIn = new MtcInput(Transport, input.Id);
        }
        if (_midiArmed && clock.OutputEnabled && clock.Mode == ClockMode.Internal && !string.IsNullOrEmpty(clock.MidiOutput))
        {
            var output = MidiDevices.Outputs().FirstOrDefault(d => d.Name == clock.MidiOutput) ?? throw new InvalidOperationException("Selected MTC output is disconnected.");
            _mtcOut = new MtcOutput(Transport, output.Id, clock.OutputOffsetMilliseconds);
        }
        ConfigureMidiDmx();
    }
    private void ConfigureMidiDmx()
    {
        _midiDmx?.Dispose(); _midiDmx = null;
        var settings = _project.MidiDmx;
        if (!_midiArmed || !settings.Enabled || string.IsNullOrEmpty(settings.OutputDevice)) return;
        if (_project.Clock.OutputEnabled && _project.Clock.MidiOutput == settings.OutputDevice)
            throw new InvalidOperationException("Use separate MIDI ports for grandMA2 MTC and gridnode MIDI-DMX.");
        var output = MidiDevices.Outputs().FirstOrDefault(d => d.Name == settings.OutputDevice) ?? throw new InvalidOperationException("Selected MIDI-DMX output is disconnected.");
        _midiDmx = new(_dmx!, output.Id, settings);
    }
    private void Run()
    {
        WinMidi.timeBeginPeriod(1);
        try
        {
            FFmpegRuntime.Initialize(_runtime);
            ProtocolRuntime.Initialize(_runtime);
            var preferred = GpuDevice.EnumerateAdapters().FirstOrDefault(g => g.Name == _project.GpuAdapter);
            _gpu = new GpuDevice(preferred?.Index); _compositor = new(_gpu); _glyphs = new(_gpu); _converter = new(_gpu);
            _preview = new(_gpu, _window, Math.Max(1, _previewWidth), Math.Max(1, _previewHeight));
            ApplyProject(_project); _ready.TrySetResult();
            double next = Stopwatch.GetTimestamp();
            while (!_stop.IsSet)
            {
                while (_commands.TryDequeue(out var command))
                {
                    try { command.Action(); command.Completion.TrySetResult(); }
                    catch (Exception ex) { _error = ex.Message; command.Completion.TrySetException(ex); }
                }
                double interval = Stopwatch.Frequency * (double)_project.FpsDenominator / _project.FpsNumerator;
                double now = Stopwatch.GetTimestamp();
                if (now < next) { _stop.Wait(1); continue; }
                int elapsedFrames = Math.Max(1, (int)Math.Min(100000, Math.Floor((now - next) / interval) + 1));
                _missed += elapsedFrames - 1;
                next += elapsedFrames * interval;
                _outputTick += elapsedFrames - 1;
                if (elapsedFrames * interval > Stopwatch.Frequency * 0.45 && _encoder != null && _streamStartTick >= 0)
                { _error = "Stream stopped: the render device stalled for more than 450 ms. Reduce output load before restarting."; StopStreaming(); }
                if (_streamStartTick == -1) { _streamStartTick = _outputTick; elapsedFrames = 1; }
                long began = Stopwatch.GetTimestamp();
                Render(elapsedFrames); _outputTick++;
                double ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                if ((ms > interval * 1000 / Stopwatch.Frequency || elapsedFrames > 1) && _slowFrames.Count < 256)
                    _slowFrames.Enqueue(new(DateTime.UtcNow, ms, elapsedFrames - 1, (double[])_phaseCurrent.Clone(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)));
                _frames++;
                _renderTotal += ms; _renderPeak = Math.Max(_renderPeak, ms);
                if (_frames % 6 == 0)
                    Volatile.Write(ref _status, new(_gpu.Adapter.Name, ms, _frames, _missed, _encoder != null, _streamUrl,
                        _dmx?.Accepted ?? 0, _media?.AudioUnderruns ?? 0, _error ?? _artNetError ?? _localAudioError ?? _mtcOut?.Error ?? _mtcIn?.Error ?? _midiDmx?.Error ?? _artnet?.LastError ?? _localAudio?.Error, _media?.Error,
                        _midiDmx?.Status ?? "Off", _spoutOut?.Name, _spoutInputs.Values.Select(s => s.Error).FirstOrDefault(s => s != null), _renderTotal / _frames, _renderPeak,
                        string.Join(", ", _phaseTotal.Select((sum, i) => $"{new[] { "video", "layers", "spout", "audio", "encode", "preview" }[i]}={sum / _frames:0.000}")) + " encoder(pool/copy/send/drain/audio)=" + _encoder?.TimingSummary,
                        _encoder?.NetworkStatus + " " + _lanOutput?.Status + "\n" + string.Join("\n", _networkInputs.Values.Select(input => $"{input.Protocol} in: {input.Frames} frames · {input.Error ?? (input.Frames > 0 ? "receiving" : "waiting for source")}"))));
            }
        }
        catch (Exception ex) { _error = ex.ToString(); _ready.TrySetException(ex); Volatile.Write(ref _status, _status with { Error = _error }); }
        finally
        {
            lock (_lifecycle)
            {
                _terminated = true;
                while (_commands.TryDequeue(out var pending)) pending.Completion.TrySetException(new InvalidOperationException("Engine stopped."));
            }
            try { StopStreaming(); } catch { }
            _media?.Dispose(); _mtcIn?.Dispose(); _mtcOut?.Dispose(); _midiDmx?.Dispose(); _localAudio?.Dispose();
            _spoutOut?.Dispose(); foreach (var input in _spoutInputs.Values) input.Dispose();
            _lanOutput?.Dispose();
            foreach (var input in _networkInputs.Values) input.Dispose();
            _artnet?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            foreach (var grid in _grids.Values) { grid.Renderer.Dispose(); grid.Surface.Dispose(); }
            _preview?.Dispose(); _converter?.Dispose(); _glyphs?.Dispose(); _compositor?.Dispose(); _canvas?.Dispose(); _gpu?.Dispose();
            WinMidi.timeEndPeriod(1);
        }
    }
    private unsafe void Render(int elapsedFrames)
    {
        long phase = Stopwatch.GetTimestamp();
        var snapshot = Transport.Snapshot();
        if (_media != null && _media.TryGetVideo(snapshot, out var frame))
        { using (frame) _video = _converter!.Convert(frame.Native); }
        RecordPhase(0, ref phase);
        _gpu!.Context.ClearRenderTargetView(_canvas!.Target, new Color4(0, 0, 0, 1));
        long dmxRevision = _dmx!.CopyTo(_dmxBytes);
        string timecode = snapshot.Timecode.ToString();
        foreach (var layer in _project.Layers)
        {
            if (!layer.Visible) continue;
            var rect = new Vector4((float)layer.X, (float)layer.Y, (float)layer.Width, (float)layer.Height);
            if (layer.Kind == LayerKind.Video && _video != null)
            {
                var uv = new Vector4((float)layer.CropLeft / _video.Width, (float)layer.CropTop / _video.Height,
                    1f - (float)(layer.CropLeft + layer.CropRight) / _video.Width, 1f - (float)(layer.CropTop + layer.CropBottom) / _video.Height);
                if (uv.Z > 0 && uv.W > 0) _compositor!.Draw(_canvas, _video.View, rect, uv, (float)layer.Opacity);
            }
            else if (layer.Kind == LayerKind.Grid && layer.Grid is { } layout)
            {
                if (!_grids.TryGetValue(layer.Id, out var grid) || grid.Layout != layout)
                {
                    if (grid.Renderer != null) { grid.Renderer.Dispose(); grid.Surface.Dispose(); }
                    grid = (new GridRenderer(_gpu, layout.UniverseCount), new GpuSurface(_gpu, layout.Width, layout.Height), -1, layout);
                }
                if (grid.Revision != dmxRevision)
                {
                    int offset = Math.Clamp((layout.FirstUniverse - _dmx.FirstUniverse) * 512, 0, _dmxBytes.Length);
                    int available = _project.ArtNet.AutomaticUniverseCount ? _dmx.ActiveChannelCount : _dmxBytes.Length;
                    grid.Renderer.Upload(layout, _dmxBytes.AsSpan(offset, Math.Max(0, available - offset)));
                    _gpu.Context.ClearRenderTargetView(grid.Surface.Target, new Color4(0, 0, 0, 0));
                    grid.Renderer.Draw(grid.Surface); grid.Revision = dmxRevision;
                }
                _grids[layer.Id] = grid;
                _compositor!.Draw(_canvas, grid.Surface.View, rect, new(0, 0, 1, 1), (float)layer.Opacity, point: true);
            }
            else if (layer.Kind == LayerKind.Timecode)
            {
                Vector4 background = ParseColor(layer.BackgroundColor); background.W *= (float)layer.Opacity;
                _compositor!.Fill(_canvas, rect, background);
                Vector4 color = ParseColor(layer.TextColor); color.W *= (float)layer.Opacity;
                _glyphs!.Draw(_canvas, (layer.Label.Length == 0 ? "" : layer.Label + " ") + timecode,
                    (float)layer.X, (float)layer.Y, layer.FontFamily, layer.FontSize, color);
            }
            else if (layer.Kind == LayerKind.Spout && layer.SpoutSender is { } name)
            {
                if (!_spoutInputs.TryGetValue(layer.Id, out var receiver)) _spoutInputs[layer.Id] = receiver = new(_gpu, name);
                if (receiver.Receive() is { } view)
                {
                    var uv = new Vector4((float)layer.CropLeft / receiver.Width, (float)layer.CropTop / receiver.Height,
                        1f - (float)(layer.CropLeft + layer.CropRight) / receiver.Width, 1f - (float)(layer.CropTop + layer.CropBottom) / receiver.Height);
                    if (uv.Z > 0 && uv.W > 0) _compositor!.Draw(_canvas, view, rect, uv, (float)layer.Opacity, point: true);
                }
            }
            else if (layer.Kind == LayerKind.Network && _networkInputs.TryGetValue(layer.Id, out var input) && input.GetFrame() is { } live)
            {
                var uv = new Vector4((float)layer.CropLeft / live.Width, (float)layer.CropTop / live.Height,
                    1f - (float)(layer.CropLeft + layer.CropRight) / live.Width, 1f - (float)(layer.CropTop + layer.CropBottom) / live.Height);
                if (uv.Z > 0 && uv.W > 0) _compositor!.Draw(_canvas, live.View, rect, uv, (float)layer.Opacity);
            }
        }
        RecordPhase(1, ref phase);
        _spoutOut?.Send(_canvas);
        RecordPhase(2, ref phase);
        long audioEnd = (_outputTick + 1) * 48000L * _project.FpsDenominator / _project.FpsNumerator;
        int nominalAudioFrames = (int)(audioEnd - _outputTick * 48000L * _project.FpsDenominator / _project.FpsNumerator);
        int audioFrames = (int)Math.Min(_audioScratch.Length / 2, audioEnd - (_outputTick + 1 - elapsedFrames) * 48000L * _project.FpsDenominator / _project.FpsNumerator);
        Span<float> pcm = _audioScratch.AsSpan(0, audioFrames * 2);
        bool naturalLoop = snapshot.TimelineRevision != _audioCursorRevision && snapshot.Mode == ClockMode.Internal && snapshot.LoopEnabled &&
            snapshot.State == TransportState.Playing && _lastState == TransportState.Playing &&
            (_audioWrapped || _audioCursor >= snapshot.LoopOut.TotalSeconds * 48000 - 2400) &&
            snapshot.Position < snapshot.LoopIn + TimeSpan.FromMilliseconds(50);
        if (snapshot.TimelineRevision != _audioCursorRevision || snapshot.State != _lastState)
        {
            _audioCursorRevision = snapshot.TimelineRevision;
            if (!naturalLoop) { _audioCursor = snapshot.Position.TotalSeconds * 48000; _localAudio?.Flush(); }
            _audioWrapped = false;
            _lastState = snapshot.State;
        }
        double expectedAudio = snapshot.Position.TotalSeconds * 48000 - (audioFrames - nominalAudioFrames);
        // Keep audio tied to the master monotonic clock after a late frame or incoming MTC correction.
        // Under steady playback, contiguous samples are preserved; small scheduling jitter is ignored.
        if (!naturalLoop && snapshot.State == TransportState.Playing && Math.Abs(_audioCursor - expectedAudio) > 960)
            _audioCursor = expectedAudio;
        var audioSnapshot = snapshot with { Position = TimeSpan.FromSeconds(_audioCursor / 48000) };
        if (_media != null) _media.ReadAudio(audioSnapshot, pcm, _project.Audio.OffsetMilliseconds); else pcm.Clear();
        if (snapshot.State == TransportState.Playing) _audioCursor += audioFrames;
        if (snapshot.Mode == ClockMode.Internal && snapshot.LoopEnabled && _audioCursor >= snapshot.LoopOut.TotalSeconds * 48000)
        {
            _audioCursor = snapshot.LoopIn.TotalSeconds * 48000 +
                (_audioCursor - snapshot.LoopIn.TotalSeconds * 48000) % ((snapshot.LoopOut - snapshot.LoopIn).TotalSeconds * 48000);
            _audioWrapped = true;
        }
        foreach (var layer in _project.Layers)
            if (layer.Kind == LayerKind.Network && _networkInputs.TryGetValue(layer.Id, out var input)) input.MixAudio(pcm, layer.LiveAudioEnabled ? layer.LiveAudioVolume : 0);
        _localAudio?.Write(pcm[^Math.Min(pcm.Length, nominalAudioFrames * 2)..]);
        _lanOutput?.Submit(_canvas, _outputTick, pcm, _project.Audio.StreamMute ? 0 : _project.Audio.StreamVolume);
        RecordPhase(3, ref phase);
        if (_encoder != null)
        {
            try { _encoder.SubmitVideo(_canvas, _outputTick - _streamStartTick); _encoder.SubmitAudio(pcm, _project.Audio.StreamMute ? 0 : _project.Audio.StreamVolume); }
            catch (Exception ex) { _error = $"Stream stopped: {ex.Message}"; StopStreaming(); }
        }
        RecordPhase(4, ref phase);
        if ((_frames & 1) == 0)
        {
            _preview!.Resize(Volatile.Read(ref _previewWidth), Volatile.Read(ref _previewHeight));
            _preview.Present(_compositor!, _canvas, Volatile.Read(ref _selection));
        }
        RecordPhase(5, ref phase);
    }
    private void RecordPhase(int index, ref long start)
    { long now = Stopwatch.GetTimestamp(); _phaseCurrent[index] = (now - start) * 1000.0 / Stopwatch.Frequency; _phaseTotal[index] += _phaseCurrent[index]; start = now; }
    private static Vector4 ParseColor(string text)
    {
        uint argb = Convert.ToUInt32(text.TrimStart('#'), 16);
        if (text.TrimStart('#').Length == 6) argb |= 0xff000000;
        return new(((argb >> 16) & 255) / 255f, ((argb >> 8) & 255) / 255f, (argb & 255) / 255f, (argb >> 24) / 255f);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set(); _thread.Join(); _stop.Dispose();
    }
}
