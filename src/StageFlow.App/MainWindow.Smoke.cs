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
        try
        {
            var imported = HnodeImporter.Import(File.ReadAllText(_args[2]));
            var project = imported.Project with
            {
                MediaPath = _args[1], Audio = new() { LocalMute = true },
                ArtNet = imported.Project.ArtNet with { BindAddress = "127.0.0.1", Port = 16454, Enabled = true },
                Stream = new() { BindAddress = "127.0.0.1", Port = 18888 },
                MidiDmx = new() { Enabled = false }, Clock = new()
            };
            project = project with { Layers = [.. project.Layers, .. Enumerable.Range(0, 12).Select(i => new Layer
            { Name = $"Clock {i + 1}", Kind = LayerKind.Timecode, Label = $"CLOCK {i + 1}", FontSize = 40,
                X = 60 + i % 3 * 800, Y = 480 + i / 3 * 150, Width = 740, Height = 65 })] };
            await _engine!.SetProjectAsync(project); await _engine.LoadMediaAsync(_args[1]);
            _project = project; _history = new(project); RefreshProjectUi();
            using var socket = new UdpClient();
            byte[] dmx = new byte[512]; new Random(1733).NextBytes(dmx);
            for (int universe = 0; universe < 3; universe++)
            {
                var packet = new byte[530]; ArtNetPacket.WriteDmx(packet, (ushort)(project.ArtNet.FirstUniverse + universe), 1, dmx);
                await socket.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, 16454));
            }
            await Task.Delay(500);
            _engine.Transport.SetLoop(true, TimeSpan.Zero, _engine.Transport.Snapshot().Duration);
            await _engine.StartStreamAsync(); _engine.Transport.Play();
            var timer = Stopwatch.StartNew(); var samples = new List<object>();
            using var http = new HttpClient();
            for (int second = 0; second < 30; second++)
            {
                await Task.Delay(1000);
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
                    var playlist = await http.GetStringAsync("http://127.0.0.1:18888/live/index.m3u8");
                    if (!playlist.Contains("#EXTINF")) throw new Exception("Live HLS has no segments.");
                    File.WriteAllText(Path.Combine(folder, "live.m3u8"), playlist);
                    foreach (string segment in playlist.Split('\n').Where(s => s.EndsWith(".ts")))
                        File.WriteAllBytes(Path.Combine(folder, segment.Trim()), await http.GetByteArrayAsync("http://127.0.0.1:18888/live/" + segment.Trim()));
                }
            }
            await _engine.StopStreamAsync();
            File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new { Passed = true, Elapsed = timer.Elapsed.TotalSeconds, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
        finally { _dirty = false; Close(); }
    }
}
