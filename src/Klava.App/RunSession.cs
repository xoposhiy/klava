using System.Text;

namespace Klava.App;

internal sealed class RunSession(string folder, ErrorJournal errors)
{
    private readonly string _path = Path.Combine(folder, "running-session.txt");
    private bool _started;

    public void Start()
    {
        errors.Initialize();
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(_path))
                errors.Record($"Previous session did not exit cleanly. Cause unknown: crash, forced stop, or system shutdown. Previous session: {File.ReadAllText(_path)}");
            using var file = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough);
            file.Write(Encoding.UTF8.GetBytes($"started={DateTimeOffset.Now:O} pid={Environment.ProcessId} version={ErrorJournal.Version}"));
            file.Flush(flushToDisk: true);
            _started = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Record($"Cannot track the running session: {ex}");
        }
    }

    public void Complete()
    {
        if (!_started) return;
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Record($"Cannot clear the running session: {ex}");
        }
    }
}
