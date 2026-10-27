namespace Klava.Core;

/// <summary>
/// Blocks key chatter: a press that comes too soon after the release of the same key.
/// The bounced press, its autorepeats and its release are all blocked.
/// </summary>
public sealed class Debouncer(int thresholdMs, IReadOnlyDictionary<int, int>? perKeyThresholdMs = null)
{
    private readonly Dictionary<int, long> _downAt = [];
    private readonly Dictionary<int, (long UpMs, long HeldMs)> _lastRelease = [];
    private readonly HashSet<int> _suppressed = [];

    /// <summary>Returns true to block the event.</summary>
    /// <param name="gapMs">
    /// For a new press (blocked or not): the time since the release of the same key; otherwise -1.
    /// </param>
    /// <param name="previousHeldMs">For a new press: how long the previous press of the key lasted.</param>
    public bool ShouldBlock(in KeyEvent e, out long gapMs, out long previousHeldMs)
    {
        gapMs = -1;
        previousHeldMs = -1;
        var key = e.KeyId;

        if (e.IsUp)
        {
            if (_suppressed.Remove(key))
            {
                // A chain of bounces: the next press is measured from this release, not the real one.
                if (_lastRelease.TryGetValue(key, out var real)) _lastRelease[key] = (e.TimeMs, real.HeldMs);
                return true;
            }
            var held = _downAt.Remove(key, out var down) ? e.TimeMs - down : -1;
            _lastRelease[key] = (e.TimeMs, held);
            return false;
        }

        if (_suppressed.Contains(key)) return true;
        if (_downAt.ContainsKey(key)) return false; // autorepeat of a real press

        if (_lastRelease.TryGetValue(key, out var last))
        {
            gapMs = e.TimeMs - last.UpMs;
            previousHeldMs = last.HeldMs;
            if (gapMs < Threshold(e.Vk))
            {
                _suppressed.Add(key);
                return true;
            }
        }

        _downAt[key] = e.TimeMs;
        return false;
    }

    private int Threshold(int vk) =>
        perKeyThresholdMs is not null && perKeyThresholdMs.TryGetValue(vk, out var ms) ? ms : thresholdMs;
}
