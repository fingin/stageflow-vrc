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
using Vortice.Mathematics;

namespace StageFlow.Engine;

public sealed record EngineStatus(string Gpu, double RenderMilliseconds, long RenderedFrames, long MissedDeadlines,
    bool Streaming, string? StreamUrl, long ArtNetPackets, long AudioUnderruns, string? Error, string? MediaError);

/// <summary>Owns native resource lifetime and presentation. GUI communicates through commands and immutable projects.</summary>
public sealed class ShowEngine : IDisposable
{
    private sealed record WorkerCommand(Action Action, TaskCompletionSource Completion);
    private readonly ConcurrentQueue<WorkerCommand> _commands = new();
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
    private HlsServer? _server;
    private HardwareStreamEncoder? _encoder;
    private HlsSegmentStore? _segments;
    private string? _streamUrl, _error;
    private long _frames, _missed, _audioCursorRevision = -1;
    private double _audioCursor;
    private TransportState _lastState;
    private readonly float[] _audioScratch = new float[96000];
    private bool _midiArmed;
    public Transport Transport { get; } = new();
    public Task Ready => _ready.Task;
    public EngineStatus Status => Volatile.Read(ref _status);
    public ShowProject Project => Volatile.Read(ref _project);
    public ShowEngine(nint previewHwnd, int width, int height, ShowProject project, string runtimeDirectory, string webAssetsDirectory)
    {
        _window = previewHwnd; _previewWidth = width; _previewHeight = height; _project = project;
        _runtime = runtimeDirectory; _webAssets = webAssetsDirectory;
        _thread = new Thread(Run) { IsBackground = true, Name = "StageFlow render", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }
    private Task Command(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_stop.IsSet || _terminated || _ready.Task.IsFaulted) return Task.FromException(new InvalidOperationException("Engine is stopped."));
        _commands.Enqueue(new(action, done));
        return done.Task;
    }
    public void ResizePreview(int width, int height) { Volatile.Write(ref _previewWidth, width); Volatile.Write(ref _previewHeight, height); }
    public async Task LoadMediaAsync(string path)
    {
        await Ready.ConfigureAwait(false);
        var info = await Task.Run(() => MediaProbe.Read(path)).ConfigureAwait(false);
        await Command(() =>
        {
            _media?.Dispose(); _media = null; _video = null;
            Transport.Load(info.Duration);
            _media = new MediaPlayback(_gpu!, Transport, info);
            _project = _project with { MediaPath = path };
            _localAudio?.Flush(); _audioCursorRevision = -1; _error = null;
        }).ConfigureAwait(false);
    }
    public Task SetProjectAsync(ShowProject project) => Command(() => ApplyProject(project));
    public Task SetMidiArmedAsync(bool armed) => Command(() => { _midiArmed = armed; ConfigureMidi(); });
    public void SetScrubbing(bool scrubbing) { if (_mtcOut is { } output) output.Suspended = scrubbing && !_project.Clock.SendLocateDuringScrub; }
    public Task ClearMediaAsync() => Command(() =>
    { _media?.Dispose(); _media = null; _video = null; Transport.Load(TimeSpan.Zero); _localAudio?.Flush(); });
    public Task StartStreamAsync() => Command(() =>
    {
        if (_encoder != null) return;
        var settings = _project.Stream;
        var store = new HlsSegmentStore(settings.PlaylistSegments, targetSeconds: settings.SegmentSeconds);
        var encoder = new HardwareStreamEncoder(_gpu!, store, _project.Width, _project.Height, _project.FpsNumerator, _project.FpsDenominator, settings);
        var server = new HlsServer();
        try { server.StartAsync(store, IPAddress.Parse(settings.BindAddress), settings.Port, _webAssets).GetAwaiter().GetResult(); }
        catch { encoder.Dispose(); server.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
        _segments = store; _encoder = encoder; _server = server;
        _streamUrl = $"http://127.0.0.1:{settings.Port}/live/index.m3u8"; _error = null;
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
        var old = _project;
        bool canvasChanged = _canvas == null || old.Width != project.Width || old.Height != project.Height;
        if (_encoder != null && (canvasChanged || old.FpsNumerator != project.FpsNumerator || old.FpsDenominator != project.FpsDenominator || old.Stream != project.Stream))
            throw new InvalidOperationException("Stop the stream before changing output resolution, frame rate or encoder settings.");
        _project = project;
        if (canvasChanged) { _canvas?.Dispose(); _canvas = new GpuSurface(_gpu!, project.Width, project.Height); }
        if (_dmx == null || old.ArtNet != project.ArtNet)
        {
            _artnet?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _artnet = null;
            _dmx = new DmxUniverseStore(project.ArtNet.FirstUniverse, project.ArtNet.UniverseCount); _dmxBytes = new byte[_dmx.Count * 512];
            foreach (var item in _grids.Values) { item.Renderer.Dispose(); item.Surface.Dispose(); } _grids.Clear();
            if (project.ArtNet.Enabled)
            {
                try { _artnet = new ArtNetReceiver(_dmx, IPAddress.Parse(project.ArtNet.BindAddress), project.ArtNet.Port); _artnet.Start(); }
                catch (Exception ex) { _error = $"Art-Net: {ex.Message}"; }
            }
        }
        if (_localAudio == null || old.Audio.DeviceId != project.Audio.DeviceId)
        {
            _localAudio?.Dispose(); _localAudio = null;
            try { _localAudio = new LocalAudioOutput(project.Audio.DeviceId); }
            catch (Exception ex) { _error = $"Local audio: {ex.Message}"; }
        }
        if (_localAudio != null) _localAudio.Volume = project.Audio.LocalMute ? 0 : project.Audio.LocalVolume;
        if (old.Clock != project.Clock || !_ready.Task.IsCompleted)
        {
            Transport.ConfigureClock(project.Clock.Mode, project.Clock.Rate, project.Clock.OffsetFrames); ConfigureMidi();
        }
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
    }
    private void Run()
    {
        WinMidi.timeBeginPeriod(1);
        try
        {
            FFmpegRuntime.Initialize(_runtime);
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
                if (now - next > interval) { _missed += (long)((now - next) / interval); next = now; }
                next += interval;
                long began = Stopwatch.GetTimestamp();
                Render();
                double ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                _frames++;
                Volatile.Write(ref _status, new(_gpu.Adapter.Name, ms, _frames, _missed, _encoder != null, _streamUrl,
                    _dmx?.Accepted ?? 0, _media?.AudioUnderruns ?? 0, _error ?? _mtcOut?.Error ?? _mtcIn?.Error ?? _artnet?.LastError, _media?.Error));
            }
        }
        catch (Exception ex) { _error = ex.ToString(); _ready.TrySetException(ex); Volatile.Write(ref _status, _status with { Error = _error }); }
        finally
        {
            _terminated = true;
            while (_commands.TryDequeue(out var pending)) pending.Completion.TrySetException(new InvalidOperationException("Engine stopped."));
            try { StopStreaming(); } catch { }
            _media?.Dispose(); _mtcIn?.Dispose(); _mtcOut?.Dispose(); _localAudio?.Dispose();
            _artnet?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            foreach (var grid in _grids.Values) { grid.Renderer.Dispose(); grid.Surface.Dispose(); }
            _preview?.Dispose(); _converter?.Dispose(); _glyphs?.Dispose(); _compositor?.Dispose(); _canvas?.Dispose(); _gpu?.Dispose();
            WinMidi.timeEndPeriod(1);
        }
    }
    private unsafe void Render()
    {
        var snapshot = Transport.Snapshot();
        if (_media != null && _media.TryGetVideo(snapshot, out var frame))
        { using (frame) _video = _converter!.Convert(frame.Native); }
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
                _compositor!.Draw(_canvas, _video.View, rect, uv, (float)layer.Opacity);
            }
            else if (layer.Kind == LayerKind.Grid && layer.Grid is { } layout)
            {
                if (!_grids.TryGetValue(layer.Id, out var grid) || grid.Layout != layout)
                {
                    if (grid.Renderer != null) { grid.Renderer.Dispose(); grid.Surface.Dispose(); }
                    grid = (new GridRenderer(_gpu), new GpuSurface(_gpu, layout.Width, layout.Height), -1, layout);
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
                Vector4 color = ParseColor(layer.TextColor); color.W *= (float)layer.Opacity;
                _glyphs!.Draw(_canvas, (layer.Label.Length == 0 ? "" : layer.Label + " ") + timecode,
                    (float)layer.X, (float)layer.Y, layer.FontFamily, layer.FontSize, color);
            }
        }
        int audioFrames = (int)((_frames + 1) * 48000L * _project.FpsDenominator / _project.FpsNumerator -
            _frames * 48000L * _project.FpsDenominator / _project.FpsNumerator);
        Span<float> pcm = _audioScratch.AsSpan(0, audioFrames * 2);
        if (snapshot.TimelineRevision != _audioCursorRevision || snapshot.State != _lastState)
        {
            _audioCursorRevision = snapshot.TimelineRevision; _audioCursor = snapshot.Position.TotalSeconds * 48000;
            _localAudio?.Flush(); _lastState = snapshot.State;
        }
        var audioSnapshot = snapshot with { Position = TimeSpan.FromSeconds(_audioCursor / 48000) };
        if (_media != null) _media.ReadAudio(audioSnapshot, pcm, _project.Audio.OffsetMilliseconds); else pcm.Clear();
        if (snapshot.State == TransportState.Playing) _audioCursor += audioFrames;
        _localAudio?.Write(pcm);
        if (_encoder != null)
        {
            try { _encoder.SubmitVideo(_canvas); _encoder.SubmitAudio(pcm, _project.Audio.StreamMute ? 0 : _project.Audio.StreamVolume); }
            catch (Exception ex) { _error = $"Stream stopped: {ex.Message}"; StopStreaming(); }
        }
        if ((_frames & 1) == 0)
        {
            _preview!.Resize(Volatile.Read(ref _previewWidth), Volatile.Read(ref _previewHeight));
            _preview.Present(_compositor!, _canvas);
        }
    }
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
