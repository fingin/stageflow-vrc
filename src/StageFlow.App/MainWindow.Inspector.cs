using System.Reflection;
using StageFlow.Core.Projects;
using StageFlow.Engine.Audio;
using StageFlow.Engine.Midi;
using StageFlow.Engine.Spout;
using StageFlow.Engine.Graphics;
namespace StageFlow.App;

public sealed partial class MainWindow
{
    private readonly WorkspaceTabs _inspectorTabs = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid _properties = CreatePropertyGrid();
    private readonly ComboBox _scope = new ThemedComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Theme.Input, ForeColor = Theme.Text };
    private readonly TextBox _propertySearch = Theme.TextBox();
    private readonly TextBox _diagnostics = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, ForeColor = Theme.Muted, Font = new Font("Consolas", 9) };
    private readonly Label _liveState = Theme.Label("Live inspector", true);
    private List<AudioDevice> _audioDevices = [];
    private string[] _midiInputs = [], _midiOutputs = [], _spoutSenders = [], _gpus = [], _fonts = [];
    private ShowProject? _pendingProject;
    private ShowProject? _liveInFlight;
    private string _pendingEditName = "Edit properties";
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 40 };
    private bool _liveBusy, _projectOperation;
    private AppPreferences _preferences = new();
    internal ShowProject WorkingProject => _pendingProject ?? _liveInFlight ?? _project;
    private string? _inspectorIdentity;
    private static readonly MethodInfo CloneRecord = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static PropertyGrid CreatePropertyGrid() => new()
    {
        Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized,
        BackColor = Theme.Panel, ViewBackColor = Theme.Panel, ViewForeColor = Theme.Text, LineColor = Theme.Border,
        CategoryForeColor = Theme.Accent, CategorySplitterColor = Theme.Border, HelpBackColor = Theme.Panel,
        HelpForeColor = Theme.Muted, HelpBorderColor = Theme.Border, SelectedItemWithFocusBackColor = Theme.Selection,
        SelectedItemWithFocusForeColor = Color.White, Font = new Font("Segoe UI", 10)
    };
    private void BuildInspector()
    {
        _properties.Resize += (_, _) => _properties.HelpVisible = _properties.Height >= 240;
        _uiTimer.Interval = Math.Clamp(_preferences.UiRefreshMilliseconds, 33, 1000);
        _autosave.Interval = Math.Clamp(_preferences.AutosaveSeconds, 5, 600) * 1000;
        _liveTimer.Tick += async (_, _) => await FlushLiveEdits();
        Disposed += (_, _) => _liveTimer.Dispose();
        var inspector = new Panel { Text = "Inspector", BackColor = Theme.Panel, Padding = new Padding(10) };
        _scope.Items.AddRange(["Selection", "Show & canvas"]);
        _scope.SelectedIndex = 0; _scope.SelectedIndexChanged += (_, _) => BindInspector();
        _propertySearch.Dock = DockStyle.Top; _propertySearch.PlaceholderText = "Search properties…";
        _propertySearch.TextChanged += (_, _) => { if (_properties.SelectedObject is LiveProperties rows) { rows.Filter = _propertySearch.Text; _properties.Refresh(); } };
        _liveState.Dock = DockStyle.Bottom; _liveState.Height = 38; _liveState.AutoSize = false;
        inspector.Controls.Add(_properties); inspector.Controls.Add(_propertySearch); inspector.Controls.Add(_scope); inspector.Controls.Add(_liveState);
        _inspectorTabs.TabPages.Add(inspector);
        var status = new Panel { Text = "Monitor", BackColor = Theme.Panel, Padding = new Padding(10) };
        var actions = new ActionFlowPanel { Dock = DockStyle.Bottom, Height = 130 };
        actions.RequiredHeightChanged += height => actions.Height = height;
        _recordLabel.Dock = DockStyle.Bottom; _recordLabel.Height = 45; _recordLabel.AutoSize = false; _recordLabel.AutoEllipsis = true; actions.Controls.Add(_recordButton);
        _recordButton.Click += (_, _) => ToggleRecording();
        actions.Controls.Add(Theme.Button("Open logs", (_, _) => { Directory.CreateDirectory(_diagnosticFolder); OpenExternal(_diagnosticFolder); }));
        actions.Controls.Add(Theme.Button("Clear DMX", async (_, _) => await Guard(() => _engine!.BlackoutDmxAsync())));
        status.Controls.Add(_diagnostics); status.Controls.Add(actions); status.Controls.Add(_recordLabel); _inspectorTabs.TabPages.Add(status);
        BuildOscPanel();
    }
    private static void OpenExternal(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    private void Inspect(string scope) { if (!_scope.Items.Contains(scope)) { ShowPreferences(scope switch { "Outputs" => "Encoding", _ => scope }); return; } _scope.SelectedItem = scope; _inspectorTabs.SelectedIndex = 0; BindInspector(); }
    private void BindInspector()
    {
        _engine?.SelectLayer(_selected);
        string identity = _scope.SelectedIndex + ":" + _selected + ":" + WorkingProject.Layers.FirstOrDefault(l => l.Id == _selected)?.Kind;
        if (_inspectorIdentity == identity) { if (!_properties.ContainsFocus) _properties.Refresh(); return; }
        _inspectorIdentity = identity;
        var bag = new LiveProperties(_scope.Text) { Filter = _propertySearch.Text };
        switch (_scope.SelectedIndex)
        {
            case 0:
                if (WorkingProject.Layers.FirstOrDefault(l => l.Id == _selected) is { } layer)
                {
                    Guid id = layer.Id;
                    void LayerChange(ShowProject p, Layer changed) => QueueLive("Edit " + changed.Name, p with { Layers = p.Layers.Select(l => l.Id == id ? changed : l).ToList() });
                    AddRecord(bag, "01 · Object", () => WorkingProject.Layers.First(l => l.Id == id), (p, v) => LayerChange(p, v),
                        ["Id", "Grid", "SpoutSender", "FontFamily", "FontSize", "TextColor", "BackgroundColor", "Label", "Kind", "NetworkProtocol", "NetworkSource", "LiveAudioEnabled", "LiveAudioVolume"]);
                    if (layer.Kind == LayerKind.Network)
                        AddRecord(bag, "02 · Live source", () => WorkingProject.Layers.First(l => l.Id == id), (p, v) => LayerChange(p, v),
                            include: ["NetworkProtocol", "NetworkSource", "LiveAudioEnabled", "LiveAudioVolume"]);
                    if (layer.Kind == LayerKind.Timecode)
                        AddRecord(bag, "02 · Timecode appearance", () => WorkingProject.Layers.First(l => l.Id == id), (p, v) => LayerChange(p, v),
                            include: ["FontFamily", "FontSize", "TextColor", "BackgroundColor", "Label"]);
                    if (layer.Kind == LayerKind.Spout)
                        bag.Add("SpoutSender", "02 · Source", typeof(string), () => WorkingProject.Layers.First(l => l.Id == id).SpoutSender,
                            v => LayerChange(WorkingProject, WorkingProject.Layers.First(l => l.Id == id) with { SpoutSender = (string?)v }), options: _spoutSenders);
                    if (layer.Grid != null)
                    {
                        AddRecord(bag, "03 · Data grid", () => WorkingProject.Layers.First(l => l.Id == id).Grid!, (p, v) => LayerChange(p, p.Layers.First(l => l.Id == id) with { Grid = v, Width = v.Width, Height = v.Height }), ["Masks"]);
                        bag.Add("Channel masks", "03 · Data grid", typeof(string), () => string.Join(", ", WorkingProject.Layers.First(l => l.Id == id).Grid!.Masks.Select(r => r.Start == r.End ? r.Start.ToString() : r.Start + "-" + r.End)), value =>
                        {
                            var ranges = new List<StageFlow.Core.Grid.ChannelRange>();
                            foreach (string token in ((string?)value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                string[] range = token.Split('-', StringSplitOptions.TrimEntries);
                                if (range.Length > 2) throw new InvalidDataException("Use channel indices or ranges, for example 0-15, 64, 100-120.");
                                int first = HnodeImporter.ParseChannel(range[0]), last = range.Length == 2 ? HnodeImporter.ParseChannel(range[1]) : first;
                                ranges.Add(new(first,last));
                            }
                            var current = WorkingProject.Layers.First(l => l.Id == id); LayerChange(WorkingProject, current with { Grid = current.Grid! with { Masks = ranges } });
                        }, "Zero-based channel indices/ranges, separated by commas. Hnode universe.channel notation also works. Empty means no masks.");
                    }
                }
                else bag.Add("Selection", "Inspector", typeof(string), () => "Select a layer in the hierarchy or preview", null);
                break;
            case 1: AddRecord(bag, "01 · Show", () => WorkingProject, (_, v) => QueueLive("Edit show", v), include: ["Name", "Width", "Height", "FpsNumerator", "FpsDenominator", "GpuAdapter"]); break;

        }
        _properties.SelectedObject = bag;
    }
    private void AddRecord<T>(LiveProperties bag, string category, Func<T> get, Action<ShowProject, T> commit, string[]? exclude = null, string[]? include = null) where T : class
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite || exclude?.Contains(property.Name) == true || include != null && !include.Contains(property.Name)) continue;
            if (property.Name == "DeviceId")
            {
                string Friendly(AudioDevice device) => _audioDevices.Count(d => d.Name == device.Name) > 1 ? device.Name + " [" + device.Id[^Math.Min(8, device.Id.Length)..] + "]" : device.Name;
                bag.Add("Local audio device", category, typeof(string), () =>
                {
                    string? id = property.GetValue(get()) as string;
                    return id == null ? "(default)" : _audioDevices.FirstOrDefault(d => d.Id == id) is { } device ? Friendly(device) : id;
                }, value =>
                {
                    string? label = value as string;
                    string? id = label == null || label == "(default)" ? null : _audioDevices.FirstOrDefault(d => Friendly(d) == label)?.Id ?? label;
                    var changed = (T)CloneRecord.Invoke(get(), null)!; property.SetValue(changed, id); commit(WorkingProject, changed);
                }, "Select the local monitoring output. Stream audio has independent volume and mute controls.", ["(default)", .. _audioDevices.Select(Friendly)]);
                continue;
            }
            string[]? choices = property.Name switch
            {
                "MidiInput" => ["(default)", .. _midiInputs], "MidiOutput" or "OutputDevice" => ["(default)", .. _midiOutputs],
                "GpuAdapter" => ["(default)", .. _gpus], "DeviceId" => ["(default)", .. _audioDevices.Select(d => d.Id)],
                "Encoder" => ["h264_nvenc", "hevc_nvenc", "libx264"], "FontFamily" => _fonts, _ => null
            };
            bool lockedSize = typeof(T) == typeof(Layer) && get() is Layer { Kind: LayerKind.Grid } && property.Name is "Width" or "Height";
            bag.Add(property.Name, category, property.PropertyType, () => property.GetValue(get()), lockedSize ? null : value =>
            {
                var changed = (T)CloneRecord.Invoke(get(), null)!;
                property.SetValue(changed, value); commit(WorkingProject, changed);
            }, description: SettingHelp.Describe(typeof(T), property.Name), options: choices);
        }
    }
    private void QueueLive(string name, ShowProject next)
    {
        if (_projectOperation) throw new InvalidOperationException("Wait for the current show operation to finish before editing settings.");
        next.Validate(); _pendingProject = next; _pendingEditName = name; _dirty = true;
        SetLiveFeedback("Updating…"); if (!_liveTimer.Enabled) _liveTimer.Start();
    }
    private void SetLiveFeedback(string message, bool error = false)
    {
        _liveState.Text = message; _liveState.ForeColor = error ? Theme.Warning : Theme.Muted;
        if (_preferencesFeedback is { IsDisposed: false }) { _preferencesFeedback.Text = message; _preferencesFeedback.ForeColor = _liveState.ForeColor; }
    }
    private async Task FlushLiveEdits()
    {
        if (_liveBusy) return;
        _liveTimer.Stop(); if (_pendingProject == null) return;
        _liveBusy = true; var next = _pendingProject; _pendingProject = null; _liveInFlight = next;
        try { await CommitProject(_pendingEditName, next); SetLiveFeedback("Live · changes updated"); }
        catch (Exception ex) { SetLiveFeedback(ex.Message, true); }
        finally { _liveInFlight = null; _liveBusy = false; if (!_properties.ContainsFocus) _properties.Refresh(); if (_preferencesGrid?.ContainsFocus == false) _preferencesGrid.Refresh(); if (_pendingProject != null) _liveTimer.Start(); }
    }
    internal async Task DrainLiveEdits()
    {
        while (_liveBusy || _pendingProject != null)
        { if (_liveBusy) await Task.Delay(5); else await FlushLiveEdits(); }
    }
    private async Task RunProjectOperation(Func<Task> action)
    {
        await DrainLiveEdits(); _projectOperation = _liveBusy = true;
        _workspace.Enabled = false; if (_preferencesWindow != null) _preferencesWindow.Enabled = false;
        try { await action(); }
        finally
        {
            _projectOperation = _liveBusy = false; _workspace.Enabled = true;
            if (_preferencesWindow != null) _preferencesWindow.Enabled = true;
            RefreshProjectUi(); if (_pendingProject != null) _liveTimer.Start();
        }
    }
    private async Task CommitProject(string name, ShowProject next, bool recordHistory = true)
    {
        next.Validate();
        bool restart = false;
        if (_engine != null)
        {
            await _engine.Ready;
            restart = _engine.Status.Streaming && (next.Stream != _project.Stream || !next.NetworkOutputs.SequenceEqual(_project.NetworkOutputs) || next.Width != _project.Width || next.Height != _project.Height || next.FpsNumerator != _project.FpsNumerator || next.FpsDenominator != _project.FpsDenominator || next.GpuAdapter != _project.GpuAdapter);
            if (restart) await _engine.StopStreamAsync();
            try
            {
                if (next.GpuAdapter != _project.GpuAdapter) await RestartEngine(next); else await _engine.SetProjectAsync(next);
            }
            catch { _pendingProject = null; throw; }
        }
        ConfigureOsc(next.Osc);
        if (recordHistory) _history.Apply(name, next);
        _project = next; _dirty = true; RefreshProjectUi();
        // The project is already applied even if an output cannot reopen. Preserve that
        // state in the inspector/history and leave the failed output stopped for repair.
        if (restart)
        {
            try { await _engine!.StartStreamAsync(); }
            catch (Exception ex) { _status.Text = "Settings updated; stream restart failed: " + ex.Message; }
        }
    }
    private void PopulateRoutes()
    {
        using (var installed = new System.Drawing.Text.InstalledFontCollection()) _fonts = installed.Families.Select(f => f.Name).OrderBy(n => n).ToArray();
        _midiInputs = MidiDevices.Inputs().Select(d => d.Name).ToArray(); _midiOutputs = MidiDevices.Outputs().Select(d => d.Name).ToArray();
        _spoutSenders = SpoutRegistry.Senders().ToArray(); _gpus = GpuDevice.EnumerateAdapters().Select(g => g.Name).Distinct().ToArray();
        try { _audioDevices = LocalAudioOutput.Enumerate().ToList(); } catch { _audioDevices = []; }
        _inspectorIdentity = null; BindInspector();
    }
    private async Task ReconnectRoutes()
    {
        await DrainLiveEdits(); _oscSettings = null; ConfigureOsc(_project.Osc);
        await Guard(() => _engine!.ReconnectRoutesAsync()); PopulateRoutes();
    }
    private void BindRoutes() { BindInspector(); BindOscMappings(); }
    private async Task FitLayer()
    {
        if (SelectedLayer is not { Kind: LayerKind.Video } layer) return;
        var info = _engine?.MediaInfo;
        double scale = info == null ? 1 : Math.Min((double)_project.Width / info.Width, (double)_project.Height / info.Height);
        double width = info == null ? _project.Width : info.Width * scale, height = info == null ? _project.Height : info.Height * scale;
        await Edit("Fit video", WorkingProject with { Layers = WorkingProject.Layers.Select(l => l.Id == layer.Id ? l with
        { X = (_project.Width - width) / 2, Y = (_project.Height - height) / 2, Width = width, Height = height, CropLeft = 0, CropTop = 0, CropRight = 0, CropBottom = 0 } : l).ToList() });
    }
    private async Task SaveFrame()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "StageFlow-v2-frame.png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await Guard(() => _engine!.SaveFrameAsync(dialog.FileName));
    }
    private async Task AddSpout()
    {
        string? name = Prompt("Spout sender name", SpoutRegistry.Senders().FirstOrDefault() ?? "HNode Output");
        if (string.IsNullOrWhiteSpace(name)) return;
        var layer = new Layer { Name = name, Kind = LayerKind.Spout, SpoutSender = name, Width = _project.Width, Height = _project.Height };
        _selected = layer.Id; await Edit("Add Spout input", WorkingProject with { Layers = [.. WorkingProject.Layers, layer] });
    }
    private async Task AddNetworkSource(NetworkSourceProtocol protocol)
    {
        string? source = PickNetworkSource(protocol);
        if (string.IsNullOrWhiteSpace(source)) return;
        var layer = new Layer { Name = protocol + " source", Kind = LayerKind.Network, NetworkProtocol = protocol, NetworkSource = source, Width = _project.Width, Height = _project.Height };
        _selected = layer.Id; await Edit("Add network source", WorkingProject with { Layers = [.. WorkingProject.Layers, layer] }); Inspect("Selection");
    }
    private string? PickNetworkSource(NetworkSourceProtocol protocol)
    {
        using var dialog = new Form { Text = "Add " + protocol + " source", Size = new Size(650, 260), MinimumSize = new Size(530, 260), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var hint = Theme.Label(protocol == NetworkSourceProtocol.Srt ? "Enter an SRT URL, including caller/listener mode and latency." : "Select a discovered sender or enter its complete source name.", true);
        var source = new ThemedComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat };
        if (protocol == NetworkSourceProtocol.Srt) source.Text = "srt://0.0.0.0:9002?mode=listener&latency=120000";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var add = Theme.Button("Add source", primary: true); add.DialogResult = DialogResult.OK; actions.Controls.Add(add); dialog.AcceptButton = add;
        if (protocol != NetworkSourceProtocol.Srt)
        {
            var refresh = Theme.Button("Refresh sources"); actions.Controls.Add(refresh);
            async Task Discover()
            {
                refresh.Enabled = false; hint.Text = "Looking for " + protocol + " senders…";
                try
                {
                    string[] names = await Task.Run(() => protocol == NetworkSourceProtocol.Ndi ? StageFlow.Engine.Network.NdiNative.Sources() : StageFlow.Engine.Network.OmtNative.Sources());
                    if (dialog.IsDisposed) return;
                    string typed = source.Text; source.Items.Clear(); source.Items.AddRange(names); source.Text = typed;
                    if (typed.Length == 0 && names.Length > 0) source.SelectedIndex = 0;
                    hint.Text = names.Length + " sources found. You can also enter a complete source name.";
                }
                catch (Exception ex) { if (!dialog.IsDisposed) hint.Text = ex.Message; }
                finally { if (!refresh.IsDisposed) refresh.Enabled = true; }
            }
            refresh.Click += async (_, _) => await Discover(); dialog.Shown += async (_, _) => await Discover();
        }
        layout.Controls.Add(hint, 0, 0); layout.Controls.Add(source, 0, 1); layout.Controls.Add(actions, 0, 2); dialog.Controls.Add(layout); Theme.Apply(dialog, _preferences.TextSize);
        return dialog.ShowDialog(this) == DialogResult.OK ? source.Text.Trim() : null;
    }
    private void ShowNetworkOutputs()
    {
        using var dialog = new Form { Text = "StageFlow v2 · Network destinations", Size = new Size(830, 550), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background };
        var list = new ListBox { Dock = DockStyle.Left, Width = 230, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, DisplayMember = "Name" };
        var properties = CreatePropertyGrid(); var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52 };
        void RefreshList(Guid? select = null)
        {
            list.Items.Clear(); foreach (var output in WorkingProject.NetworkOutputs) list.Items.Add(output);
            for (int i = 0; i < list.Items.Count; i++) if (((NetworkStreamOutput)list.Items[i]).Id == select) list.SelectedIndex = i;
        }
        foreach (var protocol in Enum.GetValues<NetworkStreamProtocol>()) buttons.Controls.Add(Theme.Button("+ " + protocol, (_, _) =>
        {
            var output = new NetworkStreamOutput { Name = protocol + " " + (WorkingProject.NetworkOutputs.Count + 1), Protocol = protocol,
                Url = protocol == NetworkStreamProtocol.Rtmp ? "rtmp://127.0.0.1/live/stageflow" : "srt://127.0.0.1:9001?mode=caller&latency=120000" };
            QueueLive("Add destination", WorkingProject with { NetworkOutputs = [.. WorkingProject.NetworkOutputs, output] }); RefreshList(output.Id);
        }));
        buttons.Controls.Add(Theme.Button("Remove", (_, _) =>
        {
            if (list.SelectedItem is NetworkStreamOutput output) { QueueLive("Remove destination", WorkingProject with { NetworkOutputs = WorkingProject.NetworkOutputs.Where(o => o.Id != output.Id).ToList() }); properties.SelectedObject = null; RefreshList(); }
        }));
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not NetworkStreamOutput output) return;
            var bag = new LiveProperties(output.Name);
            AddRecord(bag, "Destination", () => WorkingProject.NetworkOutputs.First(o => o.Id == output.Id), (p, value) =>
                QueueLive("Edit destination", p with { NetworkOutputs = p.NetworkOutputs.Select(o => o.Id == output.Id ? value : o).ToList() }), ["Id"]);
            properties.SelectedObject = bag;
        };
        dialog.Controls.Add(properties); dialog.Controls.Add(list); dialog.Controls.Add(buttons); RefreshList(); dialog.ShowDialog(this);
    }
}
