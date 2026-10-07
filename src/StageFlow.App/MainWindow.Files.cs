using StageFlow.Core.Projects;

namespace StageFlow.App;

public sealed partial class MainWindow
{
    private async Task OpenMovie()
    {
        using var dialog = new OpenFileDialog { Title = "Open show movie", Filter = "Video files|*.mp4;*.m4v;*.mov;*.mkv;*.avi|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadMovie(dialog.FileName);
    }
    private async Task LoadMovie(string path) => await Guard(async () =>
    {
        _status.Text = "Loading movie…"; await _engine!.LoadMediaAsync(path);
        _project = _engine.Project; _history.Apply("Load movie", _project); _dirty = true; RefreshProjectUi();
    });
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
        if (!SaveBeforeReplace()) return;
        await Guard(async () =>
        {
            var project = ProjectStore.Load(path);
            await _engine!.StopStreamAsync(); await _engine.SetMidiArmedAsync(false); _midiArmed = false; _arm.Text = "MIDI outputs: off";
            if (project.GpuAdapter != _project.GpuAdapter) await RestartEngine(project);
            await _engine.ClearMediaAsync(); await _engine.SetProjectAsync(project);
            _project = project; _projectPath = path; _history = new(project); _dirty = false; _selected = null;
            if (project.MediaPath is { } media && File.Exists(media)) await _engine.LoadMediaAsync(media);
            else if (project.MediaPath != null) _status.Text = "Movie missing. Use Open movie to relink it.";
            if (project.LoopEnabled && project.LoopOutTicks <= _engine.Transport.Snapshot().Duration.Ticks)
                _engine.Transport.SetLoop(true, TimeSpan.FromTicks(project.LoopInTicks), TimeSpan.FromTicks(project.LoopOutTicks));
            RefreshProjectUi();
            RememberProject(path);
        });
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
            var imported = HnodeImporter.Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            var project = imported.Project with { MediaPath = _project.MediaPath, Clock = _project.Clock, Audio = _project.Audio,
                Markers = _project.Markers.ToList(), Layers = [.. imported.Project.Layers, .. _project.Layers.Where(l => l.Kind == LayerKind.Timecode)] };
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
            ProjectStore.Save(_project, path); _projectPath = path; _dirty = false; RememberProject(path); ClearRecovery(); RefreshProjectUi(); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
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
        if (!SaveBeforeReplace()) return;
        await Guard(async () =>
        {
            await _engine!.StopStreamAsync(); await _engine.SetMidiArmedAsync(false); _midiArmed = false; _arm.Text = "MIDI outputs: off";
            var project = new ShowProject { Layers = [new() { Name = "Movie", Kind = LayerKind.Video }, new() { Name = "Master timecode", Kind = LayerKind.Timecode, X = 60, Y = 940, Width = 600, Height = 90 }] };
            await _engine.ClearMediaAsync(); await _engine.SetProjectAsync(project);
            _project = project; _projectPath = null; _history = new(project); _dirty = false; _selected = null; ClearRecovery(); RefreshProjectUi();
        });
    }
    private void ShowFileMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text };
        menu.Items.Add("New show   Ctrl+N", null, async (_, _) => await NewShow());
        menu.Items.Add("Open show…   Ctrl+Shift+O", null, async (_, _) => await OpenProject());
        menu.Items.Add("Save as…   Ctrl+Shift+S", null, (_, _) => SaveProject(true));
        menu.Items.Add("Rename show…", null, async (_, _) =>
        { string? name = Prompt("Show name", _project.Name); if (!string.IsNullOrWhiteSpace(name)) await Edit("Rename show", _project with { Name = name }); });
        var recent = new ToolStripMenuItem("Recent shows");
        foreach (string path in RecentProjects()) recent.DropDownItems.Add(Path.GetFileNameWithoutExtension(path), null, async (_, _) => await LoadProject(path));
        recent.Enabled = recent.DropDownItems.Count > 0; menu.Items.Add(recent);
        menu.Items.Add("Open project folder", null, (_, _) =>
        { if (_projectPath != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(_projectPath)!) { UseShellExecute = true }); });
        menu.Closed += (_, _) => menu.Dispose(); menu.Show(anchor, new Point(0, anchor.Height));
    }
}
