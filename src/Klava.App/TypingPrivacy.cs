using System.Diagnostics;
using System.Windows.Automation;
using Klava.Core;
using static Klava.App.Native;

namespace Klava.App;

/// <summary>Queries UI Automation on a separate thread. Unknown or stale focus is private.</summary>
internal sealed class TypingPrivacy : IDisposable
{
    private sealed record Snapshot(long Window, long Focus, string Application, bool Safe, long TimeMs);
    private Snapshot _snapshot = new(0, 0, "unknown", false, 0);
    private readonly Thread _thread;
    private volatile bool _stopped;

    public TypingPrivacy()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Klava field privacy" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public TypingContext Context
    {
        get
        {
            var window = GetForegroundWindow();
            var thread = GetWindowThreadProcessId(window, out _);
            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            var focus = GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
            var cached = Volatile.Read(ref _snapshot);
            var safe = cached.Safe && cached.Window == (long)window && cached.Focus == (long)focus
                && Clock.NowMs() - cached.TimeMs < 150;
            return new((long)window, (long)focus, cached.Application, (long)GetKeyboardLayout(thread), safe);
        }
    }

    private void Run()
    {
        while (!_stopped)
        {
            var checkedAt = Clock.NowMs();
            var window = GetForegroundWindow();
            var thread = GetWindowThreadProcessId(window, out var pid);
            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            var focus = GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
            var safe = false;
            var app = "unknown";
            try
            {
                using var process = Process.GetProcessById((int)pid);
                app = process.ProcessName;
                var element = AutomationElement.FocusedElement;
                safe = pid != Environment.ProcessId && element is not null && element.Current.ProcessId == pid
                    && element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is false
                    && GetForegroundWindow() == window;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* Unknown fields remain private. */ }
            Volatile.Write(ref _snapshot, new((long)window, (long)focus, app, safe, checkedAt));
            Thread.Sleep(50);
        }
    }

    public void Dispose() { _stopped = true; _thread.Join(TimeSpan.FromSeconds(1)); }
}
