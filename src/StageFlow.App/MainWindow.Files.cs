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
            await _engine.SetProjectAsync(project); await _engine.ClearMediaAsync();
            _project = project; _projectPath = path; _history = new(project); _dirty = false; _selected = null;
            if (project.MediaPath is { } media && File.Exists(media)) await _engine.LoadMediaAsync(media);
            else if (project.MediaPath != null) _status.Text = "Movie missing. Use Open movie to relink it.";
            if (project.LoopEnabled && project.LoopOutTicks <= _engine.Transport.Snapshot().Duration.Ticks)
                _engine.Transport.SetLoop(true, TimeSpan.FromTicks(project.LoopInTicks), TimeSpan.FromTicks(project.LoopOutTicks));
            RefreshProjectUi();
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
    private bool SaveProject()
    {
        try
        {
            string? path = _projectPath;
            if (path == null)
            {
                using var dialog = new SaveFileDialog { Filter = "StageFlow show|*.stageflow", FileName = _project.Name + ".stageflow", AddExtension = true, DefaultExt = "stageflow" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return false; path = dialog.FileName;
            }
            _project = _project with { Name = Path.GetFileNameWithoutExtension(path) };
            ProjectStore.Save(_project, path); _projectPath = path; _dirty = false; RefreshProjectUi(); return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }
}
