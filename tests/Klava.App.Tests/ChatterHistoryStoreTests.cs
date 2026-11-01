using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

public sealed class ChatterHistoryStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-history-tests-" + Guid.NewGuid());
    private string HistoryPath => Path.Combine(_folder, "chatter-history.json");

    public ChatterHistoryStoreTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Saves_pending_counts_on_exit_and_keeps_them_after_restart()
    {
        using (var store = new ChatterHistoryStore(HistoryPath))
        {
            store.RecordPress('M');
            store.Record('M', 68, true);
            store.RecordPress('M');
            store.Record('M', 76, false);
            store.RecordPress(Vk.LShift);
            store.Record(Vk.LShift, 2, true);
        }
        using var restored = new ChatterHistoryStore(HistoryPath);
        Assert.True(SpinWait.SpinUntil(() => restored.Snapshot.Length == 2, TimeSpan.FromSeconds(5)));
        var m = restored.Snapshot.Single(d => d.Vk == 'M');
        Assert.Equal(2, m.TotalPresses);
        Assert.Equal(1, m.BlockedGaps[6]);
        Assert.Equal(1, m.PassedGaps[7]);
        Assert.False(File.Exists(HistoryPath + ".tmp"));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"Version\":99,\"Days\":[]}")]
    public void Keeps_an_unreadable_history_file_and_collects_new_counts_in_memory(string contents)
    {
        File.WriteAllText(HistoryPath, contents);
        using (var store = new ChatterHistoryStore(HistoryPath))
        {
            store.Record('M', 68, true);
            Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Length == 1, TimeSpan.FromSeconds(5)));
            Assert.Contains("only in memory", store.Status);
        }
        Assert.Equal(contents, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void Retries_a_failed_save_without_losing_counts()
    {
        using var store = new ChatterHistoryStore(HistoryPath);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(HistoryPath), TimeSpan.FromSeconds(5)));
        File.Delete(HistoryPath);
        Directory.CreateDirectory(HistoryPath);
        store.Record('M', 68, true);
        Assert.True(SpinWait.SpinUntil(() => store.Status.StartsWith("Cannot save", StringComparison.Ordinal), TimeSpan.FromSeconds(8)));
        Assert.Equal(1, Assert.Single(store.Snapshot).BlockedGaps.Sum());
        Directory.Delete(HistoryPath);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(HistoryPath), TimeSpan.FromSeconds(8)));
        Assert.Equal("History is saved automatically.", store.Status);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
