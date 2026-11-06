namespace Klava.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            CrashHandling.Install(Log.Fatal, Log.Error);
            using var mutex = new Mutex(true, @"Local\Klava.SingleInstance", out var firstInstance);
            if (!firstInstance) return 0;
            var session = new RunSession(Log.Folder, Log.Errors);
            session.Start();
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext());
            Log.Flush();
            session.Complete();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal("Application stopped", ex);
            return 1;
        }
    }
}
