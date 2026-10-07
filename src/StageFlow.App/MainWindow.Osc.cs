using StageFlow.Core.Osc;
using StageFlow.Core.Projects;
using StageFlow.Core.Timing;

namespace StageFlow.App;
public sealed partial class MainWindow
{
    private OscListener? _osc;
    private OscFeedbackSender? _oscFeedback;
    private OscSettings? _oscSettings;
    private readonly OscRouter _oscRouter = new();
    private readonly PriorityQueue<OscReceived, DateTimeOffset> _scheduledOsc = new();
    private readonly System.Windows.Forms.Timer _oscTimer = new() { Interval = 20 };
    private DataGridView? _oscMappings;
    private PropertyGrid? _oscMappingProperties;
    private Label? _oscStatus;
    private Guid? _learnOsc;
    private bool _bindingOsc;
    private string? _oscError;
    private string? _oscFeedbackError;
    private long _lastOscFeedback, _lastOscStatus;
    private Guid? _oscEditorId;
    private List<OscMapping>? _boundOscMappings;
    private void InitializeOsc()
    {
        _oscTimer.Tick += (_, _) => PollOsc(); _oscTimer.Start();
        Disposed += (_, _) => { _oscTimer.Dispose(); _osc?.Dispose(); _oscFeedback?.Dispose(); };
        ConfigureOsc(_project.Osc);
    }
    private void OscStatus(string text) { if (_oscStatus is { IsDisposed: false }) _oscStatus.Text = text; }
    private Control CreateOscMappingsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var grid = new DataGridView { Name = "OscMappingList", Dock = DockStyle.Fill, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false,
            BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false };
        _oscMappings = grid;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "Command address", FillWeight = 40, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Control", FillWeight = 30, ReadOnly = true });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", FillWeight = 10 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Feedback", HeaderText = "Feedback", FillWeight = 20 });
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if (_bindingOsc || e.RowIndex < 0 || e.ColumnIndex < 2 || grid.Rows[e.RowIndex].Tag is not Guid id) return;
            var current = WorkingProject.Osc.Mappings.First(m => m.Id == id);
            try { ChangeOscMapping(id, current with { Enabled = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[2].Value), FeedbackEnabled = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[3].Value) }); }
            catch (InvalidDataException ex) { OscStatus(ex.Message); BeginInvoke(() => BindOscMappings(id)); }
        };
        grid.SelectionChanged += (_, _) => { if (!_bindingOsc) BindOscMappingProperties(); };
        _oscMappingProperties = CreatePropertyGrid(); _oscMappingProperties.Name = "OscMappingProperties";
        _oscMappingProperties.PropertyValueChanged += (_, _) => BindOscMappings();
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 7,
            Size = new Size(600, 450), SplitterDistance = 150, Panel1MinSize = 65, Panel2MinSize = 90 };
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(_oscMappingProperties);
        var buttons = new ActionFlowPanel { Dock = DockStyle.Top, Height = 42 };
        buttons.RequiredHeightChanged += height => buttons.Height = height;
        buttons.Controls.Add(Theme.Button("Add mapping", (_, _) => AddOscMapping()));
        buttons.Controls.Add(Theme.Button("Remove", (_, _) => RemoveOscMapping()));
        buttons.Controls.Add(Theme.Button("Learn address", (_, _) =>
        {
            if (_osc == null) { OscStatus("Enable the receiver in Preferences → OSC first."); return; }
            _learnOsc = grid.CurrentRow?.Tag as Guid?;
            OscStatus(_learnOsc == null ? "Select a mapping first." : "Listening… press a control in OSC Pilot.");
        }));
        buttons.Controls.Add(Theme.Button("Reset control defaults", (_, _) => ResetOscMappingDefaults()));
        _oscStatus = Theme.Label("", true); _oscStatus.Dock = DockStyle.Bottom; _oscStatus.Height = 48; _oscStatus.AutoSize = false; _oscStatus.AutoEllipsis = true;
        panel.Controls.Add(split); panel.Controls.Add(buttons); panel.Controls.Add(_oscStatus);
        panel.Disposed += (_, _) => { _oscMappings = null; _oscMappingProperties = null; _oscStatus = null; _oscEditorId = null; _boundOscMappings = null; _learnOsc = null; };
        BindOscMappings(); UpdateOscStatus(); return panel;
    }
    internal void AddOscMapping()
    {
        try
        {
            var mapping = OscMappingDefaults.Create(ControlAction.Play, WorkingProject.Osc.Mappings);
            SetMappings([.. WorkingProject.Osc.Mappings, mapping]); BindOscMappings(mapping.Id);
        }
        catch (Exception ex) { OscStatus(ex.Message); }
    }
    internal void RemoveOscMapping()
    {
        if (_oscMappings?.CurrentRow?.Tag is not Guid id) return;
        try
        {
            _oscMappingProperties!.SelectedObject = null; _oscEditorId = null;
            if (_learnOsc == id) _learnOsc = null;
            SetMappings(WorkingProject.Osc.Mappings.Where(m => m.Id != id).ToList()); BindOscMappings();
        }
        catch (Exception ex) { OscStatus(ex.Message); }
    }
    private void ResetOscMappingDefaults()
    {
        if (_oscMappings?.CurrentRow?.Tag is not Guid id) return;
        var current = WorkingProject.Osc.Mappings.First(m => m.Id == id); var defaults = OscMappingDefaults.ForAction(current.Action);
        ChangeOscMapping(id, current with { Trigger = defaults.Trigger, Minimum = defaults.Minimum, Maximum = defaults.Maximum,
            Argument = 0, FeedbackArgument = 0, FeedbackFormat = OscFeedbackFormat.Float32 });
        _oscMappingProperties?.Refresh();
    }
    private void ChangeOscMapping(Guid id, OscMapping next) => SetMappings(WorkingProject.Osc.Mappings.Select(m => m.Id == id ? next : m).ToList());
    private void SetMappings(List<OscMapping> mappings) => QueueLive("Edit OSC mapping", WorkingProject with { Osc = WorkingProject.Osc with { Mappings = mappings } });
    private void BindOscMappings(Guid? select = null)
    {
        if (_oscMappings is not { IsDisposed: false } grid) return;
        if (select == null && ReferenceEquals(_boundOscMappings, WorkingProject.Osc.Mappings)) return;
        _boundOscMappings = WorkingProject.Osc.Mappings;
        Guid? selected = select ?? grid.CurrentRow?.Tag as Guid?;
        _bindingOsc = true;
        try
        {
            grid.Rows.Clear();
            foreach (var m in WorkingProject.Osc.Mappings)
            {
                int i = grid.Rows.Add(m.Address, m.Action.ToString(), m.Enabled, m.FeedbackEnabled);
                grid.Rows[i].Tag = m.Id; if (m.Id == selected) grid.CurrentCell = grid.Rows[i].Cells[0];
            }
        }
        finally { _bindingOsc = false; }
        BindOscMappingProperties();
    }
    private void BindOscMappingProperties()
    {
        if (_oscMappingProperties is not { IsDisposed: false } editor) return;
        if (_oscMappings?.CurrentRow?.Tag is not Guid id) { editor.SelectedObject = null; _oscEditorId = null; return; }
        if (_oscEditorId == id) { editor.Refresh(); return; } _oscEditorId = id;
        var original = WorkingProject.Osc.Mappings.First(m => m.Id == id);
        OscMapping Current() => WorkingProject.Osc.Mappings.FirstOrDefault(m => m.Id == id) ?? original;
        var bag = new LiveProperties("OSC mapping");
        AddRecord(bag, "01 · Control", Current, (_, v) => ChangeOscMapping(id, v), include: ["Enabled", "Address"]);
        bag.Add("Action", "01 · Control", typeof(ControlAction), () => Current().Action, value =>
        {
            var next = OscMappingDefaults.ChangeAction(Current(), (ControlAction)value!);
            if (next.Address != Current().Address) next = next with { Address = OscMappingDefaults.Create(next.Action, WorkingProject.Osc.Mappings.Where(m => m.Id != id)).Address };
            ChangeOscMapping(id, next); editor.Refresh();
        }, "Choosing a control sets an appropriate button/fader trigger and range. Custom addresses are preserved.");
        AddRecord(bag, "02 · Input value", Current, (_, v) => ChangeOscMapping(id, v), include: ["Trigger", "Argument", "Minimum", "Maximum"]);
        string LayerName(Layer layer) => layer.Name + " [" + layer.Id.ToString()[..8] + "]";
        bag.Add("Target layer", "01 · Control", typeof(string), () => Current().LayerId is Guid target ? WorkingProject.Layers.FirstOrDefault(l => l.Id == target) is { } layer ? LayerName(layer) : "(missing layer)" : "(selected layer)", value =>
        {
            Guid? target = (string?)value == "(selected layer)" ? null : WorkingProject.Layers.FirstOrDefault(l => LayerName(l) == (string?)value)?.Id ?? throw new InvalidDataException("Choose an existing layer.");
            ChangeOscMapping(id, Current() with { LayerId = target });
        }, "Layer controls use this fixed layer, or follow the current selection.", ["(selected layer)", .. WorkingProject.Layers.Select(LayerName)]);
        AddRecord(bag, "03 · Feedback", Current, (_, v) => ChangeOscMapping(id, v), include: ["FeedbackEnabled", "FeedbackAddress", "FeedbackFormat", "FeedbackArgument"]);
        editor.SelectedObject = bag;
    }
    private void ConfigureOsc(OscSettings settings)
    {
        if (_oscSettings == settings) return;
        bool reconnect = _oscSettings == null || _oscSettings.Enabled != settings.Enabled || _oscSettings.Port != settings.Port || _oscSettings.BindAddress != settings.BindAddress;
        if (reconnect)
        {
            _osc?.Dispose(); _osc = null; _scheduledOsc.Clear(); _learnOsc = null; _oscError = null;
            if (settings.Enabled) try { _osc = new(settings.BindAddress, settings.Port); } catch (Exception ex) { _oscError = ex.Message; }
        }
        _oscFeedback?.Dispose(); _oscFeedback = null; _oscFeedbackError = null;
        if (settings.FeedbackEnabled) try { _oscFeedback = new(settings); } catch (Exception ex) { _oscFeedbackError = "Feedback: " + ex.Message; }
        _oscSettings = settings; _oscRouter.Configure(settings.Mappings); _lastOscFeedback = 0;
        UpdateOscStatus();
    }
    internal void SendOscFeedback(bool force = false)
    {
        if (_oscFeedback == null || _projectOperation || _liveBusy) return;
        long now = Environment.TickCount64;
        if (!force && now - _lastOscFeedback < _oscSettings!.FeedbackIntervalMilliseconds) return;
        _lastOscFeedback = now;
        var project = _engine?.Project ?? _project;
        _oscFeedback.Publish(new(_engine?.Transport.Snapshot() ?? default, project.Audio, project.Layers, _selected,
            _engine?.Status.Streaming == true, _recorder != null), force);
    }
    private void UpdateOscStatus()
    {
        string input = _oscError ?? _osc?.Error ?? (_osc == null ? "Input off" : $"Input {_oscSettings!.BindAddress}:{_oscSettings.Port} · {_osc.Received} received");
        string output = _oscFeedbackError ?? _oscFeedback?.Error ?? (_oscFeedback == null ? "Feedback off" : $"Feedback → {_oscSettings!.FeedbackHost}:{_oscSettings.FeedbackPort} · {_oscFeedback.Sent} sent");
        OscStatus(input + "\n" + output);
        if (_preferencesSection == "OSC" && _preferencesFeedback is { IsDisposed: false })
        { _preferencesFeedback.Height = 48; _preferencesFeedback.Text = input + "\n" + output; }
    }
    private void PollOsc()
    {
        if (_projectOperation) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 128 && _osc != null && _osc.TryRead(out var received); i++)
        {
            if (received == null || received.Sender == _oscFeedback?.LocalEndpoint.ToString()) continue;
            if (_learnOsc is Guid id)
            {
                _learnOsc = null;
                try
                {
                    SetMappings(WorkingProject.Osc.Mappings.Select(m => m.Id == id ? m with { Address = received.Message.Address } : m).ToList());
                    BindOscMappings(); OscStatus("Learned " + received.Message.Address);
                }
                catch (InvalidDataException ex) { OscStatus("Cannot learn this address: " + ex.Message); }
                continue;
            }
            if (received.Message.Due is { } due && due > now)
            { if (_scheduledOsc.Count < 512 && due < now.AddMinutes(1)) _scheduledOsc.Enqueue(received, due); continue; }
            DispatchOsc(received);
        }
        for (int i = 0; i < 128 && _scheduledOsc.TryPeek(out _, out var due) && due <= now; i++) DispatchOsc(_scheduledOsc.Dequeue());
        SendOscFeedback();
        if (_learnOsc == null && Environment.TickCount64 - _lastOscStatus > 2000) { _lastOscStatus = Environment.TickCount64; UpdateOscStatus(); }
    }
    private void DispatchOsc(OscReceived received)
    {
        foreach (var invocation in _oscRouter.Route(received.Message, received.Sender))
            _ = InvokeControl(invocation);
    }
    private async Task InvokeControl(ControlInvocation command)
    {
        if (_engine == null) return;
        try
        {
            var p = WorkingProject;
            switch (command.Action)
            {
                case ControlAction.Play: _engine.Transport.Play(); break;
                case ControlAction.Pause: _engine.Transport.Pause(); break;
                case ControlAction.TogglePlay: TogglePlay(); break;
                case ControlAction.Stop: _engine.Transport.Stop(); break;
                case ControlAction.SeekSeconds: _engine.Transport.Seek(TimeSpan.FromSeconds(Math.Max(0, command.Value))); break;
                case ControlAction.SeekNormalized: _engine.Transport.Seek(TimeSpan.FromTicks((long)(_engine.Transport.Snapshot().Duration.Ticks * Math.Clamp(command.Value, 0, 1)))); break;
                case ControlAction.NextMarker: JumpMarker(1); break;
                case ControlAction.PreviousMarker: JumpMarker(-1); break;
                case ControlAction.LocalVolume: QueueLive("OSC local volume", p with { Audio = p.Audio with { LocalVolume = (float)Math.Clamp(command.Value, 0, 2) } }); break;
                case ControlAction.StreamVolume: QueueLive("OSC stream volume", p with { Audio = p.Audio with { StreamVolume = (float)Math.Clamp(command.Value, 0, 2) } }); break;
                case ControlAction.LocalMute: QueueLive("OSC local mute", p with { Audio = p.Audio with { LocalMute = command.Value > 0 } }); break;
                case ControlAction.StreamMute: QueueLive("OSC stream mute", p with { Audio = p.Audio with { StreamMute = command.Value > 0 } }); break;
                case ControlAction.LayerOpacity: case ControlAction.LayerVisible:
                    Guid? layer = command.LayerId ?? _selected;
                    QueueLive("OSC layer", p with { Layers = p.Layers.Select(l => l.Id != layer ? l : command.Action == ControlAction.LayerOpacity
                        ? l with { Opacity = Math.Clamp(command.Value, 0, 1) } : l with { Visible = command.Value > 0 }).ToList() }); break;
                case ControlAction.StartStream: await _engine.StartStreamAsync(); break;
                case ControlAction.StopStream: await _engine.StopStreamAsync(); break;
                case ControlAction.ToggleStream: if (_engine.Status.Streaming) await _engine.StopStreamAsync(); else await _engine.StartStreamAsync(); break;
                case ControlAction.StartDiagnostics: if (_recorder == null) ToggleRecording(); break;
                case ControlAction.StopDiagnostics: if (_recorder != null) ToggleRecording(); break;
            }
        }
        catch (Exception ex) { OscStatus("Control failed: " + ex.Message); }
    }
}
