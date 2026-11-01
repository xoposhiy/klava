using Klava.Core;

namespace Klava.Core.Tests;

public class ChatterHistoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    [Theory]
    [InlineData(50, 0, 1)]
    [InlineData(70, 1, 0)]
    public void Records_the_observed_68_ms_repress(int threshold, int blocked, int passed)
    {
        var json = KlavaConfig.DefaultJson.Replace("\"chatteringKeys\": {}", $"\"chatteringKeys\": {{ \"M\": {threshold} }}");
        var kb = new Keyboard(json);
        var history = Observe(kb);

        kb.Tap('M', holdMs: 21, afterMs: 68);
        kb.Tap('M', holdMs: 23);

        var day = Assert.Single(history.Snapshot());
        Assert.Equal('M', day.Vk);
        Assert.Equal(Today, day.Date);
        Assert.Equal(blocked, day.BlockedGaps[6]);
        Assert.Equal(passed, day.PassedGaps[6]);
        Assert.Equal(1, day.BlockedGaps.Sum() + day.PassedGaps.Sum());
    }

    [Fact]
    public void Counts_each_bounce_in_a_chain_once_and_ignores_autorepeats()
    {
        var kb = new Keyboard(KlavaConfig.DefaultJson.Replace("\"chatteringKeys\": {}", "\"chatteringKeys\": { \"M\": 70 }"));
        var history = Observe(kb);
        kb.Tap('M', holdMs: 15, afterMs: 8);
        kb.Tap('M', holdMs: 52, afterMs: 7);
        kb.Down('M');
        kb.Wait(500).Down('M');
        kb.Up('M');

        var day = Assert.Single(history.Snapshot());
        Assert.Equal(2, day.BlockedGaps[0]);
        Assert.Equal(2, day.BlockedGaps.Sum());
        Assert.Equal(0, day.PassedGaps.Sum());
    }

    [Fact]
    public void Keeps_keys_and_days_separate_and_returns_independent_snapshots()
    {
        var history = new ChatterHistory();
        history.Record(Today.ToDateTime(TimeOnly.MinValue), 'M', 68, true);
        history.Record(Today.ToDateTime(TimeOnly.MinValue), Vk.LShift, 2, true);
        history.Record(Today.AddDays(1).ToDateTime(TimeOnly.MinValue), 'M', 99, false);
        var saved = history.Snapshot();
        var restored = new ChatterHistory(saved);
        saved[0].BlockedGaps[6] = 999;
        history.Record(Today.ToDateTime(TimeOnly.MinValue), 'M', 69, true);

        Assert.Equal(3, restored.Snapshot().Length);
        Assert.Equal(1, restored.Snapshot().Single(d => d.Vk == 'M' && d.Date == Today).BlockedGaps[6]);
        Assert.Equal(2, history.Snapshot().Single(d => d.Vk == 'M' && d.Date == Today).BlockedGaps[6]);
        Assert.Equal(1, restored.Snapshot().Single(d => d.Date > Today).PassedGaps[9]);
    }

    [Fact]
    public void Rejects_invalid_saved_counts()
    {
        Assert.Throws<ArgumentException>(() => new ChatterHistory([new ChatterDay(Today, 'M', [-1], [])]));
        var counts = new long[20];
        counts[12] = 1;
        Assert.Throws<ArgumentException>(() => new ChatterHistory([new ChatterDay(Today, 'M', new long[20], counts)]));
    }

    [Fact]
    public void Counts_presses_including_blocked_bounces_but_not_hold_repeats_or_injected_input()
    {
        var kb = new Keyboard();
        var presses = new List<int>();
        kb.Engine.PressObserved += presses.Add;
        kb.Down('M');
        kb.Wait(500).Down('M');
        kb.Up('M');
        kb.Wait(2).Down('M');
        kb.Wait(100).Down('M');
        kb.Up('M');
        kb.Engine.OnKey(new KeyEvent('N', 0, false, false, kb.Now, Injected: true));
        kb.Engine.Enabled = false;
        kb.Tap(0x25);
        Assert.Equal(new[] { (int)'M', 'M', 0x25 }, presses);
        Assert.Single(kb.Bounces);
    }

    [Fact]
    public void Compacts_old_minutes_without_losing_totals()
    {
        var history = new ChatterHistory();
        var time = new DateTime(2026, 10, 30, 10, 20, 25);
        history.RecordPress(time, 'M');
        history.RecordPress(time.AddMinutes(1), 'M');
        history.Record(time.AddMinutes(1), 'M', 3, true);
        history.RecordPress(time.AddHours(3), 'M');
        history.Compact(time.AddHours(1));
        var saved = new ChatterHistory(history.Snapshot()).Snapshot();
        Assert.Equal(2, saved.Length);
        Assert.Equal(3, saved.Sum(d => d.TotalPresses));
        Assert.Equal(1, saved.Sum(d => d.BlockedGaps.Sum()));
        var measuredDay = saved.Single(d => d.Minute is null);
        Assert.Equal(2, measuredDay.TotalPresses);
        Assert.Equal(1, measuredDay.BlockedGaps.Sum());
        Assert.Single(saved, d => d.Minute.HasValue);
    }

    private static ChatterHistory Observe(Keyboard kb)
    {
        var history = new ChatterHistory();
        kb.Engine.PressObserved += vk => history.RecordPress(Today.ToDateTime(TimeOnly.MinValue), vk);
        kb.Engine.BounceBlocked += b => history.Record(Today.ToDateTime(TimeOnly.MinValue), b.Vk, b.GapMs, true);
        kb.Engine.FastRepressPassed += r => history.Record(Today.ToDateTime(TimeOnly.MinValue), r.Vk, r.GapMs, false);
        return history;
    }
}
