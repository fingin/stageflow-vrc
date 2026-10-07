namespace StageFlow.App;

internal static class Program
{
    internal static readonly string DisplayVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion.Split('+')[0] ?? "development";
    [STAThread]
    private static void Main(string[] args)
    {
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
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "StageFlow", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainWindow(args));
    }
}
