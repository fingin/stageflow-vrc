using System.Runtime.InteropServices;
using System.Text;

namespace StageFlow.App;

/// <summary>Best-effort synchronous reporting for failures that may end the process.</summary>
internal static class CrashReporter
{
    internal static string? Write(Exception exception, string context) => Write(exception, context,
        [Path.Combine(AppContext.BaseDirectory, "diagnostics"),
         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StageFlowV2", "diagnostics")]);

    internal static string? Write(Exception exception, string context, IEnumerable<string> folders)
    {
        foreach (string folder in folders)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"Crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
                File.WriteAllText(path, $"StageFlow {Program.DisplayVersion}\nUTC: {DateTime.UtcNow:O}\n" +
                    $"Runtime: {RuntimeInformation.FrameworkDescription}\nOS: {RuntimeInformation.OSDescription}\n" +
                    $"Process: {Environment.ProcessId}\nThread: {Environment.CurrentManagedThreadId}\n" +
                    $"Context: {context}\n\n{exception}\n", Encoding.UTF8);
                return path;
            }
            // Reporting must never replace the original failure, including a read-only install.
            catch (Exception) { }
        }
        return null;
    }
}
