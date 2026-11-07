using System.Collections.Concurrent;
using System.Text.Json;
using Klava.Core;

namespace Klava.App;

internal sealed record SavedTypingEpisode(int Version, DateTimeOffset CreatedUtc, TypingEpisode Episode, DateTimeOffset TimeBaseUtc);
internal sealed record TypingRun(string EpisodeId, string Heuristic, string Version);
internal sealed class TypingArchive
{
    public int Version { get; set; } = 1;
    public List<TypingRun> Runs { get; set; } = [];
    public List<TypingSignal> Signals { get; set; } = [];
    public Dictionary<string, long> Usage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ApprovedWords { get; set; } = [];
    public List<string> ExpiryNotices { get; set; } = [];
    public List<string> ShownExpiryNotices { get; set; } = [];
}

internal sealed record TypingCandidate(string Word, long Usage, int Errors, string Signals, bool Approved);
internal sealed record TypingSnapshot(string Status, int Episodes, int PendingExpiry, IReadOnlyList<TypingCandidate> Candidates,
    IReadOnlyList<string> ApprovedWords, IReadOnlyList<string> Patterns, bool IsAnalyzing = false);

/// <summary>The hook only queues events; this worker owns episode files and analysis state.</summary>
internal sealed class TypingEpisodeStore : IDisposable
{
    private readonly string _folder;
    private readonly BlockingCollection<object> _queue = new(8192);
    private readonly Thread _worker;
    private readonly TypingEpisodeCollector _collector = new(Guid.NewGuid().ToString("N"));
    private TypingArchive _archive = new();
    private TypingSnapshot _snapshot = new("Loading typing episodes...", 0, 0, [], [], []);
    private bool _writable = true;
    private volatile bool _stopped;
    private string _status = "Ready. Analysis runs only when requested.";
    private string _usageWord = "";
    private TypingContext? _context;
    private long _lastEvent;
    private long _lastMaintenance;
    private long _dropped;
    private readonly Queue<TypingEpisode> _pendingEpisodes = new();
    private bool _isAnalyzing;
    private readonly CancellationTokenSource _analysisCancellation = new();
    private sealed record Analyze;
    private sealed record Approve(string Word);
    private sealed record Reset;
    private sealed record NoticeShown;
    private sealed record AnalysisResult(List<TypingRun> Runs, List<TypingSignal> Signals, int Failures);

    public TypingEpisodeStore(string folder)
    {
        _folder = folder;
        _collector.Completed += SaveEpisode;
        _worker = new Thread(RunSafely) { IsBackground = true, Name = "Klava typing episodes" };
        _worker.Start();
    }

    public TypingSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Record(TypingEvent e) => Enqueue(e);
    public void AnalyzeNow() => Enqueue(new Analyze());
    public void ApproveWord(string word) => Enqueue(new Approve(word));
    public void ResetContext() => Enqueue(new Reset());
    public void ExpiryNoticeShown() => Enqueue(new NoticeShown());

    private void Enqueue(object value)
    {
        if (_stopped) return;
        try { if (!_queue.TryAdd(value)) Interlocked.Increment(ref _dropped); }
        catch (InvalidOperationException) { }
    }

    private void RunSafely()
    {
        try { Run(); }
        catch (Exception ex)
        {
            _stopped = true;
            Volatile.Write(ref _snapshot, Snapshot with { Status = "Typing collection stopped after a worker error. Saved files are unchanged.", IsAnalyzing = false });
            // Exception messages can contain file contents. Keep typed text out of diagnostic logs.
            Log.Warn($"Typing worker stopped ({ex.GetType().Name}).");
        }
    }

    private void Run()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "analysis.json");
        if (File.Exists(path))
        {
            try
            {
                _archive = JsonSerializer.Deserialize<TypingArchive>(File.ReadAllText(path))!;
                if (_archive is not { Version: 1, Runs: not null, Signals: not null, Usage: not null,
                    ApprovedWords: not null, ExpiryNotices: not null, ShownExpiryNotices: not null }) throw new InvalidDataException();
                if (_archive.Runs.Any(r => r is not { EpisodeId: not null, Heuristic: not null, Version: not null })
                    || _archive.Signals.Any(s => s is not { EpisodeId: not null, Heuristic: not null,
                        Version: not null, Candidate: not null, Original: not null, Description: not null })
                    || _archive.ApprovedWords.Any(w => string.IsNullOrEmpty(w) || TypingAnalysis.EnglishCandidate(w) != w))
                    throw new InvalidDataException();
            }
            catch
            {
                _archive = new();
                _writable = false;
                _status = "Cannot read analysis.json. Existing files are preserved; collection is in memory only.";
            }
        }
        Maintenance();
        while (!_queue.IsCompleted)
        {
            if (_queue.TryTake(out var item, 100))
            {
                if (Interlocked.Exchange(ref _dropped, 0) > 0)
                {
                    _collector.Reset("queue-overflow");
                    _usageWord = "";
                    _status = "Some input was skipped because the queue was full.";
                }
                switch (item)
                {
                    case TypingEvent e:
                        Observe(e);
                        break;
                    case Analyze:
                        _collector.Reset("manual-analysis");
                        StartAnalysis();
                        break;
                    case AnalysisResult result:
                        _isAnalyzing = false;
                        _archive.Runs.AddRange(result.Runs);
                        _archive.Signals.AddRange(result.Signals);
                        _status = result.Failures == 0 ? "Analysis complete. Select a candidate to approve it."
                            : $"Analysis complete; {result.Failures} unreadable episodes were skipped.";
                        SaveArchive();
                        Publish();
                        break;
                    case Approve approval:
                        var word = TypingAnalysis.EnglishCandidate(approval.Word);
                        if (word.Length == 0 || _archive.ApprovedWords.Count >= 10000)
                        {
                            _status = "Use English words of 1..100 letters. The dictionary holds at most 10,000 words.";
                            Publish();
                            break;
                        }
                        if (!_archive.ApprovedWords.Contains(word))
                        {
                            _archive.ApprovedWords.Add(word);
                            SaveArchive();
                            Publish();
                        }
                        break;
                    case Reset:
                        _collector.Reset("configuration-change");
                        _usageWord = "";
                        break;
                    case NoticeShown:
                        _archive.ShownExpiryNotices = _archive.ExpiryNotices.ToList();
                        SaveArchive();
                        break;
                }
            }
            _collector.Advance(Clock.NowMs());
            if (Clock.NowMs() - _lastMaintenance > 5000) Maintenance();
        }
        _collector.Reset("shutdown");
        SavePendingEpisodes();
        SaveArchive();
    }

    private void Observe(TypingEvent e)
    {
        _collector.Observe(e);
        if (!e.Context.CanRecord || _context?.Window != e.Context.Window || _context?.Focus != e.Context.Focus
            || e.TimeMs - _lastEvent >= TypingEpisodeCollector.IdleMs) _usageWord = "";
        _context = e.Context;
        _lastEvent = e.TimeMs;
        if (e.Kind == "mouse") { _usageWord = ""; return; }
        if (e.Kind == "layout") { CountWordUse(); return; }
        if (!e.Context.CanRecord || e.Kind != "key" || e.IsUp || e.Blocked || e.Source != "physical") return;
        if (e.Vk == Vk.Backspace) { if (_usageWord.Length > 0) _usageWord = _usageWord[..^1]; return; }
        if (e.Text.Length == 0 && e.Vk is not (Vk.Enter or Vk.Tab))
        {
            if (!Vk.IsModifier(e.Vk) && e.Vk != Vk.CapsLock) _usageWord = "";
            return;
        }
        foreach (var c in e.Vk is Vk.Enter or Vk.Tab ? " " : e.Text)
        {
            if (char.IsLetter(c)) { if (_usageWord.Length < 101) _usageWord += c; }
            else
            {
                CountWordUse();
            }
        }
    }

    private void CountWordUse()
    {
        var candidate = TypingAnalysis.Candidate(_usageWord);
        if (candidate.Length > 0) _archive.Usage[candidate] = _archive.Usage.GetValueOrDefault(candidate) + 1;
        _usageWord = "";
    }

    private void SaveEpisode(TypingEpisode episode)
    {
        if (_pendingEpisodes.Count == 32)
        {
            _pendingEpisodes.Dequeue();
            _status = "Some episodes were lost because storage was unavailable and the memory buffer was full.";
        }
        _pendingEpisodes.Enqueue(episode);
        SavePendingEpisodes();
    }

    private void SavePendingEpisodes()
    {
        if (!_writable) return;
        while (_pendingEpisodes.TryPeek(out var episode))
        {
            var created = DateTimeOffset.UtcNow;
            var path = Path.Combine(_folder, $"episode-{created:yyyyMMddHHmmssfff}-{episode.Id}.json");
            try
            {
                WriteJson(path, new SavedTypingEpisode(1, created, episode, created.AddMilliseconds(-Clock.NowMs())));
                _pendingEpisodes.Dequeue();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _status = "Cannot save typing episodes. Episodes remain in memory; saving will be retried.";
                return;
            }
        }
    }

    private IEnumerable<string> EpisodePaths() => Directory.EnumerateFiles(_folder, "episode-*.json");

    private void StartAnalysis()
    {
        if (_isAnalyzing) return;
        var paths = EpisodePaths().ToArray();
        var analyzed = _archive.Runs.ToHashSet();
        _status = "Analyzing saved episodes... Collection remains active.";
        _isAnalyzing = true;
        Publish();
        _ = Task.Run(() => AnalyzeEpisodes(paths, analyzed));
    }

    private void AnalyzeEpisodes(string[] paths, HashSet<TypingRun> analyzed)
    {
        var result = new AnalysisResult([], [], 0);
        var failures = 0;
        foreach (var path in paths)
        {
            if (_analysisCancellation.IsCancellationRequested) return;
            try
            {
                var saved = JsonSerializer.Deserialize<SavedTypingEpisode>(File.ReadAllText(path));
                if (saved is not { Version: 1, Episode.Events: not null } || saved.Episode.Events.Any(e => e is null || e.Context is null))
                { failures++; continue; }
                foreach (var heuristic in TypingAnalysis.Heuristics)
                {
                    var run = new TypingRun(saved.Episode.Id, heuristic.Id, heuristic.Version);
                    if (analyzed.Contains(run)) continue;
                    result.Signals.AddRange(heuristic.Analyze(saved.Episode));
                    result.Runs.Add(run);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failures++; }
        }
        try { _queue.Add(result with { Failures = failures }, _analysisCancellation.Token); }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
    }

    private void Maintenance()
    {
        _lastMaintenance = Clock.NowMs();
        if (_writable)
        {
            SavePendingEpisodes();
            var now = DateTime.UtcNow;
            foreach (var temporary in Directory.EnumerateFiles(_folder, "episode-*.json.tmp"))
                if (now - File.GetLastWriteTimeUtc(temporary) >= TimeSpan.FromDays(7)) File.Delete(temporary);
            foreach (var path in EpisodePaths())
            {
                var age = now - File.GetLastWriteTimeUtc(path);
                var name = Path.GetFileName(path);
                // A notice is published a day before expiry. No analysis is started here.
                var analyzed = IsAnalyzed(path);
                if (!analyzed && age >= TimeSpan.FromDays(6) && !_archive.ExpiryNotices.Contains(name)) _archive.ExpiryNotices.Add(name);
                if (age >= TimeSpan.FromDays(7) && (analyzed || _archive.ShownExpiryNotices.Contains(name))) File.Delete(path);
            }
            SaveArchive();
        }
        Publish();
    }

    private bool IsAnalyzed(string path)
    {
        var id = Path.GetFileNameWithoutExtension(path).Split('-').Last();
        return TypingAnalysis.Heuristics.All(h => _archive.Runs.Contains(new(id, h.Id, h.Version)));
    }

    private void SaveArchive()
    {
        if (!_writable) return;
        try { WriteJson(Path.Combine(_folder, "analysis.json"), _archive); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _status = "Cannot save typing analysis. Changes remain in memory; saving will be retried."; }
    }

    internal static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value);
            stream.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private void Publish()
    {
        var versions = TypingAnalysis.Heuristics.ToDictionary(h => h.Id, h => h.Version);
        var signals = _archive.Signals.Where(s => versions.GetValueOrDefault(s.Heuristic) == s.Version).ToArray();
        var candidates = signals.Where(s => s.Candidate.Length > 0).GroupBy(s => s.Candidate)
            .Select(group => new TypingCandidate(group.Key, _archive.Usage.GetValueOrDefault(group.Key),
                group.Where(s => s.Heuristic is not "correction-followup").Select(s => s.EpisodeId).Distinct().Count(),
                string.Join("; ", group.GroupBy(s => s.Partial ? $"{s.Heuristic} (partial)" : s.Heuristic)
                    .Select(g => $"{g.Key}: {g.Count()}")),
                _archive.ApprovedWords.Contains(group.Key)))
            .OrderByDescending(c => c.Errors).ToArray();
        var paths = EpisodePaths().ToArray();
        var pending = paths.Count(p => !IsAnalyzed(p) && _archive.ExpiryNotices.Contains(Path.GetFileName(p))
            && !_archive.ShownExpiryNotices.Contains(Path.GetFileName(p)));
        var patterns = signals.Where(s => s.Heuristic == "edit-pattern").GroupBy(s => s.Description)
            .Select(g => $"{g.Key} Episodes: {g.Select(s => s.EpisodeId).Distinct().Count()}").ToArray();
        Volatile.Write(ref _snapshot, new(_status, paths.Length, pending, candidates, _archive.ApprovedWords.ToArray(), patterns, _isAnalyzing));
    }

    public void Dispose()
    {
        _stopped = true;
        _analysisCancellation.Cancel();
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(3));
    }
}
