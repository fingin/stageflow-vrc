using StageFlow.Core.Osc;
using StageFlow.Core.Projects;

namespace StageFlow.App;
public sealed partial class MainWindow
{
    private OscListener? _osc;
    private OscSettings? _oscSettings;
    private readonly OscRouter _oscRouter = new();
    private readonly PriorityQueue<OscReceived, DateTimeOffset> _scheduledOsc = new();
    private readonly System.Windows.Forms.Timer _oscTimer = new() { Interval = 20 };
    private readonly DataGridView _oscMappings = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false,
        BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false };
    private readonly Label _oscStatus = Theme.Label("OSC listener is off", true);
    private Guid? _learnOsc;
    private bool _bindingOsc;
    private string? _oscError;
    private void BuildOscPanel()
    {
        var page = new Panel { Text = "OSC", BackColor = Theme.Panel, Padding = new Padding(8) };
        _oscMappings.DefaultCellStyle = new() { BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Selection, SelectionForeColor = Color.White };
        _oscMappings.ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.Input, ForeColor = Theme.Muted };
        _oscMappings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "OSC address", Width = 150 });
        _oscMappings.Columns.Add(new DataGridViewComboBoxColumn { Name = "Action", HeaderText = "Control", DataSource = Enum.GetNames<ControlAction>(), Width = 135 });
        _oscMappings.Columns.Add(new DataGridViewComboBoxColumn { Name = "Trigger", HeaderText = "Trigger", DataSource = Enum.GetNames<OscTrigger>(), Width = 95 });
        _oscMappings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Minimum", HeaderText = "From", Width = 65 });
        _oscMappings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Maximum", HeaderText = "To", Width = 65 });
        _oscMappings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Argument", HeaderText = "Arg", Width = 50 });
        _oscMappings.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", Width = 42 });
        _oscMappings.CurrentCellDirtyStateChanged += (_, _) => { if (_oscMappings.IsCurrentCellDirty) _oscMappings.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _oscMappings.DataError += (_, e) => { e.ThrowException = false; _oscStatus.Text = "Check the mapping value."; };
        _oscMappings.CellValueChanged += (_, e) =>
        {
            if (_bindingOsc || e.RowIndex < 0 || _oscMappings.Rows[e.RowIndex].Tag is not Guid id) return;
            try
            {
                var row = _oscMappings.Rows[e.RowIndex]; var original = WorkingProject.Osc.Mappings.First(m => m.Id == id);
                var changed = original with { Address = Convert.ToString(row.Cells[0].Value) ?? "", Action = Enum.Parse<ControlAction>(Convert.ToString(row.Cells[1].Value)!),
                    Trigger = Enum.Parse<OscTrigger>(Convert.ToString(row.Cells[2].Value)!), Minimum = Convert.ToDouble(row.Cells[3].Value), Maximum = Convert.ToDouble(row.Cells[4].Value),
                    Argument = Convert.ToInt32(row.Cells[5].Value), Enabled = Convert.ToBoolean(row.Cells[6].Value) };
                SetMappings(WorkingProject.Osc.Mappings.Select(m => m.Id == id ? changed : m).ToList());
            }
            catch (Exception ex) { _oscStatus.Text = ex.Message; }
        };
        var buttons = new ActionFlowPanel { Dock = DockStyle.Top, Height = 42 };
        buttons.RequiredHeightChanged += height => buttons.Height = height;
        buttons.Controls.Add(Theme.Button("+ Add", (_, _) => { SetMappings([.. WorkingProject.Osc.Mappings, new()]); BindOscMappings(); }));
        buttons.Controls.Add(Theme.Button("Learn", (_, _) =>
        {
            if (_osc == null) { _oscStatus.Text = "Enable the listener in Preferences → OSC first."; return; }
            _learnOsc = _oscMappings.CurrentRow?.Tag as Guid?;
            _oscStatus.Text = _learnOsc == null ? "Select a mapping first." : "Listening… press a button in OSC Pilot.";
        }));
        var more = new ContextMenuStrip { Renderer = new DarkMenuRenderer() };
        more.Items.Add("Bind selected layer", null, (_, _) =>
        {
            if (_oscMappings.CurrentRow?.Tag is Guid id && _selected != null)
            { SetMappings(WorkingProject.Osc.Mappings.Select(m => m.Id == id ? m with { LayerId = _selected } : m).ToList()); _oscStatus.Text = "Mapping targets " + SelectedLayer?.Name; }
        });
        more.Items.Add("Remove mapping", null, (_, _) => { if (_oscMappings.CurrentRow?.Tag is Guid id) { SetMappings(WorkingProject.Osc.Mappings.Where(m => m.Id != id).ToList()); BindOscMappings(); } });
        more.Items.Add("Listener settings", null, (_, _) => Inspect("OSC"));
        more.Items.Add("Float / dock editor", null, (_, _) => ToggleInspectorDock());
        var moreButton = Theme.Button("More…"); moreButton.Click += (_, _) => more.Show(moreButton, new Point(0, moreButton.Height)); buttons.Controls.Add(moreButton);
        Disposed += (_, _) => more.Dispose();
        _oscStatus.Dock = DockStyle.Bottom; _oscStatus.Height = 72; _oscStatus.AutoSize = false;
        page.Controls.Add(_oscMappings); page.Controls.Add(buttons); page.Controls.Add(_oscStatus); _inspectorTabs.TabPages.Add(page);
        _oscTimer.Tick += (_, _) => PollOsc(); _oscTimer.Start();
        Disposed += (_, _) => { _oscTimer.Dispose(); _osc?.Dispose(); _osc = null; };
    }
    private void SetMappings(List<OscMapping> mappings) => QueueLive("Edit OSC mapping", WorkingProject with { Osc = WorkingProject.Osc with { Mappings = mappings } });
    private void BindOscMappings()
    {
        if (_oscMappings.IsCurrentCellInEditMode) return;
        Guid? selected = _oscMappings.CurrentRow?.Tag as Guid?;
        _bindingOsc = true;
        try
        {
            _oscMappings.Rows.Clear();
            foreach (var m in WorkingProject.Osc.Mappings)
            {
                int i = _oscMappings.Rows.Add(m.Address, m.Action.ToString(), m.Trigger.ToString(), m.Minimum, m.Maximum, m.Argument, m.Enabled);
                _oscMappings.Rows[i].Tag = m.Id; if (m.Id == selected) _oscMappings.CurrentCell = _oscMappings.Rows[i].Cells[0];
            }
        }
        finally { _bindingOsc = false; }
    }
    private void ConfigureOsc(OscSettings settings)
    {
        if (_oscSettings == settings) return;
        bool reconnect = _oscSettings == null || _oscSettings.Enabled != settings.Enabled || _oscSettings.Port != settings.Port || _oscSettings.BindAddress != settings.BindAddress;
        if (reconnect)
        {
            _osc?.Dispose(); _osc = null; _scheduledOsc.Clear(); _oscError = null;
            if (settings.Enabled)
                try { _osc = new(settings.BindAddress, settings.Port); }
                catch (Exception ex) { _oscError = ex.Message; }
        }
        _oscSettings = settings; _oscRouter.Configure(settings.Mappings);
        _oscStatus.Text = _oscError ?? (settings.Enabled ? $"Listening on {settings.BindAddress}:{settings.Port}" : "OSC listener is off");
    }
    private void PollOsc()
    {
        if (_osc == null || _projectOperation) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 128 && _osc.TryRead(out var received); i++)
        {
            if (received == null) continue;
            if (_learnOsc is Guid id)
            {
                _learnOsc = null;
                try
                {
                    SetMappings(WorkingProject.Osc.Mappings.Select(m => m.Id == id ? m with { Address = received.Message.Address } : m).ToList());
                    BindOscMappings(); _oscStatus.Text = "Learned " + received.Message.Address;
                }
                catch (InvalidDataException ex) { _oscStatus.Text = "Cannot learn this address: " + ex.Message; }
                continue;
            }
            if (received.Message.Due is { } due && due > now)
            { if (_scheduledOsc.Count < 512 && due < now.AddMinutes(1)) _scheduledOsc.Enqueue(received, due); continue; }
            DispatchOsc(received);
        }
        for (int i = 0; i < 128 && _scheduledOsc.TryPeek(out _, out var due) && due <= now; i++) DispatchOsc(_scheduledOsc.Dequeue());
        if (_osc.Error != null) _oscStatus.Text = _osc.Error;
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
        catch (Exception ex) { _oscStatus.Text = "Control failed: " + ex.Message; }
    }
}
