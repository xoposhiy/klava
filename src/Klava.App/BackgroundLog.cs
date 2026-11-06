using System.Collections.Concurrent;

namespace Klava.App;

internal sealed class BackgroundLog : IDisposable
{
    private sealed record Entry(string? Line, bool Error, TaskCompletionSource<bool>? Completion = null);
    private readonly BlockingCollection<Entry> _queue = new();
    private readonly Action<string, bool> _write;
    private readonly Action<Exception> _reportError;
    private readonly Thread _thread;

    public BackgroundLog(Action<string, bool> write, Action<Exception> reportError)
    {
        _write = write;
        _reportError = reportError;
        _thread = new Thread(Run) { IsBackground = true, Name = "Klava log" };
        _thread.Start();
    }

    public void Add(string line, bool error) => _queue.Add(new Entry(line, error));

    public bool Flush(TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new Entry(null, false, completion));
        return completion.Task.Wait(timeout);
    }

    private void Run()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            if (entry.Completion is { } completion)
            {
                completion.TrySetResult(true);
                continue;
            }
            try
            {
                _write(entry.Line!, entry.Error);
            }
            catch (Exception ex)
            {
                // Do not report a log failure through the same worker.
                try { _reportError(ex); }
                catch (Exception) { /* Logging must not terminate the app. */ }
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
