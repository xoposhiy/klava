using Klava.App;

namespace Klava.App.Tests;

public sealed class HistorySafetyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-history-safety-tests-" + Guid.NewGuid());

    [Fact]
    public void Invalid_key_codes_and_gaps_do_not_stop_history_or_lose_valid_counts()
    {
        var path = Path.Combine(_folder, "history.json");
        using (var store = new ChatterHistoryStore(path))
        {
            foreach (var vk in new[] { -1, 0, 255, 256, int.MaxValue })
            {
                store.RecordPress(vk);
                store.Record(vk, 10, true);
            }
            store.Record('M', -1, true);
            store.Record('M', 200, true);
            store.Record('M', 100, false);
            store.RecordPress('M');
            store.Record('M', 10, true);
        }
        using var restored = new ChatterHistoryStore(path);
        Assert.True(SpinWait.SpinUntil(() => restored.Snapshot.Length == 1, TimeSpan.FromSeconds(5)));
        var day = Assert.Single(restored.Snapshot);
        Assert.Equal('M', day.Vk);
        Assert.Equal(1, day.TotalPresses);
        Assert.Equal(1, day.BlockedGaps.Sum());
    }

    [Fact]
    public void Late_callbacks_after_disposal_do_not_throw()
    {
        var store = new ChatterHistoryStore(Path.Combine(_folder, "history.json"));
        store.Dispose();
        store.RecordPress('M');
        store.Record('M', 10, true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
