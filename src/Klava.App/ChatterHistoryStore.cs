using System.Collections.Concurrent;
using System.Text.Json;
using Klava.Core;

namespace Klava.App;

internal sealed class ChatterHistoryStore : IDisposable
{
    private sealed record SavedHistory(int Version, IEnumerable<ChatterDay> Days);
    private readonly record struct Entry(DateTime Time, int Vk, long GapMs, bool Blocked);
    private readonly BlockingCollection<Entry> _queue = new();
    private readonly Thread _worker;
    private readonly string _path;
    private ChatterDay[] _snapshot = [];
    private string _status = "Loading history...";
    private int _snapshotRequested;

    public ChatterHistoryStore(string path)
    {
        _path = path;
        _worker = new Thread(Run) { IsBackground = true, Name = "Klava chatter history" };
        _worker.Start();
    }

    /// <summary>Last published counts. Reading requests a refresh from the worker.</summary>
    public ChatterDay[] Snapshot
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            Volatile.Write(ref _snapshotRequested, 1);
            return snapshot;
        }
    }
    public string Status => Volatile.Read(ref _status);

    // Called by the hook: only enqueue, never wait for disk or the UI.
    public void Record(int vk, long gapMs, bool blocked) =>
        _queue.Add(new Entry(DateTime.Now, vk, gapMs, blocked));

    public void RecordPress(int vk) => _queue.Add(new Entry(DateTime.Now, vk, -1, false));

    private void Run()
    {
        ChatterHistory history;
        var canSave = true;
        var dirty = false;
        try
        {
            if (File.Exists(_path))
            {
                var saved = JsonSerializer.Deserialize<SavedHistory>(File.ReadAllText(_path));
                if (saved is not { Version: 3, Days: not null }) throw new ArgumentException("Unsupported history format.");
                history = new ChatterHistory(saved.Days);
            }
            else
            {
                history = new ChatterHistory();
                dirty = true;
            }
            Volatile.Write(ref _status, "History is saved automatically.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Preserve an unreadable file rather than replacing history with an empty file.
            history = new ChatterHistory();
            canSave = false;
            Volatile.Write(ref _status, "Cannot load history. The existing file is kept; new counts are only in memory.");
            Log.Error("Cannot load chatter history", ex);
        }

        dirty |= history.Compact(DateTime.Now.AddHours(-2));
        Volatile.Write(ref _snapshot, history.Snapshot());
        var snapshotDirty = false;
        var nextSnapshot = Environment.TickCount64 + 1000;
        var nextCompact = Environment.TickCount64 + 60_000;
        var nextSave = Environment.TickCount64;
        while (!_queue.IsCompleted)
        {
            if (_queue.TryTake(out var entry, 1000))
            {
                Apply(history, entry);
                for (var count = 1; count < 4096 && _queue.TryTake(out entry); count++) Apply(history, entry);
                dirty = true;
                snapshotDirty = true;
            }
            var now = Environment.TickCount64;
            if (now >= nextCompact)
            {
                if (history.Compact(DateTime.Now.AddHours(-2))) dirty = snapshotDirty = true;
                nextCompact = now + 60_000;
            }
            if (snapshotDirty && now >= nextSnapshot && Interlocked.Exchange(ref _snapshotRequested, 0) != 0)
            {
                Volatile.Write(ref _snapshot, history.Snapshot());
                snapshotDirty = false;
                nextSnapshot = now + 1000;
            }
            if (!dirty || !canSave || now < nextSave) continue;
            dirty = !Save(history);
            nextSave = Environment.TickCount64 + 5000;
        }
        if (dirty && canSave) Save(history);
    }

    private static void Apply(ChatterHistory history, Entry entry)
    {
        if (entry.GapMs < 0) history.RecordPress(entry.Time, entry.Vk);
        else history.Record(entry.Time, entry.Vk, entry.GapMs, entry.Blocked);
    }

    private bool Save(ChatterHistory history)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            using (var file = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new SavedHistory(3, history.Records));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
            Volatile.Write(ref _status, "History is saved automatically.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            const string message = "Cannot save history. Klava will retry; new counts are kept in memory.";
            if (Status != message) Log.Error("Cannot save chatter history", ex);
            Volatile.Write(ref _status, message);
            return false;
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_worker.Join(TimeSpan.FromSeconds(5))) Log.Warn("Chatter history did not finish saving before exit.");
    }
}
