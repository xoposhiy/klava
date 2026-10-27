using System.Collections.Concurrent;

namespace Klava.App;

/// <summary>
/// Append-only file log. Writes happen on a background thread, so the hook thread never waits
/// for the disk. Never log typed text.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly BlockingCollection<string> Queue = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klava");

    public static string FilePath { get; } = Path.Combine(Folder, "klava.log");

    static Log()
    {
        Directory.CreateDirectory(Folder);
        new Thread(Write) { IsBackground = true, Name = "Klava log" }.Start();
    }

    public static void Info(string message) => Add("INFO", message);

    public static void Warn(string message) => Add("WARN", message);

    public static void Error(string message, Exception ex) => Add("ERROR", $"{message}: {ex}");

    /// <summary>Waits a short time for queued lines to reach the file.</summary>
    public static void Flush()
    {
        for (var i = 0; i < 20 && Queue.Count > 0; i++) Thread.Sleep(25);
    }

    private static void Add(string level, string message) =>
        Queue.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}");

    private static void Write()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log is best effort.
            }
        }
    }
}
