using System.Diagnostics;
using System.Text.Json;
using StageFlow.Engine;

namespace StageFlow.App;

/// <summary>One-second diagnostics on a background thread; never writes from the render/UI thread.</summary>
internal sealed class PerformanceRecorder : IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _writer;
    private readonly Func<ShowEngine?> _engine;
    private string? _error;
    public string Folder { get; }
    public string FilePath { get; }
    public string? Error => Volatile.Read(ref _error);
    public PerformanceRecorder(string folder, Func<ShowEngine?> engine)
    {
        Folder = Path.GetFullPath(folder); Directory.CreateDirectory(Folder); _engine = engine;
        FilePath = Path.Combine(Folder, $"StageFlow-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl");
        _writer = Task.Run(Record);
    }
    private async Task Record()
    {
        try
        {
            await using var file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384, FileOptions.Asynchronous);
            await using var text = new StreamWriter(file);
            using var process = Process.GetCurrentProcess();
            var clock = Stopwatch.StartNew();
            double lastTime = 0; TimeSpan lastCpu = process.TotalProcessorTime;
            long lastFrames = 0, lastMissed = 0, lastPackets = 0;
            await text.WriteLineAsync(JsonSerializer.Serialize(new { Type = "session", Utc = DateTime.UtcNow,
                Version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion, Runtime = Environment.Version.ToString(),
                OS = Environment.OSVersion.ToString(), Processors = Environment.ProcessorCount,
                Note = "Frame metrics measure CPU submission/wait time, not GPU timestamp queries. No media/audio payload is recorded." }));
            using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await cadence.WaitForNextTickAsync(_cancel.Token))
            {
                var engine = _engine(); if (engine == null) continue;
                var status = engine.Status; var project = engine.Project;
                var transport = engine.Transport.Snapshot(); var media = engine.MediaInfo;
                process.Refresh(); double now = clock.Elapsed.TotalSeconds, seconds = now - lastTime;
                TimeSpan cpu = process.TotalProcessorTime;
                await text.WriteLineAsync(JsonSerializer.Serialize(new { Type = "sample", Utc = DateTime.UtcNow, ElapsedSeconds = now,
                    FramesPerSecond = Math.Max(0, status.RenderedFrames - lastFrames) / seconds,
                    MissedInInterval = Math.Max(0, status.MissedDeadlines - lastMissed),
                    ArtNetPacketsPerSecond = Math.Max(0, status.ArtNetPackets - lastPackets) / seconds,
                    CpuPercentOfOneCore = (cpu - lastCpu).TotalSeconds / seconds * 100,
                    WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                    ManagedBytes = GC.GetTotalMemory(false), Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2),
                    Width = project.Width, Height = project.Height, project.FpsNumerator, project.FpsDenominator,
                    LayerCount = project.Layers.Count, VisibleLayers = project.Layers.Count(l => l.Visible),
                    ClockLayers = project.Layers.Count(l => l.Visible && l.Kind == StageFlow.Core.Projects.LayerKind.Timecode),
                    StreamBitrateKbps = project.Stream.VideoBitrateKbps, project.ArtNet.UniverseCount,
                    Media = media == null ? null : new { Name = Path.GetFileName(media.Path), media.Width, media.Height, media.VideoCodec, media.AudioCodec },
                    PositionSeconds = transport.Position.TotalSeconds, State = transport.State.ToString(), ClockMode = transport.Mode.ToString(),
                    Status = status, SlowFrames = engine.DrainSlowFrames() }));
                await text.FlushAsync(_cancel.Token);
                lastTime = now; lastCpu = cpu; lastFrames = status.RenderedFrames; lastMissed = status.MissedDeadlines; lastPackets = status.ArtNetPackets;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Volatile.Write(ref _error, ex.Message); }
    }
    public void Dispose() { _cancel.Cancel(); _writer.GetAwaiter().GetResult(); _cancel.Dispose(); }
}
