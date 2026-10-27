namespace Klava.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Klava.SingleInstance", out var firstInstance);
        if (!firstInstance) return;

        Application.ThreadException += (_, e) => Log.Error("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled", (Exception)e.ExceptionObject);
            Log.Flush();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
