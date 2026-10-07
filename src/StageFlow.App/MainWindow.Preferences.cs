using System.ComponentModel;
namespace StageFlow.App;
public sealed partial class MainWindow
{
    private Form? _preferencesWindow;
    private ListBox? _preferencesSections;
    private PropertyGrid? _preferencesGrid;
    private Label? _preferencesFeedback;
    private string _preferencesSection = "General";
    internal static readonly string[] PreferenceSections = ["General", "Appearance", "Workspace", "Video & canvas", "Audio", "Timecode", "Art-Net", "MIDI-DMX", "Encoding", "HLS server", "Spout", "NDI & OMT", "RTMP & SRT", "OSC", "OSC mappings", "Diagnostics"];
    internal void ShowPreferences(string section = "General")
    {
        section = section switch { "Streaming" or "Outputs" => "Encoding", "Art-Net & MIDI" => "Art-Net", _ => section };
        if (_preferencesWindow is { IsDisposed: false }) { _preferencesSections!.SelectedItem = section; _preferencesWindow.Activate(); return; }
        var dialog = new Form { Text = "StageFlow v2 · Preferences", Size = new Size(880, 720), MinimumSize = new Size(740, 560), BackColor = Theme.Background,
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        _preferencesWindow = dialog;
        var nav = new ListBox { Dock = DockStyle.Left, Width = 180, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36, IntegralHeight = false };
        _preferencesSections = nav; nav.Items.AddRange(PreferenceSections);
        nav.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(selected ? Theme.Selection : Theme.Background); e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, nav.Items[e.Index].ToString(), nav.Font, new Rectangle(e.Bounds.X+16,e.Bounds.Y,e.Bounds.Width-24,e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 18, 10) };
        var heading = Theme.Label("Preferences"); heading.Font = new Font("Segoe UI", 18, FontStyle.Bold); heading.Dock = DockStyle.Top; heading.Height = 52; heading.AutoSize = false;
        var note = Theme.Label("", true); note.Dock = DockStyle.Bottom; note.Height = 55; note.AutoSize = false;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 86, AutoScroll = true };
        _preferencesGrid = CreatePropertyGrid(); _preferencesGrid.Name = "PreferenceProperties";
        var oscPanel = CreateOscMappingsPanel(); oscPanel.Visible = false;
        _preferencesFeedback = Theme.Label("Changes update automatically", true); _preferencesFeedback.Dock = DockStyle.Bottom; _preferencesFeedback.AutoSize = false; _preferencesFeedback.Height = 32;
        var search = Theme.TextBox(); search.Dock = DockStyle.Top; search.PlaceholderText = "Search all settings…"; search.AccessibleName = "Search all settings";
        void BindSection()
        {
            _preferencesSection = nav.SelectedItem?.ToString() ?? "General"; heading.Text = _preferencesSection;
            _preferencesFeedback.Height = 32; _preferencesFeedback.Text = "Changes update automatically";
            bool local = _preferencesSection is "General" or "Appearance" or "Workspace" or "Diagnostics";
            note.Text = local ? "Personal preferences · saved automatically on this computer." : "Current show settings · update live and are stored with the show. Streaming changes restart the affected output.";
            foreach (Control old in actions.Controls.Cast<Control>().ToArray()) old.Dispose();
            _preferencesGrid.SelectedObject = string.IsNullOrWhiteSpace(search.Text) ? BuildPreferenceProperties(_preferencesSection) : SearchPreferences(search.Text);
            bool mappingView = _preferencesSection == "OSC mappings" && string.IsNullOrWhiteSpace(search.Text);
            _preferencesGrid.Visible = !mappingView; oscPanel.Visible = mappingView;
            if (mappingView) { oscPanel.BringToFront(); BindOscMappings(); }
            if (!string.IsNullOrWhiteSpace(search.Text)) heading.Text = "Search results";
            if (_preferencesSection == "RTMP & SRT") actions.Controls.Add(Theme.Button("RTMP / SRT destinations…", (_, _) => ShowNetworkOutputs()));
            if (_preferencesSection == "OSC")
            {
                actions.Controls.Add(Theme.Button("Control mappings", (_, _) => nav.SelectedItem = "OSC mappings"));
                actions.Controls.Add(Theme.Button("Send current state", (_, _) =>
                {
                    if (_oscFeedback == null) _preferencesFeedback.Text = "Enable feedback and set the controller's IP/port first.";
                    else { SendOscFeedback(true); UpdateOscStatus(); }
                }));
                actions.Controls.Add(Theme.Button("Reconnect OSC", async (_, _) => { await DrainLiveEdits(); _oscSettings = null; ConfigureOsc(_project.Osc); }));
            }
            if (_preferencesSection is "Audio" or "Timecode" or "MIDI-DMX") actions.Controls.Add(Theme.Button("Refresh device list", (_, _) => { PopulateRoutes(); BindSection(); }));
            if (_preferencesSection == "General")
            { actions.Controls.Add(Theme.Button("Export preferences…", (_, _) => ExportPreferences())); actions.Controls.Add(Theme.Button("Import preferences…", (_, _) => { ImportPreferences(); BindSection(); })); }
            if (_preferencesSection == "Workspace") actions.Controls.Add(Theme.Button("Reset workspace", (_, _) => { _preferences.HierarchyWidth = 260; _preferences.InspectorWidth = 360; _workspace.Panel1Collapsed = _rightDock.Panel2Collapsed = false; ApplyPreferences(true); }));
            if (_preferencesSection == "Appearance") actions.Controls.Add(Theme.Button("Reset appearance", (_, _) => { _preferences.Skin = AppSkin.Slate; _preferences.Accent = AppAccent.Periwinkle; _preferences.TextSize = 9; _preferences.CompactRows = false; ApplyPreferences(); }));
            if (_preferencesSection == "Diagnostics")
            {
                actions.Controls.Add(Theme.Button("Open logs", (_, _) => { Directory.CreateDirectory(_diagnosticFolder); OpenExternal(_diagnosticFolder); }));
                actions.Controls.Add(Theme.Button("Reconnect routes", async (_, _) => await ReconnectRoutes()));
                actions.Controls.Add(Theme.Button("Reset counters", async (_, _) => await Guard(() => _engine!.ResetDiagnosticsAsync())));
            }
            actions.Visible = !mappingView; note.Height = mappingView ? 48 : 55;
            if (mappingView) note.Text = "Select a mapping to edit it below. Feedback uses the command address unless you set a separate return address.";
            Theme.Apply(dialog, _preferences.TextSize);
            if (_preferencesSection == "OSC") UpdateOscStatus();
        }
        nav.SelectedIndexChanged += (_, _) => { search.Clear(); BindSection(); };
        search.TextChanged += (_, _) => BindSection();
        body.Controls.Add(oscPanel); body.Controls.Add(_preferencesGrid); body.Controls.Add(search); body.Controls.Add(heading); body.Controls.Add(actions); body.Controls.Add(note); body.Controls.Add(_preferencesFeedback);
        dialog.Controls.Add(body); dialog.Controls.Add(nav);
        dialog.FormClosed += (_, _) => { _preferencesWindow = null; _preferencesGrid = null; _preferencesSections = null; _preferencesFeedback = null; };
        dialog.KeyPreview = true; dialog.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape && search.TextLength > 0) { search.Clear(); e.SuppressKeyPress = true; } };
        nav.SelectedItem = PreferenceSections.Contains(section) ? section : "General";
        dialog.Show(this);
    }
    internal LiveProperties BuildPreferenceProperties(string section)
    {
        var bag = new LiveProperties(section);
        if (section is "General" or "Appearance" or "Workspace" or "Diagnostics")
        {
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(_preferences))
            {
                if (property.Category != section) continue;
                bag.Add(property.DisplayName, section, property.PropertyType, () => property.GetValue(_preferences), value =>
                {
                    property.SetValue(_preferences, value); ApplyPreferences(section == "Workspace");
                }, property.Description);
            }
        }
        else switch (section)
        {
            case "Audio": AddRecord(bag, "Playback & monitoring", () => WorkingProject.Audio, (p,v) => QueueLive("Edit audio", p with { Audio = v })); break;
            case "Timecode": AddRecord(bag, "Master clock & MIDI", () => WorkingProject.Clock, (p,v) => QueueLive("Edit timecode", p with { Clock = v })); break;
            case "Video & canvas": AddRecord(bag, "Program format", () => WorkingProject, (_,v) => QueueLive("Edit canvas", v), include: ["Name", "Width", "Height", "FpsNumerator", "FpsDenominator", "GpuAdapter"]); break;
            case "Art-Net": AddRecord(bag, "Art-Net receiver", () => WorkingProject.ArtNet, (p,v) => QueueLive("Edit Art-Net", p with { ArtNet = v }), ["HoldLastSeconds"]); break;
            case "MIDI-DMX": AddRecord(bag, "Gridnode MIDI-DMX", () => WorkingProject.MidiDmx, (p,v) => QueueLive("Edit MIDI-DMX", p with { MidiDmx = v })); break;
            case "Encoding":
                AddRecord(bag, "01 · Video encoder", () => WorkingProject.Stream, (p,v) => QueueLive("Edit encoding", p with { Stream = v }),
                    include: ["Encoder", "RateControl", "VideoBitrateKbps", "MaxBitrateKbps", "BufferSeconds", "Quality", "Preset", "Profile", "KeyframeSeconds", "BFrames"]);
                AddRecord(bag, "02 · Output format", () => WorkingProject, (_,v) => QueueLive("Edit canvas", v), include: ["Width", "Height", "FpsNumerator", "FpsDenominator"]);
                AddRecord(bag, "03 · Program audio", () => WorkingProject.Stream, (p,v) => QueueLive("Edit encoding", p with { Stream = v }), include: ["AudioBitrateKbps"]);
                bag.Add("Audio format", "03 · Program audio", typeof(string), () => "AAC · 48 kHz · stereo", null, "Program audio is included in HLS, RTMP and SRT.");
                break;
            case "HLS server": AddRecord(bag, "Built-in HLS server", () => WorkingProject.Stream, (p,v) => QueueLive("Edit HLS", p with { Stream = v }), include: ["HlsEnabled", "BindAddress", "Port", "SegmentSeconds", "PlaylistSegments"]); break;
            case "Spout": AddRecord(bag, "Spout sender", () => WorkingProject.Spout, (p,v) => QueueLive("Edit Spout", p with { Spout = v })); break;
            case "NDI & OMT": AddRecord(bag, "LAN program outputs", () => WorkingProject.LanVideo, (p,v) => QueueLive("Edit LAN outputs", p with { LanVideo = v })); break;
            case "RTMP & SRT":
                bag.Add("Configured destinations", "Network streaming", typeof(int), () => WorkingProject.NetworkOutputs.Count, null, "Open destinations below to add, name, enable and configure each RTMP or SRT endpoint.");
                bag.Add("Enabled destinations", "Network streaming", typeof(int), () => WorkingProject.NetworkOutputs.Count(o => o.Enabled), null, "Enabled destinations start with Start stream and share the video/audio encoding settings."); break;
            case "OSC":
                AddRecord(bag, "01 · Receive commands", () => WorkingProject.Osc, (p,v) => QueueLive("Edit OSC", p with { Osc = v }), include: ["Enabled", "BindAddress", "Port"]);
                AddRecord(bag, "02 · Send feedback", () => WorkingProject.Osc, (p,v) => QueueLive("Edit OSC feedback", p with { Osc = v }), include: ["FeedbackEnabled", "FeedbackHost", "FeedbackPort", "FeedbackIntervalMilliseconds", "FeedbackRefreshSeconds"]); break;
            case "OSC mappings": bag.Add("Mappings", "OSC controls", typeof(int), () => WorkingProject.Osc.Mappings.Count, null, "Open OSC mappings to add, remove, learn and edit controls."); break;
        }
        return bag;
    }
    internal LiveProperties SearchPreferences(string query)
    {
        var result = new LiveProperties("Search results");
        foreach (string section in PreferenceSections)
        {
            var properties = BuildPreferenceProperties(section); properties.Filter = query;
            foreach (PropertyDescriptor property in properties.GetProperties()) result.Include(section, property, properties);
        }
        return result;
    }
    private void ExportPreferences()
    {
        using var file = new SaveFileDialog { Filter = "StageFlow preferences|*.stageflowprefs", FileName = "My StageFlow preferences.stageflowprefs" };
        if (file.ShowDialog(_preferencesWindow) != DialogResult.OK) return;
        try { File.WriteAllText(file.FileName, System.Text.Json.JsonSerializer.Serialize(_preferences, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ImportPreferences()
    {
        using var file = new OpenFileDialog { Filter = "StageFlow preferences|*.stageflowprefs" };
        if (file.ShowDialog(_preferencesWindow) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(file.FileName).Length > 1024 * 1024) throw new InvalidDataException("Preferences file is too large.");
            var next = System.Text.Json.JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(file.FileName)) ?? throw new InvalidDataException("Empty preferences file.");
            next.Normalize(); _preferences = next; ApplyPreferences(true);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ApplyPreferences(bool resizeDocks = false)
    {
        _preferences.Normalize(); Theme.Configure(_preferences);
        foreach (Form form in Application.OpenForms) if (form == this || form.Owner == this) Theme.Apply(form, _preferences.TextSize);
        Theme.Apply(this, _preferences.TextSize); _clock.ForeColor = Theme.Cyan;
        _layers.ItemHeight = _preferences.CompactRows ? Math.Max(30, _preferences.TextSize * 3 + 4) : Math.Max(48, _preferences.TextSize * 4 + 8); FitHierarchy(); _layers.Invalidate();
        _uiTimer.Interval = _preferences.UiRefreshMilliseconds; _autosave.Interval = _preferences.AutosaveSeconds * 1000;
        _tips.Active = _preferences.ShowTooltips; _renderCard.Visible = _routeCard.Visible = _preferences.ShowStatusCards;
        _programLayout.RowStyles[2].Height = _preferences.ShowStatusCards ? 48 : 0;
        if (resizeDocks && IsHandleCreated) ResetDockWidths();
        try { _preferences.Save(_stateDirectory); } catch (IOException ex) { _status.Text = "Preferences: " + ex.Message; }
        _preferencesGrid?.Refresh();
    }
}
