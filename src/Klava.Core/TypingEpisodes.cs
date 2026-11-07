namespace Klava.Core;

public sealed record TypingContext(long Window, long Focus, string Application, long Layout, bool CanRecord = true);
public sealed record TypingCorrection(string Original, string Replacement, string Type, string RuleId);
public sealed record TypingEvent(long TimeMs, TypingContext Context, string Kind,
    string Source = "physical", int Vk = 0, int ScanCode = 0, bool Extended = false,
    bool IsUp = false, string Text = "", bool Blocked = false, bool Trigger = false,
    string? Reason = null, TypingCorrection? Correction = null, int X = 0, int Y = 0,
    long PreviousLayout = 0, bool Shift = false, bool Ctrl = false, bool Alt = false,
    bool Win = false, bool CapsLock = false);
public sealed record TypingEpisode(string Id, IReadOnlyList<TypingEvent> Events, string EndReason);

/// <summary>Groups correction events with nearby input. Time is supplied by the caller.</summary>
public sealed class TypingEpisodeCollector(string episodePrefix = "episode")
{
    public const int ContextChars = 20;
    public const int IdleMs = 3000;
    private readonly List<TypingEvent> _before = [];
    private List<TypingEvent>? _active;
    private long _lastMs;
    private TypingContext? _context;
    private long _episodeNumber;
    public event Action<TypingEpisode>? Completed;

    public void Observe(TypingEvent e)
    {
        Advance(e.TimeMs);
        if (!e.Context.CanRecord) { Reset("private-field"); _context = null; return; }
        if (_context is { } previous && (previous.Window != e.Context.Window || previous.Focus != e.Context.Focus))
            Reset("focus-change");
        _context = e.Context;
        _lastMs = e.TimeMs;
        if (_active is null && e.Trigger) _active = new List<TypingEvent>(_before);
        _active?.Add(e);
        _before.Add(e);
        var chars = 0;
        var start = _before.Count;
        while (start > 0 && chars < ContextChars) { start--; chars += _before[start].Text.Length; }
        if (start > 0) _before.RemoveRange(0, start);
        if (_before.Count > 512) _before.RemoveRange(0, _before.Count - 512);
        // Long sessions use time-adjacent parts instead of unbounded memory.
        if (_active?.Count >= 10000) Finish("continuation");
    }

    public void Advance(long nowMs)
    {
        if (nowMs - _lastMs >= IdleMs) Reset("idle");
    }

    public void Reset(string reason) { Finish(reason); _before.Clear(); }

    private void Finish(string reason)
    {
        if (_active is not { Count: > 0 } events) return;
        _active = reason == "continuation" ? [] : null;
        Completed?.Invoke(new($"{episodePrefix}_{++_episodeNumber}", events.ToArray(), reason));
    }
}

/// <summary>Physical key mapping for US English and Russian.</summary>
public static class TypingLayouts
{
    private const string English = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string Russian = "ёйцукенгшщзхъфывапролджэячсмитьбю";
    public static string Convert(string text, bool toEnglish) => string.Concat(text.Select(c =>
    {
        var index = (toEnglish ? Russian : English).IndexOf(char.ToLowerInvariant(c));
        if (index < 0) return c;
        var mapped = (toEnglish ? English : Russian)[index];
        return char.IsUpper(c) ? char.ToUpperInvariant(mapped) : mapped;
    }));
}
