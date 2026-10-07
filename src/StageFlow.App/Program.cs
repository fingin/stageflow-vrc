namespace StageFlow.App;

internal static class Program
{
    internal static readonly string DisplayVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion.Split('+')[0] ?? "development";
    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashReporter.Write(
            e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "Unhandled process exception");
        if (args.Length == 3 && args[0] == "--import-hnode")
        {
            try
            {
                var imported = StageFlow.Core.Projects.HnodeImporter.Import(File.ReadAllText(args[1]), Path.GetFileNameWithoutExtension(args[1]));
                StageFlow.Core.Projects.ProjectStore.Save(imported.Project, args[2]);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            return;
        }
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportError(e.Exception, "Unhandled UI exception");
            using var window = new MainWindow(args);
            Application.Run(window);
        }
        catch (Exception ex)
        {
            // Message-filter failures can escape Application.ThreadException.
            ReportError(ex, "Application message loop terminated");
            Environment.ExitCode = 1;
        }
    }

    private static void ReportError(Exception exception, string context)
    {
        string? report = CrashReporter.Write(exception, context);
        string message = exception.Message + (report == null ? "\n\nThe crash report could not be saved." : "\n\nDetails saved to:\n" + report);
        MessageBox.Show(message, "StageFlow error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
