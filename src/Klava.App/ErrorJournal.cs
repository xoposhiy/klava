using System.Text;

namespace Klava.App;

/// <summary>Writes error reports without using the background log thread.</summary>
internal sealed class ErrorJournal(string folder, string fallbackFolder)
{
    public static string Version { get; } = typeof(ErrorJournal).Assembly.GetName().Version?.ToString() ?? "unknown";
    public string FilePath { get; } = Path.Combine(folder, "errors.log");

    public void Initialize()
    {
        if (!TryAppend(FilePath, string.Empty))
            Record("The main error log is unavailable. Using a separate error report.");
    }

    public bool Record(string report)
    {
        var line = $"{DateTimeOffset.Now:O} Klava {Version} pid={Environment.ProcessId} {report}{Environment.NewLine}";
        if (TryAppend(FilePath, line)) return true;
        // A unique file also works when another writer has errors.log open.
        var name = $"error-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log";
        return TryAppend(Path.Combine(folder, name), line)
            || TryAppend(Path.Combine(fallbackFolder, name), line);
    }

    private static bool TryAppend(string path, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough);
            file.Write(Encoding.UTF8.GetBytes(line));
            file.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
