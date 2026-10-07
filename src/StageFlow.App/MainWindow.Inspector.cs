using StageFlow.Core.Grid;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;
using StageFlow.Engine.Audio;
using StageFlow.Engine.Midi;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    private readonly TabControl _inspectorTabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox _layerName = Theme.TextBox(), _label = Theme.TextBox(), _font = Theme.TextBox("Consolas"),
        _textColor = Theme.TextBox("#FFFFFFFF"), _backgroundColor = Theme.TextBox("#AA000000");
    private readonly NumericUpDown _x = Number(-8192, 8192), _y = Number(-8192, 8192), _width = Number(1, 8192), _height = Number(1, 8192),
        _opacity = Number(0, 100, 100), _fontSize = Number(8, 256, 48), _cropL = Number(0, 8192), _cropT = Number(0, 8192), _cropR = Number(0, 8192), _cropB = Number(0, 8192);
    private readonly CheckBox _visible = Check("Visible", true), _locked = Check("Lock position"), _gamma = Check("VRSL gamma correction", true),
        _rgb = Check("VRSL RGB packing"), _maskZero = Check("Make zero channels transparent");
    private readonly ComboBox _gridEncoding = Combo(Enum.GetNames<GridEncoding>()), _orientation = Combo(Enum.GetNames<GridOrientation>());
    private readonly ComboBox _clockMode = Combo(Enum.GetNames<ClockMode>()), _mtcRate = Combo(Enum.GetNames<MtcRate>()),
        _midiInput = Combo(), _midiOutput = Combo(), _audioDevice = Combo();
    private readonly TextBox _tcOffset = Theme.TextBox("00:00:00:00"), _artnetBind = Theme.TextBox("127.0.0.1");
    private readonly NumericUpDown _localVolume = Number(0, 100, 100), _streamVolume = Number(0, 100, 100),
        _audioOffset = Number(-5000, 5000), _mtcOffset = Number(-5000, 5000), _artnetPort = Number(1, 65535, 6454),
        _firstUniverse = Number(0, 32767), _universeCount = Number(1, 512, 48);
    private readonly CheckBox _mtcEnabled = Check("Send master MTC"), _scrubMtc = Check("Send MTC while scrubbing"),
        _localMute = Check("Mute local audio"), _streamMute = Check("Mute stream audio"), _artnetEnabled = Check("Receive Art-Net"), _autoUniverses = Check("Use received universe extent", true);
    private readonly NumericUpDown _outputW = Number(16, 8192, 1920), _outputH = Number(16, 8192, 1080), _outputFps = Number(1, 120, 60),
        _bitrate = Number(500, 200000, 12000), _serverPort = Number(1, 65535, 8888);
    private readonly TextBox _serverBind = Theme.TextBox("0.0.0.0");
    private List<AudioDevice> _audioDevices = [];
    private readonly Label _layerHint = Theme.Label("Select a layer to edit", true);

    private static NumericUpDown Number(decimal min, decimal max, decimal initial = 0) => new()
    { Minimum = min, Maximum = max, Value = Math.Clamp(initial, min, max), Width = 130, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
    private static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(3, 7, 3, 7) };
    private static ComboBox Combo(params string[] values)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235, BackColor = Theme.Input, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
        box.Items.AddRange(values); if (values.Length > 0) box.SelectedIndex = 0; return box;
    }
    private FlowLayoutPanel Tab(string name)
    {
        var page = new TabPage(name) { BackColor = Theme.Panel, ForeColor = Theme.Text, Padding = new Padding(5) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(5) };
        page.Controls.Add(panel); _inspectorTabs.TabPages.Add(page); return panel;
    }
    private static void Field(FlowLayoutPanel panel, string name, Control control)
    { panel.Controls.Add(Theme.Label(name, true)); panel.Controls.Add(control); }
    private void BuildInspector()
    {
        var layer = Tab("Layer"); layer.Controls.Add(_layerHint);
        Field(layer, "Name", _layerName); layer.Controls.Add(_visible); layer.Controls.Add(_locked);
        Field(layer, "X · pixels", _x); Field(layer, "Y · pixels", _y); Field(layer, "Width", _width); Field(layer, "Height", _height); Field(layer, "Opacity · %", _opacity);
        Field(layer, "Crop left / top / right / bottom", _cropL); layer.Controls.Add(_cropT); layer.Controls.Add(_cropR); layer.Controls.Add(_cropB);
        Field(layer, "Clock label", _label); Field(layer, "Font family", _font); Field(layer, "Font size · pixels", _fontSize);
        Field(layer, "Text color · #AARRGGBB", _textColor); Field(layer, "Background · #AARRGGBB", _backgroundColor);
        Field(layer, "Grid encoding", _gridEncoding); Field(layer, "Grid orientation", _orientation); layer.Controls.Add(_gamma); layer.Controls.Add(_rgb); layer.Controls.Add(_maskZero);
        layer.Controls.Add(Theme.Button("Apply layer", async (_, _) => await ApplyLayer(), true));
        layer.Controls.Add(Theme.Button("Fit movie to canvas", async (_, _) => await FitLayer()));
        var routes = Tab("Routing");
        Field(routes, "Clock source", _clockMode); Field(routes, "MTC frame rate", _mtcRate); Field(routes, "Show start timecode", _tcOffset);
        Field(routes, "MTC input", _midiInput); Field(routes, "MTC output → grandMA2", _midiOutput); routes.Controls.Add(_mtcEnabled); routes.Controls.Add(_scrubMtc);
        Field(routes, "MTC output offset · ms", _mtcOffset); Field(routes, "Local audio device", _audioDevice);
        Field(routes, "Local volume · %", _localVolume); routes.Controls.Add(_localMute); Field(routes, "Stream volume · %", _streamVolume); routes.Controls.Add(_streamMute);
        Field(routes, "Audio offset · ms", _audioOffset); routes.Controls.Add(_artnetEnabled); Field(routes, "Art-Net bind IPv4", _artnetBind); Field(routes, "Art-Net port", _artnetPort);
        Field(routes, "First universe · zero-based", _firstUniverse); Field(routes, "Universe capacity", _universeCount); routes.Controls.Add(_autoUniverses);
        routes.Controls.Add(Theme.Button("Apply routing", async (_, _) => await ApplyRoutes(), true)); routes.Controls.Add(Theme.Button("Refresh devices", (_, _) => { PopulateRoutes(); BindRoutes(); }));
        var output = Tab("Output");
        Field(output, "Canvas width", _outputW); Field(output, "Canvas height", _outputH); Field(output, "Video FPS (MTC rate is separate)", _outputFps);
        Field(output, "Video bitrate · Kbps", _bitrate); Field(output, "HTTP bind address", _serverBind); Field(output, "HTTP port", _serverPort);
        output.Controls.Add(Theme.Label("Hardware H.264 + AAC\nHLS server runs inside StageFlow", true));
        output.Controls.Add(Theme.Button("Apply output", async (_, _) => await ApplyOutput(), true));
        output.Controls.Add(Theme.Button("Copy HLS URL", (_, _) => Clipboard.SetText($"http://127.0.0.1:{_project.Stream.Port}/live/index.m3u8")));
        output.Controls.Add(Theme.Button("Open player", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://127.0.0.1:{_project.Stream.Port}/") { UseShellExecute = true })));
        output.Controls.Add(Theme.Label("SHORTCUTS\nSpace  Play / pause\n← / →  Frame step\nM  Add marker\nI / O  Loop in / out\nL  Toggle loop\nCtrl+S  Save show\nCtrl+Z / Ctrl+Y  Undo / redo\nF11  Full screen\n\nDrop a movie or show file to open it.", true));
    }
    private void BindInspector()
    {
        _binding = true;
        if (SelectedLayer is { } layer)
        {
            _layerHint.Text = layer.Kind == LayerKind.Grid ? "Data layer · native pixel geometry" : layer.Kind + " layer";
            _layerName.Text = layer.Name; _visible.Checked = layer.Visible; _locked.Checked = layer.Locked;
            Set(_x, layer.X); Set(_y, layer.Y); Set(_width, layer.Width); Set(_height, layer.Height); Set(_opacity, layer.Opacity * 100);
            _width.Enabled = _height.Enabled = layer.Kind != LayerKind.Grid;
            Set(_cropL, layer.CropLeft); Set(_cropT, layer.CropTop); Set(_cropR, layer.CropRight); Set(_cropB, layer.CropBottom);
            _label.Text = layer.Label; _font.Text = layer.FontFamily; Set(_fontSize, layer.FontSize); _textColor.Text = layer.TextColor; _backgroundColor.Text = layer.BackgroundColor;
            if (layer.Grid is { } grid) { _gridEncoding.SelectedItem = grid.Encoding.ToString(); _orientation.SelectedItem = grid.Orientation.ToString(); _gamma.Checked = grid.GammaCorrection; _rgb.Checked = grid.RgbPacking; _maskZero.Checked = grid.MaskZero; }
        }
        else _layerHint.Text = "Select a layer to edit";
        _binding = false;
    }
    private static void Set(NumericUpDown control, double value) => control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);
    private async Task ApplyLayer()
    {
        if (SelectedLayer is not { } layer) return;
        var changed = layer with
        {
            Name = _layerName.Text, Visible = _visible.Checked, Locked = _locked.Checked, X = (double)_x.Value, Y = (double)_y.Value,
            Width = (double)_width.Value, Height = (double)_height.Value, Opacity = (double)_opacity.Value / 100,
            CropLeft = (int)_cropL.Value, CropTop = (int)_cropT.Value, CropRight = (int)_cropR.Value, CropBottom = (int)_cropB.Value,
            Label = _label.Text, FontFamily = _font.Text, FontSize = (int)_fontSize.Value, TextColor = _textColor.Text, BackgroundColor = _backgroundColor.Text,
            Grid = layer.Grid is { } grid ? grid with { Encoding = Enum.Parse<GridEncoding>((string)_gridEncoding.SelectedItem!),
                Orientation = Enum.Parse<GridOrientation>((string)_orientation.SelectedItem!), GammaCorrection = _gamma.Checked, RgbPacking = _rgb.Checked, MaskZero = _maskZero.Checked } : null
        };
        await Edit("Edit layer", _project with { Layers = _project.Layers.Select(l => l.Id == layer.Id ? changed : l).ToList() });
    }
    private async Task FitLayer()
    {
        if (SelectedLayer is not { Kind: LayerKind.Video } layer) return;
        await Edit("Fit video", _project with { Layers = _project.Layers.Select(l => l.Id == layer.Id ? l with { X = 0, Y = 0, Width = _project.Width, Height = _project.Height, CropLeft = 0, CropTop = 0, CropRight = 0, CropBottom = 0 } : l).ToList() });
    }
    private void PopulateRoutes()
    {
        _midiInput.Items.Clear(); _midiInput.Items.Add("(none)"); foreach (var device in MidiDevices.Inputs()) _midiInput.Items.Add(device.Name);
        _midiOutput.Items.Clear(); _midiOutput.Items.Add("(none)"); foreach (var device in MidiDevices.Outputs()) _midiOutput.Items.Add(device.Name);
        try { _audioDevices = LocalAudioOutput.Enumerate().ToList(); } catch { _audioDevices = []; }
        _audioDevice.Items.Clear(); _audioDevice.Items.Add("Default output"); foreach (var device in _audioDevices) _audioDevice.Items.Add(device.Name);
        BindRoutes();
    }
    private static void SelectDevice(ComboBox combo, string? name)
    {
        int index = name == null ? -1 : combo.Items.IndexOf(name);
        if (index < 0 && !string.IsNullOrEmpty(name)) { combo.Items.Add(name); index = combo.Items.Count - 1; }
        combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
    }
    private void BindRoutes()
    {
        _clockMode.SelectedItem = _project.Clock.Mode.ToString(); _mtcRate.SelectedItem = _project.Clock.Rate.ToString();
        _tcOffset.Text = Timecode.FromFrameNumber(_project.Clock.OffsetFrames, _project.Clock.Rate).ToString();
        SelectDevice(_midiInput, _project.Clock.MidiInput); SelectDevice(_midiOutput, _project.Clock.MidiOutput);
        _mtcEnabled.Checked = _project.Clock.OutputEnabled; _scrubMtc.Checked = _project.Clock.SendLocateDuringScrub; Set(_mtcOffset, _project.Clock.OutputOffsetMilliseconds);
        int audio = _audioDevices.FindIndex(d => d.Id == _project.Audio.DeviceId); if (_audioDevice.Items.Count > 0) _audioDevice.SelectedIndex = audio + 1;
        Set(_localVolume, _project.Audio.LocalVolume * 100); Set(_streamVolume, _project.Audio.StreamVolume * 100); Set(_audioOffset, _project.Audio.OffsetMilliseconds);
        _localMute.Checked = _project.Audio.LocalMute; _streamMute.Checked = _project.Audio.StreamMute;
        _artnetEnabled.Checked = _project.ArtNet.Enabled; _artnetBind.Text = _project.ArtNet.BindAddress; Set(_artnetPort, _project.ArtNet.Port);
        Set(_firstUniverse, _project.ArtNet.FirstUniverse); Set(_universeCount, _project.ArtNet.UniverseCount); _autoUniverses.Checked = _project.ArtNet.AutomaticUniverseCount;
        Set(_outputW, _project.Width); Set(_outputH, _project.Height); Set(_outputFps, (double)_project.FpsNumerator / _project.FpsDenominator);
        Set(_bitrate, _project.Stream.VideoBitrateKbps); Set(_serverPort, _project.Stream.Port); _serverBind.Text = _project.Stream.BindAddress;
    }
    private async Task ApplyRoutes()
    {
        var rate = Enum.Parse<MtcRate>((string)_mtcRate.SelectedItem!);
        if (!Timecode.TryParse(_tcOffset.Text, rate, out var offset)) { _status.Text = "Invalid show start timecode."; return; }
        await Edit("Change routing", _project with
        {
            Clock = _project.Clock with { Mode = Enum.Parse<ClockMode>((string)_clockMode.SelectedItem!), Rate = rate, OffsetFrames = offset.ToFrameNumber(),
                MidiInput = _midiInput.SelectedIndex <= 0 ? null : (string)_midiInput.SelectedItem!, MidiOutput = _midiOutput.SelectedIndex <= 0 ? null : (string)_midiOutput.SelectedItem!,
                OutputEnabled = _mtcEnabled.Checked, SendLocateDuringScrub = _scrubMtc.Checked, OutputOffsetMilliseconds = (int)_mtcOffset.Value },
            Audio = _project.Audio with { DeviceId = _audioDevice.SelectedIndex <= 0 ? null : _audioDevices[_audioDevice.SelectedIndex - 1].Id,
                LocalVolume = (float)_localVolume.Value / 100, StreamVolume = (float)_streamVolume.Value / 100, LocalMute = _localMute.Checked,
                StreamMute = _streamMute.Checked, OffsetMilliseconds = (int)_audioOffset.Value },
            ArtNet = _project.ArtNet with { Enabled = _artnetEnabled.Checked, BindAddress = _artnetBind.Text, Port = (int)_artnetPort.Value,
                FirstUniverse = (int)_firstUniverse.Value, UniverseCount = (int)_universeCount.Value, AutomaticUniverseCount = _autoUniverses.Checked }
        });
    }
    private async Task ApplyOutput() => await Edit("Change output", _project with { Width = (int)_outputW.Value, Height = (int)_outputH.Value,
        FpsNumerator = (int)_outputFps.Value, FpsDenominator = 1,
        Stream = _project.Stream with { BindAddress = _serverBind.Text, Port = (int)_serverPort.Value, VideoBitrateKbps = (int)_bitrate.Value } });
}
