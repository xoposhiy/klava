namespace Klava.Core;

/// <summary>One press of a traced key with its neighbors, for debugging chatter and taps.</summary>
/// <param name="PauseBeforeMs">Time from the previous key press (any key) to this press; -1 if none.</param>
/// <param name="SameKeyGapMs">Time from the previous release of the same key; -1 if none.</param>
/// <param name="HeldMs">Press length; -1 if the release never came.</param>
/// <param name="NextAfterUpMs">Time from this release to the next key press; negative if the next key went down during the hold.</param>
public sealed record KeyTrace(
    int Vk, long DownMs, bool Blocked, int? PreviousVk, long PauseBeforeMs, long SameKeyGapMs,
    long HeldMs, int Repeats, int? NextVk, long NextAfterUpMs, bool NextBlocked)
{
    public override string ToString()
    {
        var name = KeyNames.Name(Vk) + (Blocked ? " (BLOCKED)" : "");
        var before = PreviousVk is { } p ? $"{KeyNames.Name(p)}, {PauseBeforeMs} ms before" : "none";
        var held = HeldMs < 0 ? "no release" : $"{HeldMs} ms" + (Repeats > 0 ? $", {Repeats} autorepeats" : "");
        var after = NextVk is not { } n
            ? "none"
            : KeyNames.Name(n) + (NextBlocked ? " (BLOCKED)" : "")
              + (NextAfterUpMs >= 0 ? $", {NextAfterUpMs} ms after release" : $", during the hold ({HeldMs + NextAfterUpMs} ms after press)");
        var gap = SameKeyGapMs < 0 ? "" : $" | since last {KeyNames.Name(Vk)} release: {SameKeyGapMs} ms";
        return $"{name} | previous: {before} | held: {held} | next: {after}{gap}";
    }
}

/// <summary>
/// Traces presses of selected keys with the keys and pauses around them. It sees every physical
/// event, including events that the debounce filter blocks. Opt-in: the trace shows neighbor
/// keys, which are fragments of typed text.
/// </summary>
public sealed class KeyTracer(IReadOnlySet<int> tracedKeys)
{
    private const long MaxPendingMs = 10_000;

    private sealed class Entry
    {
        public required int Vk;
        public required long DownMs;
        public required bool Blocked;
        public int? PreviousVk;
        public long PauseBeforeMs = -1;
        public long SameKeyGapMs = -1;
        public long? UpMs;
        public int Repeats;
        public int? NextVk;
        public long NextDownMs;
        public bool NextBlocked;
    }

    private readonly List<Entry> _pending = [];
    private readonly HashSet<int> _down = [];
    private readonly Dictionary<int, long> _lastUp = [];
    private (int Vk, long Ms)? _lastPress;

    /// <summary>Raised when a trace is complete: the next key went down and the traced key went up.</summary>
    public event Action<KeyTrace>? Traced;

    public void Observe(in KeyEvent e, bool blocked)
    {
        if (e.IsUp)
        {
            _down.Remove(e.Vk);
            foreach (var entry in _pending)
                if (entry.Vk == e.Vk && entry.UpMs is null)
                    entry.UpMs = e.TimeMs;
            _lastUp[e.Vk] = e.TimeMs;
            Flush(e.TimeMs);
            return;
        }

        if (!_down.Add(e.Vk))
        {
            foreach (var entry in _pending)
                if (entry.Vk == e.Vk && entry.UpMs is null)
                    entry.Repeats++;
            return;
        }

        foreach (var entry in _pending)
        {
            if (entry.NextVk is not null) continue;
            entry.NextVk = e.Vk;
            entry.NextDownMs = e.TimeMs;
            entry.NextBlocked = blocked;
        }

        if (tracedKeys.Contains(e.Vk))
        {
            _pending.Add(new Entry
            {
                Vk = e.Vk,
                DownMs = e.TimeMs,
                Blocked = blocked,
                PreviousVk = _lastPress?.Vk,
                PauseBeforeMs = _lastPress is { } last ? e.TimeMs - last.Ms : -1,
                SameKeyGapMs = _lastUp.TryGetValue(e.Vk, out var up) ? e.TimeMs - up : -1,
            });
        }
        _lastPress = (e.Vk, e.TimeMs);
        Flush(e.TimeMs);
    }

    private void Flush(long nowMs)
    {
        _pending.RemoveAll(entry =>
        {
            var complete = entry.UpMs is not null && entry.NextVk is not null;
            var stale = nowMs - entry.DownMs > MaxPendingMs;
            if (!complete && !stale) return false;
            Traced?.Invoke(new KeyTrace(
                entry.Vk, entry.DownMs, entry.Blocked, entry.PreviousVk, entry.PauseBeforeMs, entry.SameKeyGapMs,
                entry.UpMs is { } up ? up - entry.DownMs : -1, entry.Repeats, entry.NextVk,
                entry.NextVk is null ? 0 : entry.NextDownMs - (entry.UpMs ?? entry.NextDownMs), entry.NextBlocked));
            return true;
        });
    }
}
