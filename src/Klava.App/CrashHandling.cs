namespace Klava.App;

internal static class CrashHandling
{
    public static void Install(Action<string, object?> fatal, Action<string, Exception> handled)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => handled("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => fatal("Unhandled exception", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            handled("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }
}
