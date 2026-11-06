namespace Klava.Core;

public sealed record ChatterDay(DateOnly Date, int Vk, long[] BlockedGaps, long[] PassedGaps,
    long TotalPresses = 0, DateTime? Minute = null);

/// <summary>Daily and recent minute counts by key and release-to-press gap. No key order or typed text is kept.</summary>
public sealed class ChatterHistory
{
    public const int GapBucketMs = 10;
    public const int GapBucketCount = 20;
    private readonly Dictionary<(DateOnly Date, int Vk, DateTime? Minute), ChatterDay> _days = [];

    public ChatterHistory(IEnumerable<ChatterDay>? days = null)
    {
        foreach (var day in days ?? [])
        {
            if (day is null || day.Vk is <= 0 or >= 255
                || !ValidCounts(day.BlockedGaps) || !ValidCounts(day.PassedGaps)
                || day.PassedGaps.Skip(10).Any(n => n != 0)
                || day.TotalPresses < 0
                || day.Minute is { } minute && (DateOnly.FromDateTime(minute) != day.Date || minute.Ticks % TimeSpan.TicksPerMinute != 0)
                || !_days.TryAdd((day.Date, day.Vk, day.Minute), Copy(day)))
                throw new ArgumentException("Invalid chatter history.", nameof(days));
        }
    }

    public void RecordPress(DateTime time, int vk)
    {
        var minute = RoundMinute(time);
        var day = Get(DateOnly.FromDateTime(time), vk, minute);
        _days[(day.Date, vk, minute)] = day with { TotalPresses = day.TotalPresses + 1 };
    }

    public void Record(DateTime time, int vk, long gapMs, bool blocked)
    {
        if (gapMs < 0 || gapMs >= (blocked ? 200 : Engine.FastRepressMs))
            throw new ArgumentOutOfRangeException(nameof(gapMs));
        var day = Get(DateOnly.FromDateTime(time), vk, RoundMinute(time));
        (blocked ? day.BlockedGaps : day.PassedGaps)[(int)(gapMs / GapBucketMs)]++;
    }

    public static DateTime RoundMinute(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerMinute, time.Kind);

    // Only the recent rolling window needs minute counts. Keep older counts in daily buckets.
    public bool Compact(DateTime before)
    {
        var oldMinutes = _days.Values.Where(d => d.Minute < before).ToArray();
        foreach (var day in oldMinutes)
        {
            var daily = Get(day.Date, day.Vk, null);
            for (var i = 0; i < GapBucketCount; i++)
            {
                daily.BlockedGaps[i] += day.BlockedGaps[i];
                daily.PassedGaps[i] += day.PassedGaps[i];
            }
            _days[(day.Date, day.Vk, null)] = daily with
            {
                TotalPresses = daily.TotalPresses + day.TotalPresses,
            };
            _days.Remove((day.Date, day.Vk, day.Minute));
        }
        return oldMinutes.Length > 0;
    }

    private ChatterDay Get(DateOnly date, int vk, DateTime? minute)
    {
        if (vk is <= 0 or >= 255) throw new ArgumentOutOfRangeException(nameof(vk));
        var id = (date, vk, minute);
        if (!_days.TryGetValue(id, out var day))
            _days[id] = day = new ChatterDay(date, vk, new long[GapBucketCount], new long[GapBucketCount], 0, minute);
        return day;
    }

    public ChatterDay[] Snapshot() => _days.Values.OrderBy(d => d.Date).ThenBy(d => d.Vk).Select(Copy).ToArray();

    /// <summary>Live records for saving on the owner thread. Do not change or retain the records.</summary>
    public IEnumerable<ChatterDay> Records => _days.Values;

    private static ChatterDay Copy(ChatterDay day) => day with
    {
        BlockedGaps = (long[])day.BlockedGaps.Clone(),
        PassedGaps = (long[])day.PassedGaps.Clone(),
    };

    private static bool ValidCounts(long[]? counts) =>
        counts is { Length: GapBucketCount } && counts.All(n => n is >= 0 and <= 1_000_000_000);
}
