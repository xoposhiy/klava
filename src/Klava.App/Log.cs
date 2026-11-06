namespace Klava.App;

/// <summary>
/// Hook callbacks only enqueue messages. Fatal errors use an independent disk write.
/// </summary>
internal static class Log
{
    public static string Folder { get; } = Environment.GetEnvironmentVariable("KLAVA_LOG_DIR") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klava");

    public static string FilePath { get; } = Path.Combine(Folder, "klava.log");

    public static ErrorJournal Errors { get; } = new(Folder, Path.Combine(Path.GetTempPath(), "Klava"));
    private static readonly BackgroundLog Writer = new(Write,
        ex => Errors.Record($"Log worker failed: {ex}"));

    public static void Info(string message) => Add("INFO", message);

    public static void Warn(string message) => Add("WARN", message);

    public static void Error(string message, Exception ex) => Add("ERROR", $"{message}: {ex}");

    public static void Fatal(string message, object? error)
    {
        var line = Format("FATAL", $"{message}: {error}");
        // The log worker can be the thread that failed; never wait for it here.
        Errors.Record(line);
        Writer.Add(line, error: false);
    }

    public static bool Flush() => Writer.Flush(TimeSpan.FromSeconds(2));

    private static void Add(string level, string message) =>
        Writer.Add(Format(level, message), level == "ERROR");

    private static string Format(string level, string message) =>
        $"{DateTimeOffset.Now:O} {level} pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} {message}";

    private static void Write(string line, bool error)
    {
        if (error) Errors.Record(line);
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                File.Move(FilePath, FilePath + ".old", overwrite: true);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve errors even when the event log cannot be written.
        }
    }
}
