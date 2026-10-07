namespace StageFlow.App;
public sealed partial class MainWindow
{
    private readonly SplitContainer _workspace = new() { Dock = DockStyle.Fill, SplitterWidth = 7, BackColor = Theme.Background, FixedPanel = FixedPanel.Panel1 };
    private readonly SplitContainer _rightDock = new() { Dock = DockStyle.Fill, SplitterWidth = 7, BackColor = Theme.Background, FixedPanel = FixedPanel.Panel2 };
    private Form? _floatingInspector;
    private AspectPreviewHost _previewHost = null!;
    private TableLayoutPanel _programLayout = null!;
    private bool _settingDockWidths;
    private SurfacePanel _hierarchySurface = null!;
    private TableLayoutPanel _hierarchyLayout = null!;
    private readonly Label _renderCard = Theme.Label("GPU engine · starting", true), _routeCard = Theme.Label("Outputs · idle", true);
    private void BuildLayout()
    {
        SuspendLayout();
        MinimumSize = new Size(1100, 720); Size = new Size(1600, 1000);
        var menu = new MenuStrip { BackColor = Theme.Background, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), Padding = new Padding(12, 5, 0, 5) };
        MainMenuStrip = menu;
        var file = new ToolStripMenuItem("File"); _fileMenu = CreateFileMenu(); _fileMenu.Renderer = new DarkMenuRenderer(); file.DropDown = _fileMenu;
        menu.Items.Add(file);
        ToolStripMenuItem Menu(string text) { var item = new ToolStripMenuItem(text); menu.Items.Add(item); return item; }
        var edit = Menu("Edit"); edit.DropDownItems.Add("Undo    Ctrl+Z", null, async (_, _) => await Undo()); edit.DropDownItems.Add("Redo    Ctrl+Y", null, async (_, _) => await Redo());
        edit.DropDownItems.Add(new ToolStripSeparator()); edit.DropDownItems.Add("Duplicate layer", null, async (_, _) => await DuplicateLayer()); edit.DropDownItems.Add("Preferences…", null, (_, _) => ShowPreferences());
        var view = Menu("View"); view.DropDownItems.Add("Hierarchy", null, (_, _) => _workspace.Panel1Collapsed = !_workspace.Panel1Collapsed);
        view.DropDownItems.Add("Inspector", null, (_, _) => _rightDock.Panel2Collapsed = !_rightDock.Panel2Collapsed);
        view.DropDownItems.Add("Float / dock inspector", null, (_, _) => ToggleInspectorDock());
        view.DropDownItems.Add("Reset workspace", null, (_, _) => { _workspace.Panel1Collapsed = _rightDock.Panel2Collapsed = false; ResetDockWidths(); });
        var show = Menu("Show"); show.DropDownItems.Add("Play / pause    Space", null, (_, _) => TogglePlay()); show.DropDownItems.Add("Stop", null, (_, _) => _engine?.Transport.Stop());
        show.DropDownItems.Add("Add marker    M", null, async (_, _) => await AddMarker()); show.DropDownItems.Add("Fit selected video", null, async (_, _) => await FitLayer());
        var routes = Menu("Connections"); foreach (string scope in new[] { "Audio", "Timecode", "Art-Net", "MIDI-DMX", "Outputs", "OSC" }) routes.DropDownItems.Add(scope, null, (_, _) => Inspect(scope));
        routes.DropDownItems.Add("Refresh devices", null, (_, _) => PopulateRoutes()); routes.DropDownItems.Add("OSC mappings", null, (_, _) => ShowPreferences("OSC mappings"));
        routes.DropDownItems.Add("Network destinations…", null, (_, _) => ShowNetworkOutputs());
        routes.DropDownItems.Add("NDI® information", null, (_, _) => OpenExternal("https://ndi.video/"));
        foreach (var protocol in Enum.GetValues<StageFlow.Core.Projects.NetworkSourceProtocol>())
            routes.DropDownItems.Add("Add " + protocol + " input…", null, async (_, _) => await AddNetworkSource(protocol));
        var help = Menu("Help"); help.DropDownItems.Add("Open documentation", null, (_, _) => OpenExternal(Path.Combine(AppContext.BaseDirectory, "README.md")));
        help.DropDownItems.Add("About StageFlow v2", null, (_, _) => MessageBox.Show(this, "StageFlow " + Program.DisplayVersion + "\nGPU show playback · media routing · OSC\nIndependent v2 workspace", "StageFlow v2"));
        foreach (ToolStripMenuItem item in menu.Items) item.DropDown.Renderer = new DarkMenuRenderer();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18, 8, 18, 12), BackColor = Theme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 196)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root); Controls.Add(menu);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(new Label { Text = "stageflow / 02", Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Theme.Text, AutoSize = true });
        brand.Controls.Add(Theme.Label("LIVE PRODUCTION", true)); header.Controls.Add(brand, 0, 0);
        var files = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0), WrapContents = false };
        files.Controls.Add(Theme.Button("Open movie…", async (_, _) => await OpenMovie())); files.Controls.Add(Theme.Button("Save show", (_, _) => SaveProject())); header.Controls.Add(files, 1, 0);
        var outputs = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0), WrapContents = false }; outputs.Controls.Add(_arm); outputs.Controls.Add(_stream); header.Controls.Add(outputs, 2, 0); root.Controls.Add(header, 0, 0);

        var hierarchy = new SurfacePanel { Dock = DockStyle.Fill, AutoScroll = true }; _hierarchySurface = hierarchy;
        var hierarchyLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 7 }; _hierarchyLayout = hierarchyLayout;
        hierarchy.SizeChanged += (_, _) => FitHierarchy();
        hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); hierarchyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        hierarchyLayout.Controls.Add(Theme.Label("HIERARCHY", true), 0, 0); hierarchyLayout.Controls.Add(_layers, 0, 1);
        var tools = new ActionFlowPanel { Dock = DockStyle.Fill };
        tools.RequiredHeightChanged += height => { if (hierarchyLayout.RowStyles[2].Height != height + 6) hierarchyLayout.RowStyles[2].Height = height + 6; FitHierarchy(); };
        var addMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer() };
        addMenu.Items.Add("Movie…", null, async (_, _) => await OpenMovie()); addMenu.Items.Add("Timecode overlay", null, async (_, _) => await AddClock());
        addMenu.Items.Add("Art-Net grid", null, async (_, _) => await AddGrid()); addMenu.Items.Add("Spout input…", null, async (_, _) => await AddSpout());
        foreach (var protocol in Enum.GetValues<StageFlow.Core.Projects.NetworkSourceProtocol>()) addMenu.Items.Add(protocol + " input…", null, async (_, _) => await AddNetworkSource(protocol));
        var addSource = Theme.Button("+ Add"); addSource.Click += (_, _) => addMenu.Show(addSource, new Point(0, addSource.Height)); tools.Controls.Add(addSource);
        Disposed += (_, _) => addMenu.Dispose();
        tools.Controls.Add(Theme.Button("Copy", async (_, _) => await DuplicateLayer())); tools.Controls.Add(Theme.Button("↑", async (_, _) => await MoveLayer(1))); tools.Controls.Add(Theme.Button("↓", async (_, _) => await MoveLayer(-1))); tools.Controls.Add(Theme.Button("×", async (_, _) => await RemoveLayer())); hierarchyLayout.Controls.Add(tools, 0, 2);
        hierarchyLayout.Controls.Add(Theme.Label("CUE MARKERS", true), 0, 3); hierarchyLayout.Controls.Add(_markers, 0, 4);
        var markers = new ActionFlowPanel { Dock = DockStyle.Fill };
        markers.RequiredHeightChanged += height => { if (hierarchyLayout.RowStyles[5].Height != height + 6) hierarchyLayout.RowStyles[5].Height = height + 6; FitHierarchy(); }; markers.Controls.Add(Theme.Button("+ Marker", async (_, _) => await AddMarker())); markers.Controls.Add(Theme.Button("Remove", async (_, _) => await RemoveMarker())); hierarchyLayout.Controls.Add(markers, 0, 5);
        hierarchyLayout.Controls.Add(Theme.Button("Show properties", (_, _) => Inspect("Show & canvas")), 0, 6); hierarchy.Controls.Add(hierarchyLayout);
        _workspace.Panel1.Controls.Add(hierarchy); _workspace.Panel2.Controls.Add(_rightDock);
        var program = new SurfacePanel { Dock = DockStyle.Fill };
        var programLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        programLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); programLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); programLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _projectTitle.Font = new Font("Segoe UI", 11, FontStyle.Bold); programLayout.Controls.Add(_projectTitle, 0, 0); _programLayout = programLayout; _previewHost = new(_preview); programLayout.Controls.Add(_previewHost, 0, 1);
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true }; cards.Controls.Add(_renderCard); cards.Controls.Add(_routeCard); programLayout.Controls.Add(cards, 0, 2); program.Controls.Add(programLayout);
        _rightDock.Panel1.Controls.Add(program); _rightDock.Panel2.Controls.Add(_inspectorTabs); root.Controls.Add(_workspace, 0, 1);

        var transport = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        var timeline = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        timeline.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); timeline.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); timeline.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var time = new FlowLayoutPanel { Dock = DockStyle.Fill }; _clock.ForeColor = Theme.Cyan; time.Controls.Add(_clock); time.Controls.Add(_position); timeline.Controls.Add(time, 0, 0); timeline.Controls.Add(_timeline, 0, 1);
        var controls = new ActionFlowPanel { Dock = DockStyle.Fill };
        controls.RequiredHeightChanged += height => { if (root.RowStyles[2].Height != height + 122) root.RowStyles[2].Height = height + 122; }; controls.Controls.Add(Theme.Button("■ Stop", (_, _) => _engine?.Transport.Stop())); controls.Controls.Add(_play);
        controls.Controls.Add(Theme.Button("◀ Frame", (_, _) => _engine?.StepFrame(-1))); controls.Controls.Add(Theme.Button("Frame ▶", (_, _) => _engine?.StepFrame(1)));
        controls.Controls.Add(Theme.Button("In [I]", async (_, _) => await SetLoopPoint(false))); controls.Controls.Add(Theme.Button("Out [O]", async (_, _) => await SetLoopPoint(true))); controls.Controls.Add(_loop);
        controls.Controls.Add(Theme.Label("Go to", true)); _goto.Width = 122; _goto.Margin = new Padding(5, 9, 5, 0); controls.Controls.Add(_goto); controls.Controls.Add(Theme.Button("Go", (_, _) => GoTo()));
        controls.Controls.Add(Theme.Button("Player ↗", (_, _) => OpenExternal($"http://127.0.0.1:{_project.Stream.Port}/"))); controls.Controls.Add(Theme.Button("Snapshot", async (_, _) => await SaveFrame())); timeline.Controls.Add(controls, 0, 2);
        transport.Controls.Add(timeline); root.Controls.Add(transport, 0, 2);
        _status.AutoSize = false; _status.AutoEllipsis = true; _status.Dock = DockStyle.Fill; _status.Margin = Padding.Empty; _status.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(_status, 0, 3);
        _layers.BackColor = _markers.BackColor = Theme.Panel; _layers.ForeColor = _markers.ForeColor = Theme.Text; _timeline.BackColor = Theme.Panel;
        _tips.SetToolTip(_arm, "Enable configured MIDI outputs to grandMA2 / gridnode.");
        Shown += (_, _) =>
        {
            if (_preferences.RememberWindow && _preferences.WindowX != int.MinValue)
            {
                var restored = new Rectangle(_preferences.WindowX, _preferences.WindowY, Math.Max(MinimumSize.Width, _preferences.WindowWidth), Math.Max(MinimumSize.Height, _preferences.WindowHeight));
                var screen = Screen.AllScreens.FirstOrDefault(s => Rectangle.Intersect(s.WorkingArea, restored) is { Width: > 80, Height: > 80 }) ?? Screen.FromControl(this);
                var area = screen.WorkingArea; restored.Width = Math.Min(restored.Width, area.Width); restored.Height = Math.Min(restored.Height, area.Height);
                restored.X = Math.Clamp(restored.X, area.Left, Math.Max(area.Left, area.Right-restored.Width)); restored.Y = Math.Clamp(restored.Y, area.Top, Math.Max(area.Top, area.Bottom-restored.Height));
                Bounds = restored; if (_preferences.WindowMaximized) WindowState = FormWindowState.Maximized;
            }
            ResetDockWidths();
        };
        _workspace.SplitterMoved += (_, _) => { if (!_settingDockWidths && _workspace.Panel1.Width > 100) _preferences.HierarchyWidth = _workspace.Panel1.Width; };
        _rightDock.SplitterMoved += (_, _) => { if (!_settingDockWidths && _rightDock.Panel2.Width > 100) _preferences.InspectorWidth = _rightDock.Panel2.Width; };
        _layers.SelectedIndexChanged += (_, _) => { if (!_binding && _layers.SelectedItem != null) Inspect("Selection"); };
        ResumeLayout();
    }
    private void FitHierarchy()
    {
        if (_hierarchyLayout == null || _hierarchySurface == null) return;
        if (_hierarchyLayout.RowStyles.Count < 7) return;
        _hierarchyLayout.RowStyles[0].Height = _hierarchyLayout.RowStyles[3].Height = Math.Max(32, Font.Height + 14);
        _hierarchyLayout.RowStyles[6].Height = Math.Max(42, (_hierarchyLayout.GetControlFromPosition(0, 6)?.GetPreferredSize(Size.Empty).Height ?? 34) + 6);
        int fixedHeight = (int)_hierarchyLayout.RowStyles.Cast<RowStyle>().Where(r => r.SizeType == SizeType.Absolute).Sum(r => r.Height);
        int height = Math.Max(fixedHeight + 160, _hierarchySurface.ClientSize.Height - _hierarchySurface.Padding.Vertical);
        if (_hierarchyLayout.Height != height) _hierarchyLayout.Height = height;
    }
    private void ResetDockWidths()
    {
        if (_workspace.Width < 50 || _rightDock.Width < 50) return;
        _settingDockWidths = true;
        try
        {
            _workspace.SplitterDistance = Math.Clamp(_preferences.HierarchyWidth, 150, Math.Max(150, _workspace.Width - 610));
            int inspector = Math.Clamp(_preferences.InspectorWidth, 280, Math.Max(280, _rightDock.Width - 250));
            _rightDock.SplitterDistance = Math.Clamp(_rightDock.Width - inspector - _rightDock.SplitterWidth, 100, Math.Max(100, _rightDock.Width-100));
        }
        finally { _settingDockWidths = false; }
    }
    private void ToggleInspectorDock()
    {
        if (_floatingInspector != null) { _floatingInspector.Close(); return; }
        _floatingInspector = new Form { Text = "StageFlow v2 · Inspector", Size = new Size(600, 760), BackColor = Theme.Panel, Owner = this };
        _floatingInspector.Controls.Add(_inspectorTabs); _rightDock.Panel2Collapsed = true;
        _floatingInspector.FormClosed += (_, _) => { _rightDock.Panel2.Controls.Add(_inspectorTabs); _rightDock.Panel2Collapsed = false; _floatingInspector = null; };
        Theme.Apply(_floatingInspector, _preferences.TextSize); _floatingInspector.Show(this);
    }
}
