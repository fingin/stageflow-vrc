using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using StageFlow.Core.ArtNet;
using StageFlow.Core.Projects;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    // Opt-in integration harness: uses the real GUI, native engine and loopback network.
    // Never opens MIDI outputs and keeps local audio muted.
    private async Task RunSmoke()
    {
        string folder = Path.GetFullPath(_args[3]); Directory.CreateDirectory(folder);
        var samples = new List<object>();
        using var trafficStop = new CancellationTokenSource();
        Task? traffic = null;
        try
        {
            var imported = HnodeImporter.Import(File.ReadAllText(_args[2]));
            var project = imported.Project with
            {
                MediaPath = _args[1], Audio = new() { LocalMute = true },
                ArtNet = imported.Project.ArtNet with { BindAddress = "127.0.0.1", Port = 16454, Enabled = true },
                Stream = new() { BindAddress = "127.0.0.1", Port = 18888 },
                MidiDmx = new() { Enabled = false }, Clock = new(), Spout = new() { OutputEnabled = true, OutputName = "StageFlow QA Program" }
            };
            project = project with { Layers = [.. project.Layers, .. Enumerable.Range(0, 12).Select(i => new Layer
            { Name = $"Clock {i + 1}", Kind = LayerKind.Timecode, Label = $"CLOCK {i + 1}", FontSize = 40,
                X = 60 + i % 3 * 800, Y = 480 + i / 3 * 150, Width = 740, Height = 65 })] };
            await _engine!.SetProjectAsync(project); await _engine.LoadMediaAsync(_args[1]);
            _project = project; _history = new(project); RefreshProjectUi();
            int initialLayers = _project.Layers.Count;
            await AddClock();
            if (_project.Layers.Count != initialLayers + 1) throw new Exception("GUI add-clock failed.");
            await Undo(); if (_project.Layers.Count != initialLayers) throw new Exception("GUI undo failed.");
            await Redo(); if (_project.Layers.Count != initialLayers + 1) throw new Exception("GUI redo failed.");
            await RemoveLayer(); if (_project.Layers.Count != initialLayers) throw new Exception("GUI remove-layer failed.");
            ProjectStore.Save(project with { LoopEnabled = true, LoopOutTicks = _engine.Transport.Snapshot().Duration.Ticks }, Path.Combine(folder, "smoke.stageflow"));
            using var socket = new UdpClient();
            byte[] dmx = new byte[512]; new Random(1733).NextBytes(dmx);
            int universeCount = _args.Length > 5 ? Math.Clamp(int.Parse(_args[5]), 1, project.ArtNet.UniverseCount) : 3;
            traffic = Task.Run(async () =>
            {
                using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 60));
                byte sequence = 1; byte[] packet = new byte[530];
                while (await cadence.WaitForNextTickAsync(trafficStop.Token))
                {
                    dmx[0]++; dmx[257] = (byte)(255 - dmx[0]);
                    for (int universe = 0; universe < universeCount; universe++)
                    {
                        ArtNetPacket.WriteDmx(packet, (ushort)(project.ArtNet.FirstUniverse + universe), sequence, dmx);
                        await socket.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, 16454), trafficStop.Token);
                    }
                    sequence = sequence == 255 ? (byte)1 : (byte)(sequence + 1);
                }
            });
            await Task.Delay(500);
            _engine.Transport.SetLoop(true, TimeSpan.Zero, _engine.Transport.Snapshot().Duration);
            await _engine.StartStreamAsync(); _engine.Transport.Play();
            var timer = Stopwatch.StartNew();
            using var http = new HttpClient();
            int duration = _args.Length > 4 ? Math.Clamp(int.Parse(_args[4]), 30, 600) : 30;
            double baselineSeconds = 0; TimeSpan baselineCpu = default; long baselineMemory = 0;
            for (int second = 0; second < duration; second++)
            {
                await Task.Delay(1000);
                if (traffic.IsFaulted) await traffic;
                var status = _engine.Status; samples.Add(status);
                if (status.Error != null || status.MediaError != null) throw new Exception(status.Error ?? status.MediaError);
                if (second == 6)
                {
                    _engine.Transport.Pause(); await Task.Delay(120);
                    await _engine.SaveFrameAsync(Path.Combine(folder, "program.png"));
                    using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.Combine(folder, "interface.png"), ImageFormat.Png);
                    _engine.Transport.Seek(TimeSpan.FromSeconds(2.3)); await Task.Delay(120); _engine.Transport.Play();
                }
                if (second == 12)
                {
                    var videoMonitor = _engine.Status.Video ?? throw new Exception("Engine monitor telemetry missing.");
                    if (!videoMonitor.EncoderRunning || videoMonitor.EncodedFrames <= 0 || videoMonitor.Routes.Single(r => r.Id == "hls").State != StageFlow.Core.Monitoring.VideoRouteState.Live ||
                        videoMonitor.Routes.Single(r => r.Id == "spout").Frames <= 0) throw new Exception("Monitor does not report live HLS/encoder/Spout.");
                    _inspectorTabs.SelectedIndex = 1; RefreshMonitor(force: true);
                    if (SelectedMonitorRoute()?.Id != "hls" || !_monitorOpen.Enabled || _monitorAddress.Text != "http://127.0.0.1:18888/") throw new Exception("Monitor did not expose live HLS links on first open.");
                    await Task.Delay(50); // Let newly visible owner-drawn controls paint before capture.
                    using (var monitorImage = new Bitmap(Width, Height)) { DrawToBitmap(monitorImage, new Rectangle(0, 0, Width, Height)); monitorImage.Save(Path.Combine(folder, "monitor.png"), ImageFormat.Png); }
                    var playlist = await http.GetStringAsync("http://127.0.0.1:18888/live/index.m3u8");
                    if (!playlist.Contains("#EXTINF")) throw new Exception("Live HLS has no segments.");
                    File.WriteAllText(Path.Combine(folder, "live.m3u8"), playlist);
                    foreach (string segment in playlist.Split('\n').Where(s => s.EndsWith(".ts")))
                        File.WriteAllBytes(Path.Combine(folder, segment.Trim()), await http.GetByteArrayAsync("http://127.0.0.1:18888/live/" + segment.Trim()));
                }
                if (second == 14)
                {
                    await _engine.ResetDiagnosticsAsync(); samples.Clear(); baselineSeconds = timer.Elapsed.TotalSeconds;
                    using var process = Process.GetCurrentProcess(); baselineCpu = process.TotalProcessorTime; baselineMemory = process.WorkingSet64;
                }
            }
            var final = _engine.Status;
            using var currentProcess = Process.GetCurrentProcess();
            double measuredSeconds = timer.Elapsed.TotalSeconds - baselineSeconds;
            double cpuCorePercent = (currentProcess.TotalProcessorTime - baselineCpu).TotalSeconds / measuredSeconds * 100;
            await _engine.StopStreamAsync();
            bool passed = final.MissedDeadlines <= measuredSeconds * 60 * 0.01 && final.AudioUnderruns == 0;
            File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
            { Passed = passed, DynamicArtNetUniverses = universeCount, Elapsed = timer.Elapsed.TotalSeconds, MeasuredSeconds = measuredSeconds, CpuPercentOfOneCore = cpuCorePercent,
                BaselineWorkingSetBytes = baselineMemory, FinalWorkingSetBytes = currentProcess.WorkingSet64,
                Final = final, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new Exception("Playback exceeded the 1% late-frame limit or encountered an audio-buffer underrun.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
            File.WriteAllText(Path.Combine(folder, "failure-status.json"), JsonSerializer.Serialize(new { Status = _engine?.Status, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 1;
        }
        finally
        {
            await trafficStop.CancelAsync();
            if (traffic != null) { try { await traffic; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { } }
            _dirty = false; Close();
        }
    }
}
