using System.Text.Json;
using StageFlow.App;
using StageFlow.Core.Osc;
using System.Net;
using System.Net.Sockets;

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
                    var grid = Descendants(prefs).OfType<PropertyGrid>().Single(g => g.Name == "PreferenceProperties");
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
                window.ShowNetworkOutputs(); await Task.Delay(20);
                var destinations = window.OwnedForms.Single(f => f.Text.EndsWith("Network destinations"));
                Descendants(destinations).OfType<Button>().Single(b => b.Text == "+ Rtmp").PerformClick();
                var destinationId = window.WorkingProject.NetworkOutputs.Single().Id;
                var destination = window.BuildNetworkProperties(destinationId);
                destination.GetProperties()["Url"]!.SetValue(destination, "rtmp://127.0.0.1/live");
                destination.GetProperties()["Username"]!.SetValue(destination, "fixture-user");
                destination.GetProperties()["Password"]!.SetValue(destination, "fixture-password");
                destination.GetProperties()["StreamKey"]!.SetValue(destination, "fixture-key");
                destination.GetProperties()["AuthenticationEnabled"]!.SetValue(destination, true);
                foreach (string name in new[] { "Password", "StreamKey" })
                    if (destination.GetProperties()[name]!.Attributes[typeof(System.ComponentModel.PasswordPropertyTextAttribute)] is not System.ComponentModel.PasswordPropertyTextAttribute { Password: true })
                        throw new Exception(name + " editor is not masked.");
                await window.DrainLiveEdits();
                var networkGrid = Descendants(destinations).OfType<PropertyGrid>().Single(); networkGrid.Refresh(); Capture(destinations, "RTMP-destination.png");
                window.SaveTo(Path.Combine(state, "network.stageflow"));
                var savedOutput = StageFlow.Core.Projects.ProjectStore.Load(Path.Combine(state, "network.stageflow")).NetworkOutputs.Single();
                if (savedOutput.Password != "fixture-password" || savedOutput.StreamKey != "fixture-key" || !savedOutput.AuthenticationEnabled) throw new Exception("RTMP fields did not persist.");
                await window.Undo();
                if (window.WorkingProject.NetworkOutputs.Count != 0 || networkGrid.SelectedObject != null) throw new Exception("Undo left a stale destination editor.");
                await window.Redo();
                var restoredDestination = window.WorkingProject.NetworkOutputs.Single();
                if (restoredDestination.StreamKey != "fixture-key") throw new Exception("Redo lost RTMP credentials.");
                window.ShowPreferences("Encoding");
                var encoding = window.BuildPreferenceProperties("Encoding");
                encoding.GetProperties()["KeyframeSeconds"]!.SetValue(encoding, 2d);
                encoding.GetProperties()["Preset"]!.SetValue(encoding, StageFlow.Core.Projects.EncodingPreset.Balanced);
                await window.DrainLiveEdits();
                if (window.WorkingProject.Stream.KeyframeSeconds != 2 || window.WorkingProject.Stream.Preset != StageFlow.Core.Projects.EncodingPreset.Balanced) throw new Exception("Encoding controls did not commit.");
                Capture(window.OwnedForms.Single(f => f.Text.EndsWith("Preferences")), "Encoding-updated.png");
                var destinationList = Descendants(destinations).OfType<ListBox>().Single(); destinationList.SelectedIndex = 0;
                Descendants(destinations).OfType<Button>().Single(b => b.Text == "Remove").PerformClick(); await window.DrainLiveEdits();
                Descendants(destinations).OfType<Button>().Single(b => b.Text == "+ Srt").PerformClick();
                if (((LiveProperties)networkGrid.SelectedObject!).GetProperties()["Password"] != null) throw new Exception("SRT exposes RTMP-only credentials.");
                Descendants(destinations).OfType<Button>().Single(b => b.Text == "Remove").PerformClick(); await window.DrainLiveEdits(); destinations.Close();
                Console.WriteLine("PASS: RTMP key/auth fields, masking, live encoding, save, undo/redo, destination removal and SRT field isolation.");
                window.ShowPreferences("OSC mappings"); await Task.Delay(20);
                var oscPrefs = window.OwnedForms.Single(f => f.Text.EndsWith("Preferences"));
                var oscList = Descendants(oscPrefs).OfType<DataGridView>().Single(g => g.Name == "OscMappingList");
                var oscEditor = Descendants(oscPrefs).OfType<PropertyGrid>().Single(g => g.Name == "OscMappingProperties");
                if (!oscList.Visible || Descendants(window).OfType<DataGridView>().Any(g => g.Name == "OscMappingList")) throw new Exception("OSC did not move into Preferences.");
                Descendants(oscPrefs).OfType<Button>().Single(b => b.Text == "Add mapping").PerformClick();
                if (window.WorkingProject.Osc.Mappings.Single().Address != "/transport/play") throw new Exception("New OSC defaults are incorrect.");
                oscList.Rows[0].Cells["Enabled"].Value = false;
                if (window.WorkingProject.Osc.Mappings[0].Enabled) throw new Exception("Inline mapping enable toggle failed.");
                oscList.Rows[0].Cells["Enabled"].Value = true;
                var mapping = (LiveProperties)oscEditor.SelectedObject!;
                mapping.GetProperties()["Action"]!.SetValue(mapping, ControlAction.LocalVolume);
                if (window.WorkingProject.Osc.Mappings.Single().Trigger != OscTrigger.Value) throw new Exception("Fader defaults are incorrect.");
                mapping.GetProperties()["Address"]!.SetValue(mapping, "/project/record");
                mapping.GetProperties()["Action"]!.SetValue(mapping, ControlAction.StreamVolume);
                if (window.WorkingProject.Osc.Mappings.Single().Address != "/project/record") throw new Exception("Action changes overwrite custom addresses.");
                mapping.GetProperties()["Action"]!.SetValue(mapping, ControlAction.LocalVolume);
                await window.DrainLiveEdits();
                Descendants(oscPrefs).OfType<Button>().Single(b => b.Text == "Add mapping").PerformClick(); await window.DrainLiveEdits();
                Descendants(oscPrefs).OfType<Button>().Single(b => b.Text == "Remove").PerformClick(); await window.DrainLiveEdits();
                if (window.WorkingProject.Osc.Mappings.Count != 1) throw new Exception("Remove OSC mapping failed.");
                await window.Undo(); if (window.WorkingProject.Osc.Mappings.Count != 2) throw new Exception("OSC remove undo failed.");
                await window.Redo(); if (window.WorkingProject.Osc.Mappings.Count != 1) throw new Exception("OSC remove redo failed.");
                using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                {
                    var osc = window.BuildPreferenceProperties("OSC");
                    osc.GetProperties()["FeedbackPort"]!.SetValue(osc, ((IPEndPoint)receiver.Client.LocalEndPoint!).Port);
                    osc.GetProperties()["FeedbackEnabled"]!.SetValue(osc, true);
                    await window.DrainLiveEdits();
                    async Task Expect(float expected)
                    {
                        using var timeout = new CancellationTokenSource(3000);
                        while (true)
                        {
                            var packet = await receiver.ReceiveAsync(timeout.Token);
                            if (OscCodec.TryDecode(packet.Buffer, out var values) && values.Any(v => v.Address == "/project/record" && v.Arguments[0] is float f && Math.Abs(f - expected) < .0001)) return;
                        }
                    }
                    await Expect(.4f);
                    audio.GetProperties()["LocalVolume"]!.SetValue(audio, .7f); await window.DrainLiveEdits(); await Expect(.7f);
                    window.SendOscFeedback(true); await Expect(.7f);
                    osc.GetProperties()["FeedbackEnabled"]!.SetValue(osc, false); await window.DrainLiveEdits();
                }
                Capture(oscPrefs, "OSC-mappings.png");
                window.ShowPreferences("OSC"); await Task.Delay(20); Capture(oscPrefs, "OSC-feedback.png");
                oscPrefs.Close(); window.ShowPreferences("OSC mappings"); await Task.Delay(20);
                if (Descendants(window.OwnedForms.Single(f => f.Text.EndsWith("Preferences"))).OfType<DataGridView>().Single().Rows.Count != 1) throw new Exception("Reopening preferences lost OSC mappings.");
                window.OwnedForms.Single(f => f.Text.EndsWith("Preferences")).Close();
                Console.WriteLine("PASS: OSC Preferences add/remove, action defaults, custom addresses, undo/redo, reopen and actual local-edit UDP feedback.");
                var previewHost = Descendants(window).OfType<AspectPreviewHost>().Single();
                foreach (var size in new[] { new Size(1100,720), new Size(1920,1080), new Size(1200,900), new Size(1500,780) })
                {
                    window.Size = size; window.PerformLayout(); await Task.Delay(5);
                    var preview = previewHost.Controls[0]; var fitted = previewHost.ProgramBounds;
                    if (preview.Bounds != fitted || !previewHost.ClientRectangle.Contains(preview.Bounds) || preview.Width < 100)
                        throw new Exception($"Preview layout failed at {size}: {preview.Bounds}, host {previewHost.ClientSize}");
                    if (Math.Abs(preview.Width - preview.Height * (1920.0/1080)) > 2) throw new Exception("Preview stretched during resize.");
                }
                if (previewHost.BackColor == Color.Black || previewHost.Controls[0].BackColor != Color.Black) throw new Exception("Canvas bounds blend into the workspace.");
                foreach (var canvas in new[] { new Size(1080,1920), new Size(1000,1000), new Size(2560,1440) })
                {
                    previewHost.CanvasSize = canvas; await Task.Delay(10);
                    var bounds = previewHost.Controls[0].Bounds;
                    if (Math.Abs(bounds.Width - bounds.Height * (canvas.Width/(double)canvas.Height)) > 2 || bounds.Left < 10 || bounds.Top < 10) throw new Exception("Output aspect or canvas frame changed on resize.");
                }
                previewHost.CanvasSize = new Size(1920,1080);
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
                        foreach (int tab in Enumerable.Range(0, tabs.TabPages.Count))
                        {
                            tabs.SelectedIndex = tab; await Task.Delay(20);
                            foreach (var button in Descendants(window).OfType<Button>().Where(b => b.Visible && b.Parent is FlowLayoutPanel))
                                if (!button.Parent!.ClientRectangle.Contains(button.Bounds)) throw new Exception($"Clipped control at {size}, {font}pt, tab {tab}: {button.Text}, bounds {button.Bounds}, parent {button.Parent.ClientRectangle}");
                        }
                        tabs.SelectedIndex = 0;
                        if (layerList.Height < 60) throw new Exception("Small window collapsed the layer list.");
                    }
                }
                Console.WriteLine("PASS: all 16 preferences sections, descriptor ownership, live skins, aspect-safe resizing, and unclipped controls at 8–13pt.");
            }
            catch (Exception ex) { failure = ex; }
            finally { await window.DrainLiveEdits(); window.SaveTo(Path.Combine(state, "last-check.stageflow")); window.Close(); }
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
