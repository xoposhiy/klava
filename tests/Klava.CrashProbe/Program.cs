using System.Reflection;
using System.Runtime.InteropServices;

namespace Klava.CrashProbe;

internal static class Program
{
    private static readonly Assembly App = Assembly.Load("Klava");

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Use: Klava.CrashProbe <log-folder> <crash|clean|wait|ui>");
        Environment.SetEnvironmentVariable("KLAVA_LOG_DIR", Path.GetFullPath(args[0]));
        SetErrorMode(0x0002); // Do not leave a crash dialog waiting for user input.
        var log = App.GetType("Klava.App.Log", throwOnError: true)!;
        var fatal = log.GetMethod("Fatal")!.CreateDelegate<Action<string, object?>>();
        var handled = log.GetMethod("Error")!.CreateDelegate<Action<string, Exception>>();
        App.GetType("Klava.App.CrashHandling", true)!.GetMethod("Install")!.Invoke(null, [fatal, handled]);
        var errors = log.GetProperty("Errors")!.GetValue(null)!;
        var sessionType = App.GetType("Klava.App.RunSession", true)!;
        var session = Activator.CreateInstance(sessionType, [Path.GetFullPath(args[0]), errors])!;
        sessionType.GetMethod("Start")!.Invoke(session, null);

        switch (args[1])
        {
            case "crash":
                new Thread(() => throw new InvalidOperationException("Crash probe background failure")).Start();
                Thread.Sleep(Timeout.Infinite);
                break;
            case "wait":
                File.WriteAllText(Path.Combine(args[0], "ready.txt"), Environment.ProcessId.ToString());
                Thread.Sleep(Timeout.Infinite);
                break;
            case "ui":
                CheckUiRecovery();
                break;
            case "clean":
                break;
            default:
                throw new ArgumentException("Unknown probe mode.");
        }
        log.GetMethod("Flush")!.Invoke(null, null);
        sessionType.GetMethod("Complete")!.Invoke(session, null);
    }

    private static void CheckUiRecovery()
    {
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            if (++ticks == 1) throw new InvalidOperationException("Crash probe UI failure");
            context.ExitThread();
        };
        timer.Start();
        Application.Run(context);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
