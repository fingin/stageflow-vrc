using StageFlow.Core.Projects;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    private ContextMenuStrip? _fileMenu;
    private async Task OpenMovie()
    {
        using var dialog = new OpenFileDialog { Title = "Open show movie", Filter = "Video files|*.mp4;*.m4v;*.mov;*.mkv;*.avi|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadMovie(dialog.FileName);
    }
    private async Task LoadMovie(string path) => await Guard(() => RunProjectOperation(async () =>
    {
        _status.Text = "Loading movie…";
        try { await _engine!.LoadMediaAsync(path); }
        finally { _project = _engine!.Project; _history.Apply("Load movie", _project); _dirty = true; RefreshProjectUi(); }
    }));
    private async Task OpenProject()
    {
        using var dialog = new OpenFileDialog { Title = "Open StageFlow show", Filter = "StageFlow show|*.stageflow" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadProject(dialog.FileName);
    }
    private bool SaveBeforeReplace()
    {
        if (!_dirty) return true;
        var choice = MessageBox.Show(this, "Save the current show before replacing it?", "StageFlow", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return choice == DialogResult.No || (choice == DialogResult.Yes && SaveProject());
    }
    private async Task LoadProject(string path)
    {
        await DrainLiveEdits();
        if (!SaveBeforeReplace()) return;
        await Guard(() => RunProjectOperation(async () =>
        {
            var project = ProjectStore.Load(path);
            using var original = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            bool legacy = original.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() < ShowProject.CurrentSchema;
            await _engine!.StopStreamAsync(); await _engine.SetMidiArmedAsync(false); _midiArmed = false; _arm.Text = "MIDI outputs: off";
            if (project.GpuAdapter != _project.GpuAdapter) await RestartEngine(project);
            await _engine.ClearMediaAsync(); await _engine.SetProjectAsync(project);
            _project = project; _projectPath = legacy ? null : path; _history = new(project); _dirty = legacy; _selected = null; ConfigureOsc(project.Osc);
            try
            {
                if (project.MediaPath is { } media && File.Exists(media)) await _engine.LoadMediaAsync(media);
                else if (project.MediaPath != null) _status.Text = "Movie missing. Use Open movie to relink it.";
                if (project.LoopEnabled && project.LoopOutTicks <= _engine.Transport.Snapshot().Duration.Ticks)
                    _engine.Transport.SetLoop(true, TimeSpan.FromTicks(project.LoopInTicks), TimeSpan.FromTicks(project.LoopOutTicks));
            }
            finally { RefreshProjectUi(); RememberProject(path); }
        }));
    }
    private async Task ImportHnode()
    {
        using var dialog = new OpenFileDialog { Title = "Import Hnode configuration", Filter = "Hnode show configuration|*.shwcfg" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await ImportHnodeFile(dialog.FileName);
    }
    private async Task ImportHnodeFile(string path)
    {
        await Guard(async () =>
        {
            await DrainLiveEdits();
            var imported = HnodeImporter.Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            var project = imported.Project with { MediaPath = _project.MediaPath, Clock = _project.Clock, Audio = _project.Audio,
                Stream = _project.Stream, Spout = _project.Spout, LanVideo = _project.LanVideo, NetworkOutputs = _project.NetworkOutputs,
                Osc = _project.Osc, GpuAdapter = _project.GpuAdapter, LoopEnabled = _project.LoopEnabled, LoopInTicks = _project.LoopInTicks, LoopOutTicks = _project.LoopOutTicks,
                Markers = _project.Markers.ToList(), Layers = [.. imported.Project.Layers, .. _project.Layers.Where(l => l.Kind is LayerKind.Timecode or LayerKind.Spout or LayerKind.Network)] };
            await Edit("Import Hnode layout", project); _status.Text = string.Join("  ", imported.Notices);
        });
    }
    private async Task OpenPath(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".stageflow": await LoadProject(path); break;
            case ".shwcfg": await ImportHnodeFile(path); break;
            default: await LoadMovie(path); break;
        }
    }
    private bool SaveProject(bool saveAs = false)
    {
        try
        {
            string? path = saveAs ? null : _projectPath;
            if (path == null)
            {
                using var dialog = new SaveFileDialog { Filter = "StageFlow show|*.stageflow", FileName = _project.Name + ".stageflow", AddExtension = true, DefaultExt = "stageflow" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return false; path = dialog.FileName;
            }
            SaveTo(path); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    internal void SaveTo(string path)
    { ProjectStore.Save(WorkingProject, path); _projectPath = path; _dirty = false; RememberProject(path); ClearRecovery(); RefreshProjectUi(); }
    private string RecoveryPath => Path.Combine(_stateDirectory, "autosave.stageflow");
    private string RecentPath => Path.Combine(_stateDirectory, "recent.json");
    private List<string> RecentProjects()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentPath))?.Where(File.Exists).Take(10).ToList() ?? []; }
        catch { return []; }
    }
    private void RememberProject(string path)
    {
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            File.WriteAllText(RecentPath, System.Text.Json.JsonSerializer.Serialize(new[] { Path.GetFullPath(path) }.Concat(RecentProjects()).Distinct(StringComparer.OrdinalIgnoreCase).Take(10)));
        }
        catch (IOException) { }
    }
    private void ClearRecovery()
    { try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); } catch (IOException) { } }
    private async Task OfferRecovery()
    {
        if (!File.Exists(RecoveryPath)) return;
        if (MessageBox.Show(this, "An unsaved show was recovered from a previous session. Restore it?", "Recover show", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        { ClearRecovery(); return; }
        await LoadProject(RecoveryPath); _projectPath = null; _dirty = true; RefreshProjectUi();
    }
    private async Task NewShow()
    {
        await DrainLiveEdits();
        if (!SaveBeforeReplace()) return;
        await Guard(() => RunProjectOperation(async () =>
        {
            await _engine!.StopStreamAsync(); await _engine.SetMidiArmedAsync(false); _midiArmed = false; _arm.Text = "MIDI outputs: off";
            var project = new ShowProject { GpuAdapter = _project.GpuAdapter, Layers = [new() { Name = "Movie", Kind = LayerKind.Video }, new() { Name = "Master timecode", Kind = LayerKind.Timecode, X = 60, Y = 940, Width = 600, Height = 90 }] };
            await _engine.ClearMediaAsync(); await _engine.SetProjectAsync(project);
            _project = project; _projectPath = null; _history = new(project); _dirty = false; _selected = null; ConfigureOsc(project.Osc); ClearRecovery(); RefreshProjectUi();
        }));
    }
    internal ContextMenuStrip ShowFileMenu(Control anchor)
    {
        _fileMenu ??= CreateFileMenu();
        _fileMenu.Show(anchor, new Point(0, anchor.Height));
        return _fileMenu;
    }
    private ContextMenuStrip CreateFileMenu()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text };
        menu.Items.Add("New show   Ctrl+N", null, async (_, _) => await NewShow());
        menu.Items.Add("Open show…   Ctrl+Shift+O", null, async (_, _) => await OpenProject());
        menu.Items.Add("Open movie…   Ctrl+O", null, async (_, _) => await OpenMovie());
        menu.Items.Add("Import Hnode layout…", null, async (_, _) => await ImportHnode());
        menu.Items.Add("Save show   Ctrl+S", null, (_, _) => SaveProject());
        menu.Items.Add("Save as…   Ctrl+Shift+S", null, (_, _) => SaveProject(true));
        menu.Items.Add("Rename show…", null, async (_, _) =>
        { string? name = Prompt("Show name", _project.Name); if (!string.IsNullOrWhiteSpace(name)) await Edit("Rename show", _project with { Name = name }); });
        var recent = new ToolStripMenuItem("Recent shows");
        menu.Items.Add(recent);
        menu.Opening += (_, _) =>
        {
            // Refresh only when reopening: no active dropdown is being torn down here.
            foreach (var item in recent.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
            recent.DropDownItems.Clear();
            foreach (string path in RecentProjects()) recent.DropDownItems.Add(Path.GetFileNameWithoutExtension(path), null, async (_, _) => await LoadProject(path));
            recent.Enabled = recent.DropDownItems.Count > 0;
        };
        menu.Items.Add("Open project folder", null, (_, _) =>
        { if (_projectPath != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(_projectPath)!) { UseShellExecute = true }); });
        // Closed fires inside WinForms' modal-menu filter, which still needs the HWND.
        // Disposing there crashes when clicking outside the menu. Match the form's lifetime.
        Disposed += (_, _) => menu.Dispose();
        return menu;
    }
}
