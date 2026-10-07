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
                var anchor = Descendants(window).OfType<Button>().Single(c => c.Text == "Show ▾");
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
}
