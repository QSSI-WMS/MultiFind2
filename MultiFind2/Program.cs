namespace MultiFind2;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Fix #1: a stray exception on a worker or in a UI handler should surface as a message,
        // never take the window down and lose the results already on screen.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { ReportCrash(e.Exception); e.SetObserved(); };

        // Off the UI thread: a store with thousands of files in it would otherwise be a visible
        // pause before the window appears, and nothing on screen depends on the result.
        Task.Run(BackupStore.Purge);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void ReportCrash(Exception? ex)
    {
        if (ex == null) return;

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{ex.Message}\n\n{ex.GetType().Name}",
            "MultiFind2", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
