using System.Diagnostics;
using StageFlow.Core.Grid;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;
using StageFlow.Engine;

namespace StageFlow.App;

public sealed partial class MainWindow : Form
{
    private ShowProject _project = new() { Layers = [new() { Name = "Movie", Kind = LayerKind.Video },
        new() { Name = "Master timecode", Kind = LayerKind.Timecode, X = 60, Y = 940, Width = 600, Height = 90 }] };
    private EditHistory<ShowProject> _history;
    private ShowEngine? _engine;
    private readonly string[] _args;
    private readonly bool _recordOnStartup;
    private PerformanceRecorder? _recorder;
    private readonly string _diagnosticFolder = Path.Combine(AppContext.BaseDirectory, "diagnostics");
    private readonly Button _recordButton = Theme.Button("Start performance recording");
    private readonly Label _recordLabel = Theme.Label("Performance recording is off", true);
    private string? _projectPath;
    private bool _dirty, _binding, _scrubbing, _resumeAfterScrub, _midiArmed;
    private Guid? _selected;
    private long _lastScrub;
    private readonly Panel _preview = new() { BackColor = Color.Black, Dock = DockStyle.Fill, TabStop = true };
    private readonly ListBox _layers = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 48 };
    private readonly ListBox _markers = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly Label _clock = new() { Text = "00:00:00:00", AutoSize = true, Font = new Font("Consolas", 27), ForeColor = Theme.Text };
    private readonly Label _position = Theme.Label("No movie loaded", true);
    private readonly Label _status = Theme.Label("Starting engine…", true);
    private readonly Label _projectTitle = Theme.Label("Untitled show");
    private readonly TrackBar _timeline = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000000, TickStyle = TickStyle.None, Height = 30 };
    private readonly Button _play = Theme.Button("▶  Play", primary: true);
    private readonly Button _stream = Theme.Button("Start stream", primary: true);
    private readonly Button _arm = Theme.Button("MIDI outputs: off");
    private readonly Button _loop = Theme.Button("Loop: off");
    private readonly TextBox _goto = Theme.TextBox("00:00:00:00");
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 30000 };
    private readonly ToolTip _tips = new();
    private Layer? _dragLayer;
    private Layer? _pendingDrag;
    private bool _dragResize;
    private long _lastDrag;
    private PointF _dragStart;
    private bool _fullscreen;
    private Rectangle _restoredBounds;
    private readonly string _stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StageFlow");

    public MainWindow(string[] args)
    {
        _recordOnStartup = args.Contains("--record"); _args = args.Where(a => a != "--record").ToArray(); _history = new(_project);
        Text = "StageFlow " + Program.DisplayVersion; Size = new Size(1500, 940); MinimumSize = new Size(1180, 800);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9); KeyPreview = true; AllowDrop = true;
        BuildLayout(); BuildInspector(); RefreshProjectUi();
        Shown += async (_, _) => await StartEngine();
        _preview.Resize += (_, _) => _engine?.ResizePreview(_preview.ClientSize.Width, _preview.ClientSize.Height);
        _preview.MouseDown += PreviewMouseDown; _preview.MouseMove += PreviewMouseMove; _preview.MouseUp += PreviewMouseUp;
        _layers.SelectedIndexChanged += (_, _) => { if (!_binding) { _selected = (_layers.SelectedItem as LayerItem)?.Layer.Id; BindInspector(); } };
        _layers.DrawItem += DrawLayerItem;
        _markers.DoubleClick += (_, _) => JumpToSelectedMarker();
        var markerMenu = new ContextMenuStrip();
        markerMenu.Items.Add("Go to marker", null, (_, _) => JumpToSelectedMarker());
        markerMenu.Items.Add("Rename marker…", null, async (_, _) => await RenameMarker());
        markerMenu.Items.Add("Previous marker   Ctrl+↑", null, (_, _) => JumpMarker(-1));
        markerMenu.Items.Add("Next marker   Ctrl+↓", null, (_, _) => JumpMarker(1));
        markerMenu.Items.Add("Remove marker", null, async (_, _) => await RemoveMarker());
        _markers.ContextMenuStrip = markerMenu;
        _play.Click += (_, _) => TogglePlay();
        _stream.Click += async (_, _) => await Guard(async () => { if (_engine!.Status.Streaming) await _engine.StopStreamAsync(); else await _engine.StartStreamAsync(); });
        _arm.Click += async (_, _) => await Guard(async () =>
        {
            bool arm = !_midiArmed;
            try { await _engine!.SetMidiArmedAsync(arm); _midiArmed = arm; }
            catch { _midiArmed = false; throw; }
            finally { _arm.Text = _midiArmed ? "MIDI outputs: ON" : "MIDI outputs: off"; }
        });
        _timeline.MouseDown += (_, _) => { if (_engine == null) return; _scrubbing = true; _resumeAfterScrub = _engine.Transport.Snapshot().State == TransportState.Playing; _engine.SetScrubbing(true); _engine.Transport.Pause(); };
        _timeline.Scroll += (_, _) => { if (Environment.TickCount64 - _lastScrub > 45) { SeekFromSlider(); _lastScrub = Environment.TickCount64; } };
        _timeline.MouseUp += (_, _) => { SeekFromSlider(); _scrubbing = false; _engine?.SetScrubbing(false); if (_resumeAfterScrub) _engine?.Transport.Play(); };
        _loop.Click += async (_, _) => await ToggleLoop();
        _goto.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { GoTo(); e.SuppressKeyPress = true; } };
        _uiTimer.Tick += (_, _) => UpdateStatus(); _uiTimer.Start();
        _autosave.Tick += (_, _) => { if (_dirty) { try { Directory.CreateDirectory(_stateDirectory); ProjectStore.Save(_project, Path.Combine(_stateDirectory, "autosave.stageflow")); } catch (Exception ex) { _status.Text = "Autosave: " + ex.Message; } } };
        _autosave.Start();
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await OpenPath(files[0]); };
        FormClosing += ClosingWindow;
    }
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), BackColor = Theme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 186)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Theme.Background };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        var branding = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        branding.Controls.Add(new Label { Text = "STAGEFLOW", Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true });
        branding.Controls.Add(Theme.Label("SHOW CONTROL  ·  " + Program.DisplayVersion, true)); header.Controls.Add(branding, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 9, 0, 0) };
        toolbar.Controls.Add(Theme.Button("Open movie…", async (_, _) => await OpenMovie()));
        toolbar.Controls.Add(Theme.Button("Show ▾", (sender, _) => ShowFileMenu((Control)sender!)));
        toolbar.Controls.Add(Theme.Button("Import Hnode…", async (_, _) => await ImportHnode()));
        toolbar.Controls.Add(Theme.Button("Save", (_, _) => SaveProject()));
        header.Controls.Add(toolbar, 1, 0);
        var outputs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 9, 0, 0) }; outputs.Controls.Add(_arm); outputs.Controls.Add(_stream); header.Controls.Add(outputs, 2, 0);
        root.Controls.Add(header, 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, BackColor = Theme.Panel, Padding = new Padding(10) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 126)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 95)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        left.Controls.Add(Theme.Label("LAYERS  ·  topmost first"), 0, 0); left.Controls.Add(_layers, 0, 1);
        var layerTools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        layerTools.Controls.Add(Theme.Button("+ Clock", async (_, _) => await AddClock()));
        layerTools.Controls.Add(Theme.Button("+ Grid", async (_, _) => await AddGrid()));
        layerTools.Controls.Add(Theme.Button("+ Spout", async (_, _) => await AddSpout()));
        layerTools.Controls.Add(Theme.Button("Copy", async (_, _) => await DuplicateLayer()));
        layerTools.Controls.Add(Theme.Button("↑", async (_, _) => await MoveLayer(1)));
        layerTools.Controls.Add(Theme.Button("↓", async (_, _) => await MoveLayer(-1)));
        layerTools.Controls.Add(Theme.Button("×", async (_, _) => await RemoveLayer()));
        left.Controls.Add(layerTools, 0, 2); left.Controls.Add(Theme.Label("MARKERS  ·  double-click to go"), 0, 3); left.Controls.Add(_markers, 0, 4);
        var markerTools = new FlowLayoutPanel { Dock = DockStyle.Fill }; markerTools.Controls.Add(Theme.Button("+ Marker", async (_, _) => await AddMarker()));
        markerTools.Controls.Add(Theme.Button("Remove", async (_, _) => await RemoveMarker())); left.Controls.Add(markerTools, 0, 5);
        body.Controls.Add(left, 0, 0);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(10, 0, 10, 0) };
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.Controls.Add(_projectTitle, 0, 0); center.Controls.Add(_preview, 0, 1); body.Controls.Add(center, 1, 0);
        body.Controls.Add(_inspectorTabs, 2, 0); root.Controls.Add(body, 0, 1);
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0, 8, 0, 0) };
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        var readout = new FlowLayoutPanel { Dock = DockStyle.Fill }; readout.Controls.Add(_clock); readout.Controls.Add(_position);
        transport.Controls.Add(readout, 0, 0); transport.Controls.Add(_timeline, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.Add(Theme.Button("■ Stop", (_, _) => _engine?.Transport.Stop())); buttons.Controls.Add(_play);
        buttons.Controls.Add(Theme.Button("◀ Frame", (_, _) => _engine?.StepFrame(-1))); buttons.Controls.Add(Theme.Button("Frame ▶", (_, _) => _engine?.StepFrame(1)));
        buttons.Controls.Add(Theme.Button("Set in [I]", async (_, _) => await SetLoopPoint(false))); buttons.Controls.Add(Theme.Button("Set out [O]", async (_, _) => await SetLoopPoint(true)));
        buttons.Controls.Add(_loop); buttons.Controls.Add(Theme.Label("Go to")); _goto.Width = 135; _goto.Margin = new Padding(5, 10, 5, 0); buttons.Controls.Add(_goto);
        buttons.Controls.Add(Theme.Button("Go", (_, _) => GoTo())); buttons.Controls.Add(Theme.Button("Undo", async (_, _) => await Undo())); buttons.Controls.Add(Theme.Button("Redo", async (_, _) => await Redo()));
        transport.Controls.Add(buttons, 0, 2); root.Controls.Add(transport, 0, 2); root.Controls.Add(_status, 0, 3);
        _layers.BackColor = _markers.BackColor = Theme.Panel; _layers.ForeColor = _markers.ForeColor = Theme.Text;
        _timeline.BackColor = Theme.Background; _tips.SetToolTip(_arm, "Enable configured MIDI output routes. Incoming MTC and Art-Net can operate while outputs are off.");
        _tips.SetToolTip(_preview, "Click a layer to select it. Drag an unlocked layer to move it. Hold Shift for 8-pixel snapping.");
    }
    private async Task StartEngine()
    {
        try
        {
            _engine = CreateEngine(_project);
            await _engine.Ready; PopulateRoutes();
            if (_recordOnStartup) ToggleRecording();
            if (_args.Length > 0 && _args[0] == "--smoke") { await RunSmoke(); return; }
            if (_args.Length > 0 && File.Exists(_args[0])) await OpenPath(_args[0]);
            else await OfferRecovery();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task Guard(Func<Task> action)
    { try { if (_engine == null) return; await _engine.Ready; await action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) { _status.Text = ex.Message; MessageBox.Show(this, ex.Message, "StageFlow", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private void UpdateStatus()
    {
        if (_engine == null) return;
        var snapshot = _engine.Transport.Snapshot(); var status = _engine.Status;
        _clock.Text = snapshot.Timecode.ToString();
        _position.Text = $"{snapshot.Position:hh\\:mm\\:ss} / {snapshot.Duration:hh\\:mm\\:ss}   ·   {snapshot.State}   ·   {snapshot.Mode}";
        if (!_scrubbing) _timeline.Value = snapshot.Duration.Ticks > 0 ? (int)Math.Clamp(snapshot.Position.Ticks * 1000000.0 / snapshot.Duration.Ticks, 0, 1000000) : 0;
        _play.Text = snapshot.State == TransportState.Playing ? "Ⅱ  Pause" : "▶  Play";
        _play.Enabled = snapshot.Mode == ClockMode.Internal; _timeline.Enabled = snapshot.Mode == ClockMode.Internal;
        _stream.Text = status.Streaming ? "■ Stop stream" : "Start stream";
        _status.Text = status.Error ?? status.MediaError ?? $"{status.Gpu}   ·   Render {status.RenderMilliseconds:0.00} ms   ·   Late {status.MissedDeadlines}   ·   Art-Net {status.ArtNetPackets:N0} packets   ·   Audio underruns {status.AudioUnderruns}   {(status.Streaming ? "·   HLS LIVE" : "")}";
        if (_recorder != null)
        {
            _status.Text = "● REC   ·   " + _status.Text;
            _recordLabel.Text = _recorder.Error is { } failure ? "Recording error: " + failure : "Recording once per second\n" + Path.GetFileName(_recorder.FilePath);
        }
        _diagnostics.Text = $"GPU\n{status.Gpu}\n\nRender mean / peak\n{status.MeanRenderMilliseconds:0.00} / {status.PeakRenderMilliseconds:0.00} ms\nFrames {status.RenderedFrames:N0} · late {status.MissedDeadlines:N0}\n\nAudio buffer underruns {status.AudioUnderruns}\nArt-Net packets {status.ArtNetPackets:N0}\n\nMIDI-DMX\n{status.MidiDmx}\n\nSpout output\n{status.SpoutOutput ?? "Off"}\n{status.SpoutStatus}";
    }
    private void TogglePlay()
    { if (_engine == null) return; if (_engine.Transport.Snapshot().State == TransportState.Playing) _engine.Transport.Pause(); else _engine.Transport.Play(); }
    private void SeekFromSlider()
    { if (_engine is { } engine) engine.Transport.Seek(TimeSpan.FromTicks((long)(engine.Transport.Snapshot().Duration.Ticks * (_timeline.Value / 1000000.0)))); }
    private void GoTo()
    {
        if (_engine == null) return;
        if (!Timecode.TryParse(_goto.Text, _project.Clock.Rate, out var tc)) { _status.Text = "Enter a valid HH:MM:SS:FF timecode for the selected frame rate."; return; }
        _engine.Transport.Seek(_project.Clock.Rate.ToTime(tc.ToFrameNumber() - _project.Clock.OffsetFrames));
    }
    private async Task Edit(string name, ShowProject next)
    {
        await Guard(async () =>
        {
            if (next.GpuAdapter != _project.GpuAdapter) await RestartEngine(next);
            else await _engine!.SetProjectAsync(next);
            _history.Apply(name, next); _project = next; _dirty = true; RefreshProjectUi();
        });
    }
    private async Task Undo() => await RestoreHistory(false);
    private async Task Redo() => await RestoreHistory(true);
    private async Task RestoreHistory(bool redo)
    {
        if (!(redo ? _history.Redo() : _history.Undo())) return;
        await Guard(async () =>
        {
            try
            {
                var next = _history.Current;
                if (next.GpuAdapter != _project.GpuAdapter) await RestartEngine(next);
                else await _engine!.SetProjectAsync(next);
                if (next.MediaPath != _project.MediaPath)
                { if (next.MediaPath != null) await _engine!.LoadMediaAsync(next.MediaPath); else await _engine!.ClearMediaAsync(); }
                _project = next; _dirty = true; RefreshProjectUi();
            }
            catch { if (redo) _history.Undo(); else _history.Redo(); throw; }
        });
    }
    private void RefreshProjectUi()
    {
        _binding = true;
        _layers.Items.Clear(); foreach (var layer in _project.Layers.AsEnumerable().Reverse()) _layers.Items.Add(new LayerItem(layer));
        for (int i = 0; i < _layers.Items.Count; i++) if (((LayerItem)_layers.Items[i]).Layer.Id == _selected) _layers.SelectedIndex = i;
        Guid? selectedMarker = (_markers.SelectedItem as MarkerItem)?.Marker.Id;
        _markers.Items.Clear();
        foreach (var marker in _project.Markers.OrderBy(m => m.PositionTicks))
        {
            int index = _markers.Items.Add(new MarkerItem(marker, Timecode.FromFrameNumber(
                _project.Clock.Rate.ToFrames(TimeSpan.FromTicks(marker.PositionTicks)) + _project.Clock.OffsetFrames, _project.Clock.Rate).ToString()));
            if (marker.Id == selectedMarker) _markers.SelectedIndex = index;
        }
        _projectTitle.Text = _project.Name + (_dirty ? "  •" : "") + $"    {_project.Width} × {_project.Height} / {(double)_project.FpsNumerator / _project.FpsDenominator:0.###} fps";
        Text = $"{_project.Name}{(_dirty ? " *" : "")} — StageFlow {Program.DisplayVersion}";
        _loop.Text = _project.LoopEnabled ? "Loop: ON" : "Loop: off";
        _binding = false; BindInspector(); BindRoutes();
    }
    private void DrawLayerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var layer = ((LayerItem)_layers.Items[e.Index]).Layer;
        using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Input : Theme.Panel);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, layer.Name, Font, new Point(e.Bounds.X + 8, e.Bounds.Y + 5), layer.Visible ? Theme.Text : Theme.Muted);
        TextRenderer.DrawText(e.Graphics, $"{layer.Kind}  {(layer.Locked ? "· Locked" : "")}", Font, new Point(e.Bounds.X + 8, e.Bounds.Y + 25), Theme.Muted);
    }
    private async Task AddClock()
    {
        var layer = new Layer { Name = $"Timecode {_project.Layers.Count(l => l.Kind == LayerKind.Timecode) + 1}", Kind = LayerKind.Timecode,
            X = 60, Y = Math.Max(0, _project.Height - 130), Width = 600, Height = 90, FontSize = 48 };
        _selected = layer.Id; await Edit("Add timecode", _project with { Layers = [.. _project.Layers, layer] });
    }
    private async Task AddGrid()
    {
        var layer = new Layer { Name = "Art-Net grid", Kind = LayerKind.Grid, Width = _project.Width, Height = _project.Height, Locked = true,
            Grid = new GridLayout { Encoding = GridEncoding.BinaryStageFlight, Width = _project.Width, Height = _project.Height, UniverseCount = _project.ArtNet.UniverseCount } };
        _selected = layer.Id; await Edit("Add grid", _project with { Layers = [.. _project.Layers, layer] });
    }
    private Layer? SelectedLayer => _project.Layers.FirstOrDefault(l => l.Id == _selected);
    private async Task DuplicateLayer()
    {
        if (SelectedLayer is not { } layer) return;
        var copy = layer with { Id = Guid.NewGuid(), Name = layer.Name + " copy", X = layer.X + 24, Y = layer.Y + 24 };
        _selected = copy.Id; await Edit("Duplicate layer", _project with { Layers = [.. _project.Layers, copy] });
    }
    private async Task RemoveLayer()
    { if (_selected is { } id) { _selected = null; await Edit("Remove layer", _project with { Layers = _project.Layers.Where(l => l.Id != id).ToList() }); } }
    private async Task MoveLayer(int direction)
    {
        var layers = _project.Layers.ToList(); int index = layers.FindIndex(l => l.Id == _selected), target = index + direction;
        if (index < 0 || target < 0 || target >= layers.Count) return;
        (layers[index], layers[target]) = (layers[target], layers[index]); await Edit("Reorder layer", _project with { Layers = layers });
    }
    private async Task AddMarker()
    {
        if (_engine == null) return;
        string? name = Prompt("Marker name", $"Marker {_project.Markers.Count + 1}"); if (name == null) return;
        var marker = new Marker { Name = name, PositionTicks = _engine.Transport.Snapshot().Position.Ticks };
        await Edit("Add marker", _project with { Markers = [.. _project.Markers, marker] });
    }
    private async Task RemoveMarker()
    { if (_markers.SelectedItem is MarkerItem item) await Edit("Remove marker", _project with { Markers = _project.Markers.Where(m => m.Id != item.Marker.Id).ToList() }); }
    private void JumpToSelectedMarker() { if (_markers.SelectedItem is MarkerItem item) _engine?.Transport.Seek(TimeSpan.FromTicks(item.Marker.PositionTicks)); }
    private async Task RenameMarker()
    {
        if (_markers.SelectedItem is not MarkerItem item) return;
        string? name = Prompt("Marker name", item.Marker.Name); if (name == null) return;
        await Edit("Rename marker", _project with { Markers = _project.Markers.Select(m => m.Id == item.Marker.Id ? m with { Name = name } : m).ToList() });
    }
    private void JumpMarker(int direction)
    {
        if (_engine == null) return;
        long position = _engine.Transport.Snapshot().Position.Ticks;
        var marker = direction > 0 ? _project.Markers.OrderBy(m => m.PositionTicks).FirstOrDefault(m => m.PositionTicks > position + 10000)
            : _project.Markers.OrderByDescending(m => m.PositionTicks).FirstOrDefault(m => m.PositionTicks < position - 10000);
        if (marker != null) _engine.Transport.Seek(TimeSpan.FromTicks(marker.PositionTicks));
    }
    private async Task SetLoopPoint(bool end)
    {
        if (_engine == null) return;
        long ticks = _engine.Transport.Snapshot().Position.Ticks;
        await Edit("Set loop range", end ? _project with { LoopOutTicks = ticks } : _project with { LoopInTicks = ticks });
    }
    private async Task ToggleLoop()
    {
        if (_engine == null) return;
        await Guard(async () =>
        {
            bool enabled = !_project.LoopEnabled;
            _engine.Transport.SetLoop(enabled, TimeSpan.FromTicks(_project.LoopInTicks), TimeSpan.FromTicks(_project.LoopOutTicks));
            await Edit("Toggle loop", _project with { LoopEnabled = enabled });
        });
    }
    private PointF CanvasPoint(Point point)
    {
        float scale = Math.Min((float)_preview.Width / _project.Width, (float)_preview.Height / _project.Height);
        return new((point.X - (_preview.Width - _project.Width * scale) / 2) / scale, (point.Y - (_preview.Height - _project.Height * scale) / 2) / scale);
    }
    private void PreviewMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return; _preview.Focus(); var p = CanvasPoint(e.Location);
        var layer = _project.Layers.LastOrDefault(l => l.Visible && !l.Locked && p.X >= l.X && p.X <= l.X + l.Width && p.Y >= l.Y && p.Y <= l.Y + l.Height);
        _selected = layer?.Id; RefreshProjectUi();
        if (layer is { Locked: false })
        {
            _dragLayer = _pendingDrag = layer; _dragStart = p; _preview.Capture = true;
            double tolerance = 12.0 * _project.Width / Math.Max(1, _preview.Width);
            _dragResize = layer.Kind != LayerKind.Grid && Math.Abs(p.X - layer.X - layer.Width) < tolerance && Math.Abs(p.Y - layer.Y - layer.Height) < tolerance;
        }
    }
    private async void PreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragLayer == null || _engine == null) return;
        var p = CanvasPoint(e.Location); double x = _dragLayer.X + p.X - _dragStart.X, y = _dragLayer.Y + p.Y - _dragStart.Y;
        if ((ModifierKeys & Keys.Shift) != 0) { x = Math.Round(x / 8) * 8; y = Math.Round(y / 8) * 8; }
        var changed = _dragResize ? _dragLayer with { Width = Math.Max(1, Math.Round(_dragLayer.Width + p.X - _dragStart.X)), Height = Math.Max(1, Math.Round(_dragLayer.Height + p.Y - _dragStart.Y)) }
            : _dragLayer with { X = Math.Round(x), Y = Math.Round(y) };
        _pendingDrag = changed;
        if (Environment.TickCount64 - _lastDrag < 30) return;
        _lastDrag = Environment.TickCount64;
        var preview = _project with { Layers = _project.Layers.Select(l => l.Id == changed.Id ? changed : l).ToList() };
        try { await _engine.SetProjectAsync(preview); } catch (Exception ex) { _status.Text = ex.Message; }
    }
    private async void PreviewMouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragLayer == null || _engine == null) return;
        var changed = _pendingDrag; _dragLayer = _pendingDrag = null; _preview.Capture = false;
        if (changed != null) await Edit(_dragResize ? "Resize layer" : "Move layer", _project with { Layers = _project.Layers.Select(l => l.Id == changed.Id ? changed : l).ToList() });
    }
    private static string? Prompt(string title, string initial)
    {
        using var form = new Form { Text = title, Width = 380, Height = 155, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Panel, ForeColor = Theme.Text };
        var text = Theme.TextBox(initial); text.SetBounds(16, 16, 330, 28); form.Controls.Add(text);
        var ok = Theme.Button("OK"); ok.SetBounds(258, 56, 88, 34); ok.DialogResult = DialogResult.OK; form.Controls.Add(ok); form.AcceptButton = ok;
        text.SelectAll(); return form.ShowDialog() == DialogResult.OK ? text.Text : null;
    }
    private void ClosingWindow(object? sender, FormClosingEventArgs e)
    {
        if (_dirty)
        {
            var choice = MessageBox.Show(this, "Save changes before closing?", "StageFlow", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) { e.Cancel = true; return; }
            if (choice == DialogResult.Yes && !SaveProject()) { e.Cancel = true; return; }
        }
        _uiTimer.Stop(); _autosave.Stop(); _recorder?.Dispose(); _recorder = null; _engine?.Dispose(); _engine = null; _tips.Dispose();
        ClearRecovery();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { SaveProject(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveProject(true); return true; }
        if (keyData == (Keys.Control | Keys.N)) { _ = NewShow(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.O)) { _ = OpenProject(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { _ = OpenMovie(); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { _ = Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { _ = Redo(); return true; }
        if (keyData == (Keys.Control | Keys.Up)) { JumpMarker(-1); return true; }
        if (keyData == (Keys.Control | Keys.Down)) { JumpMarker(1); return true; }
        if (keyData == Keys.F11 || (_fullscreen && keyData == Keys.Escape))
        {
            if (!_fullscreen) { _restoredBounds = Bounds; FormBorderStyle = FormBorderStyle.None; Bounds = Screen.FromControl(this).Bounds; }
            else { FormBorderStyle = FormBorderStyle.Sizable; Bounds = _restoredBounds; }
            _fullscreen = !_fullscreen; return true;
        }
        if (HasEditorFocus(this)) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.Left: _engine?.StepFrame(-1); return true;
            case Keys.Right: _engine?.StepFrame(1); return true;
            case Keys.M: _ = AddMarker(); return true;
            case Keys.I: _ = SetLoopPoint(false); return true;
            case Keys.O: _ = SetLoopPoint(true); return true;
            case Keys.L: _ = ToggleLoop(); return true;
            case Keys.Delete: _ = RemoveLayer(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private static bool HasEditorFocus(Control root)
    {
        foreach (Control child in root.Controls)
            if (child.ContainsFocus && (child is TextBoxBase or ComboBox or NumericUpDown || HasEditorFocus(child))) return true;
        return false;
    }
    private void ToggleRecording()
    {
        try
        {
            if (_recorder != null)
            { string file = Path.GetFileName(_recorder.FilePath); _recorder.Dispose(); _recorder = null; _recordLabel.Text = "Saved " + file; }
            else _recorder = new PerformanceRecorder(_diagnosticFolder, () => _engine);
            _recordButton.Text = _recorder == null ? "Start performance recording" : "Stop performance recording";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private sealed record LayerItem(Layer Layer) { public override string ToString() => Layer.Name; }
    private ShowEngine CreateEngine(ShowProject project)
    {
        string runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
        // Development builds may opt into a native fixture; portable releases always bundle it.
        if (!Directory.Exists(runtime)) runtime = Environment.GetEnvironmentVariable("STAGEFLOW_NATIVE") ?? runtime;
        return new(_preview.Handle, _preview.Width, _preview.Height, project, runtime, Path.Combine(AppContext.BaseDirectory, "web"));
    }
    private async Task RestartEngine(ShowProject next)
    {
        next.Validate();
        if (_engine?.Status.Streaming == true) throw new InvalidOperationException("Stop streaming before changing GPU.");
        _engine?.Dispose(); _engine = null; _midiArmed = false; _arm.Text = "MIDI outputs: off";
        _engine = CreateEngine(next); await _engine.Ready;
        if (next.MediaPath != null && File.Exists(next.MediaPath)) await _engine.LoadMediaAsync(next.MediaPath);
    }
    private sealed record MarkerItem(Marker Marker, string Timecode) { public override string ToString() => $"{Timecode}  {Marker.Name}"; }
}
