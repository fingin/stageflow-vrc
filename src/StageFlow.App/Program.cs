namespace StageFlow.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "StageFlow", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainWindow(args));
    }
}
