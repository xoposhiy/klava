using System.Diagnostics;
using System.Text.Json;
using Klava.Core;

namespace Klava.App.Tests;

public sealed class HistoryPerformanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-history-performance-tests-" + Guid.NewGuid());

    [Fact]
    public void Publishes_at_most_once_per_second_during_continuous_input()
    {
        using var store = new ChatterHistoryStore(Path.Combine(_folder, "history.json"));
        var previous = store.Snapshot;
        var publications = 0;
        var elapsed = Stopwatch.StartNew();
        for (var i = 0; i < 300; i++)
        {
            store.RecordPress('M');
            var snapshot = store.Snapshot;
            if (!ReferenceEquals(previous, snapshot)) publications++;
            previous = snapshot;
            Thread.Sleep(2);
        }
        Assert.True(publications <= Math.Ceiling(elapsed.Elapsed.TotalSeconds) + 1,
            "History copies must follow the refresh interval, not the input rate.");
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Sum(d => d.TotalPresses) == 300, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Saves_input_without_building_display_snapshots_until_a_reader_requests_one()
    {
        var path = Path.Combine(_folder, "history.json");
        using var store = new ChatterHistoryStore(path);
        for (var i = 0; i < 300; i++) store.RecordPress('M');

        Assert.True(SpinWait.SpinUntil(() => SavedPresses(path) == 300, TimeSpan.FromSeconds(8)));
        Assert.Empty(store.Snapshot);
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Sum(d => d.TotalPresses) == 300, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Saves_a_year_of_counts_without_copying_all_gap_arrays()
    {
        var history = new ChatterHistory();
        var start = DateTime.Today.AddDays(-365);
        for (var day = 0; day < 365; day++)
            for (var vk = 32; vk < 132; vk++) history.RecordPress(start.AddDays(day), vk);
        history.Compact(DateTime.Today);
        var saved = new { Version = 3, Days = history.Records };
        JsonSerializer.Serialize(Stream.Null, saved);

        var before = GC.GetAllocatedBytesForCurrentThread();
        JsonSerializer.Serialize(Stream.Null, saved);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1_000_000, $"Saving allocated {allocated:N0} bytes.");
        Assert.Equal(36_500, history.Records.Sum(day => day.TotalPresses));
    }

    private static long SavedPresses(string path)
    {
        try
        {
            if (!File.Exists(path)) return -1;
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            return saved.RootElement.GetProperty("Days").EnumerateArray().Sum(day => day.GetProperty("TotalPresses").GetInt64());
        }
        catch (IOException) { return -1; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
