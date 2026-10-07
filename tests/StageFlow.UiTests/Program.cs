using System.Text.Json;
using StageFlow.App;

namespace StageFlow.UiTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        string state = Path.Combine(Path.GetTempPath(), "StageFlow-UiChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        string recentPath = Path.Combine(state, "fixture.stageflow");
        File.WriteAllText(recentPath, "{}"); // Only used to populate the submenu; never opened.
        File.WriteAllText(Path.Combine(state, "recent.json"), JsonSerializer.Serialize(new[] { recentPath }));
        Exception diagnosticFixture;
        try { throw new InvalidOperationException("Crash logging fixture", new IOException("Inner exception fixture")); }
        catch (Exception ex) { diagnosticFixture = ex; }
        string fallback = Path.Combine(state, "diagnostics");
        string? report = CrashReporter.Write(diagnosticFixture, "UI regression test", [recentPath, fallback]);
        if (report == null || !File.ReadAllText(report).Contains("Inner exception fixture") ||
            !File.ReadAllText(report).Contains("StageFlow.UiTests.Program.Main"))
            throw new Exception("Crash report did not preserve the exception and stack using the fallback folder.");
        if (CrashReporter.Write(diagnosticFixture, "Unwritable destination", [recentPath]) != null)
            throw new Exception("Crash reporting should safely return null when all destinations fail.");
        Console.WriteLine("PASS: crash report preserves inner exception/stack; unwritable destinations fall back safely.");
        using var window = new MainWindow([], startEngine: false, stateDirectory: state);
        Exception? failure = null;
        ContextMenuStrip? lastMenu = null;
        window.Shown += async (_, _) =>
        {
            try
            {
                var anchor = Descendants(window).OfType<Button>().Single(c => c.Text == "Open movie…");
                for (int iteration = 0; iteration < 100; iteration++)
                {
                    lastMenu = window.ShowFileMenu(anchor);
                    await Task.Delay(1);
                    if (iteration % 2 == 1)
                    {
                        var recent = lastMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Recent shows");
                        if (recent.DropDownItems.Count != 1) throw new Exception("Recent projects were not populated.");
                        recent.ShowDropDown();
                        await Task.Delay(1);
                    }
                    // Exercise WinForms' real modal-menu filter, including the exact outside-click
                    // path from the crash report. No system-wide keyboard/mouse injection.
                    var click = Message.Create(window.Handle, 0x0201, (nint)1,
                        (nint)(((window.ClientSize.Height - 20) << 16) | (window.ClientSize.Width - 20)));
                    Application.FilterMessage(ref click);
                    if (lastMenu.Visible) throw new Exception("Outside click did not dismiss the Show menu.");
                    await Task.Delay(1);
                    lastMenu = window.ShowFileMenu(anchor);
                    lastMenu.Close(iteration % 2 == 0 ? ToolStripDropDownCloseReason.Keyboard : ToolStripDropDownCloseReason.ItemClicked);
                    if (lastMenu.Visible) throw new Exception("Explicit dismissal left the Show menu visible.");
                }
                Console.WriteLine("PASS: 100 outside-click dismissals (50 with Recent submenu) and 100 keyboard/item dismissals.");
                foreach (string section in MainWindow.PreferenceSections)
                {
                    window.ShowPreferences(section);
                    await Task.Delay(5);
                    var prefs = window.OwnedForms.Single(f => f.Text.EndsWith("Preferences"));
                    var grid = Descendants(prefs).OfType<PropertyGrid>().Single();
                    if (grid.SelectedObject is not LiveProperties properties || properties.GetProperties().Count == 0)
                        throw new Exception("Empty preferences section: " + section);
                    if (properties.GetPropertyOwner(null) != properties) throw new Exception("PropertyGrid descriptor owner is missing.");
                    foreach (System.ComponentModel.PropertyDescriptor property in properties.GetProperties()) _ = property.GetValue(properties);
                    if (section is "Appearance" or "Encoding") Capture(prefs, section + ".png");
                }
                if (window.SearchPreferences("bitrate").GetProperties().Count < 2) throw new Exception("Global settings search missed encoding fields.");
                if (window.BuildPreferenceProperties("Art-Net").GetProperties()["HoldLastSeconds"] != null) throw new Exception("Unused setting was exposed.");
                var appearance = window.BuildPreferenceProperties("Appearance");
                foreach (var skin in Enum.GetValues<AppSkin>())
                {
                    appearance.GetProperties()["Skin"]!.SetValue(appearance, skin);
                    if (AppPreferences.Load(state).Skin != skin) throw new Exception("Skin did not persist immediately.");
                    if (window.BackColor != Theme.Background) throw new Exception("Skin was not applied to the open workspace.");
                }
                appearance.GetProperties()["Skin"]!.SetValue(appearance, AppSkin.Slate);
                var audio = window.BuildPreferenceProperties("Audio");
                audio.GetProperties()["LocalVolume"]!.SetValue(audio, 0.4f);
                audio.GetProperties()["StreamMute"]!.SetValue(audio, true);
                await window.DrainLiveEdits();
                if (window.WorkingProject.Audio.LocalVolume != 0.4f || !window.WorkingProject.Audio.StreamMute) throw new Exception("Consecutive settings edits lost a value.");
                await window.Undo();
                if (window.WorkingProject.Audio.LocalVolume != 1 || window.WorkingProject.Audio.StreamMute) throw new Exception("Undo did not restore live settings.");
                await window.Redo();
                if (window.WorkingProject.Audio.LocalVolume != 0.4f || !window.WorkingProject.Audio.StreamMute) throw new Exception("Redo lost live settings.");
                bool rejected = false;
                try { audio.GetProperties()["LocalVolume"]!.SetValue(audio, float.NaN); } catch (InvalidDataException) { rejected = true; }
                if (!rejected || window.WorkingProject.Audio.LocalVolume != 0.4f) throw new Exception("Invalid edit mutated the current project.");
                window.SaveTo(Path.Combine(state, "settings.stageflow"));
                if (StageFlow.Core.Projects.ProjectStore.Load(Path.Combine(state, "settings.stageflow")).Audio.LocalVolume != 0.4f) throw new Exception("Live settings were not saved.");
                Console.WriteLine("PASS: consecutive live settings, undo/redo, invalid value rollback, and project persistence.");
                window.OwnedForms.Single(f => f.Text.EndsWith("Preferences")).Close();
                var previewHost = Descendants(window).OfType<AspectPreviewHost>().Single();
                foreach (var size in new[] { new Size(1100,720), new Size(1920,1080), new Size(1200,900), new Size(1500,780) })
                {
                    window.Size = size; window.PerformLayout(); await Task.Delay(5);
                    var preview = previewHost.Controls[0]; var fitted = AspectPreviewHost.Fit(previewHost.ClientSize, new Size(1920,1080));
                    if (preview.Bounds != fitted || !previewHost.ClientRectangle.Contains(preview.Bounds) || preview.Width < 100)
                        throw new Exception($"Preview layout failed at {size}: {preview.Bounds}, host {previewHost.ClientSize}");
                    if (Math.Abs(preview.Width - preview.Height * (1920.0/1080)) > 2) throw new Exception("Preview stretched during resize.");
                }
                if (Descendants(window).OfType<TabControl>().Any()) throw new Exception("Native tab coloring returned.");
                window.Size = new Size(1600, 1000); await Task.Delay(10);
                var layerList = Descendants(window).OfType<ListBox>().Single(l => l.Items.Cast<object>().Any(x => x.ToString() == "Master timecode"));
                layerList.SelectedItem = layerList.Items.Cast<object>().Single(x => x.ToString() == "Master timecode");
                await Task.Delay(10); Capture(window, "Workspace.png");
                foreach (var button in Descendants(window).OfType<Button>().Where(b => b.Visible && b.Parent is FlowLayoutPanel))
                    if (!button.Parent!.ClientRectangle.Contains(button.Bounds)) throw new Exception($"Clipped control: {button.Text}, bounds {button.Bounds}, parent {button.Parent.ClientRectangle}");
                foreach (int font in new[] { 8, 13, 9 })
                {
                    appearance.GetProperties()["Interface text size"]!.SetValue(appearance, font);
                    foreach (var size in new[] { new Size(1100, 720), new Size(1600, 1000) })
                    {
                        window.Size = size; window.PerformLayout(); await Task.Delay(20);
                        Capture(window, $"Workspace-{size.Width}-{font}.png");
                        var tabs = Descendants(window).OfType<WorkspaceTabs>().Single();
                        foreach (int tab in new[] { 0, 1, 2 })
                        {
                            tabs.SelectedIndex = tab; await Task.Delay(20);
                            foreach (var button in Descendants(window).OfType<Button>().Where(b => b.Visible && b.Parent is FlowLayoutPanel))
                                if (!button.Parent!.ClientRectangle.Contains(button.Bounds)) throw new Exception($"Clipped control at {size}, {font}pt, tab {tab}: {button.Text}, bounds {button.Bounds}, parent {button.Parent.ClientRectangle}");
                        }
                        tabs.SelectedIndex = 0;
                        if (layerList.Height < 60) throw new Exception("Small window collapsed the layer list.");
                    }
                }
                Console.WriteLine("PASS: all 15 preferences sections, descriptor ownership, live skins, aspect-safe resizing, and unclipped controls at 8–13pt.");
            }
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); }
        };
        try { Application.Run(window); }
        catch (Exception ex) { failure ??= ex; }
        if (failure != null) { Console.Error.WriteLine(failure); return 1; }
        if (lastMenu == null || !lastMenu.IsDisposed) { Console.Error.WriteLine("FAIL: menu survived owner shutdown."); return 1; }
        Console.WriteLine("PASS: menu released when owner closes.");
        string recovery = Path.Combine(state, "autosave.stageflow");
        File.WriteAllText(recovery, "recovery fixture");
        using (var abnormalWindow = new MainWindow([], startEngine: false, stateDirectory: state))
            abnormalWindow.Dispose(); // Dispose without FormClosing, as on a message-loop failure.
        if (File.ReadAllText(recovery) != "recovery fixture") throw new Exception("Abnormal shutdown erased recovery.");
        Console.WriteLine("PASS: abnormal window disposal preserves recovery.");
        return 0;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
    private static void Capture(Form form, string file)
    {
        string? folder = Environment.GetEnvironmentVariable("STAGEFLOW_UI_ARTIFACTS"); if (folder == null) return;
        Directory.CreateDirectory(folder); using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(folder, file), System.Drawing.Imaging.ImageFormat.Png);
    }
}
