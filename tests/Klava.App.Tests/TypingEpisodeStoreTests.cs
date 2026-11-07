using System.Text.Json;
using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

public sealed class TypingEpisodeStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-typing-tests-" + Guid.NewGuid());
    private static readonly TypingContext Context = new(1, 1, "test", 0x409);
    private string ArchivePath => Path.Combine(_folder, "analysis.json");

    public TypingEpisodeStoreTests() => Directory.CreateDirectory(_folder);

    private static void Ready(TypingEpisodeStore store) => Assert.True(SpinWait.SpinUntil(
        () => !store.Snapshot.Status.StartsWith("Loading", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));

    private static void Analyze(TypingEpisodeStore store)
    {
        store.AnalyzeNow();
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Status.StartsWith("Analysis complete", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
    }

    private string SaveEpisode(string id, int ageDays = 0)
    {
        var episode = new TypingEpisode(id,
        [new(1000, Context, "correction", "klava", Trigger: true,
            Correction: new("реез", "http", "shift-shift", "shift-shift:v1"))], "idle");
        var path = Path.Combine(_folder, $"episode-20260101-{id}.json");
        TypingEpisodeStore.WriteJson(path, new SavedTypingEpisode(1, DateTimeOffset.UtcNow.AddDays(-ageDays), episode, DateTimeOffset.UtcNow.AddMilliseconds(-1000)));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
        return path;
    }

    [Fact]
    public void Analysis_is_manual_versioned_and_idempotent_and_never_approves_a_word()
    {
        SaveEpisode("one");
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            Assert.Empty(store.Snapshot.Candidates);
            Analyze(store);
            var candidate = Assert.Single(store.Snapshot.Candidates);
            Assert.Equal("http", candidate.Word);
            Assert.Equal(1, candidate.Errors);
            Assert.False(candidate.Approved);
            store.AnalyzeNow();
        }
        var archive = JsonSerializer.Deserialize<TypingArchive>(File.ReadAllText(ArchivePath))!;
        Assert.Equal(TypingAnalysis.Heuristics.Count, archive.Runs.Count);
        Assert.Single(archive.Signals);
        Assert.Empty(archive.ApprovedWords);
        using var restored = new TypingEpisodeStore(_folder);
        Ready(restored);
        Assert.Equal(1, Assert.Single(restored.Snapshot.Candidates).Errors);
    }

    [Fact]
    public void Normal_usage_is_counted_outside_episodes_and_approval_survives_restart()
    {
        SaveEpisode("one");
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            var time = Clock.NowMs();
            foreach (var c in "http http ") store.Record(new(time++, Context, "key", Text: c.ToString()));
            Analyze(store);
            Assert.Equal(2, Assert.Single(store.Snapshot.Candidates).Usage);
            store.ApproveWord("http");
        }
        using var restored = new TypingEpisodeStore(_folder);
        Ready(restored);
        Assert.Equal("http", Assert.Single(restored.Snapshot.ApprovedWords));
        Assert.Equal(2, Assert.Single(restored.Snapshot.Candidates).Usage);
    }

    [Fact]
    public void A_new_heuristic_version_reanalyzes_saved_data_without_doubling_displayed_signals()
    {
        SaveEpisode("one");
        TypingEpisodeStore.WriteJson(ArchivePath, new TypingArchive
        {
            Runs = [new("one", "shift-shift", "0")],
            Signals = [new("one", "shift-shift", "0", "http", "реез", "Earlier result.")]
        });
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            Analyze(store);
            Assert.Equal("shift-shift: 1", Assert.Single(store.Snapshot.Candidates).Signals);
        }
        var archive = JsonSerializer.Deserialize<TypingArchive>(File.ReadAllText(ArchivePath))!;
        Assert.Equal(2, archive.Signals.Count);
        Assert.Contains(archive.Runs, r => r.Heuristic == "shift-shift" && r.Version == "1");
    }

    [Fact]
    public void Expired_unanalyzed_data_waits_for_a_notice_then_is_deleted_while_results_remain()
    {
        var path = SaveEpisode("old", 8);
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            Assert.True(File.Exists(path));
            Assert.Equal(1, store.Snapshot.PendingExpiry);
            store.ExpiryNoticeShown();
        }
        using var restored = new TypingEpisodeStore(_folder);
        Ready(restored);
        Assert.False(File.Exists(path));
        Assert.Equal(0, restored.Snapshot.Episodes);
    }

    [Fact]
    public void Analyzed_raw_data_expires_without_deleting_signals_or_counts()
    {
        var path = SaveEpisode("old", 8);
        using (var store = new TypingEpisodeStore(_folder)) { Ready(store); Analyze(store); }
        using var restored = new TypingEpisodeStore(_folder);
        Ready(restored);
        Assert.False(File.Exists(path));
        Assert.Equal(1, Assert.Single(restored.Snapshot.Candidates).Errors);
        Assert.Equal(TypingAnalysis.Heuristics.Count,
            JsonSerializer.Deserialize<TypingArchive>(File.ReadAllText(ArchivePath))!.Runs.Count);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"Version\":99}")]
    public void A_broken_archive_is_preserved_and_does_not_stop_input_collection(string text)
    {
        File.WriteAllText(ArchivePath, text);
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            Assert.Contains("in memory only", store.Snapshot.Status);
            store.Record(new(Clock.NowMs(), Context, "key", Vk: Vk.Backspace, Trigger: true));
        }
        Assert.Equal(text, File.ReadAllText(ArchivePath));
        Assert.Empty(Directory.GetFiles(_folder, "episode-*.json"));
    }

    [Fact]
    public void Privacy_transitions_remove_private_text_and_shutdown_saves_the_active_episode()
    {
        using (var store = new TypingEpisodeStore(_folder))
        {
            Ready(store);
            var time = Clock.NowMs();
            store.Record(new(time, Context, "key", Text: "public", Trigger: true));
            store.Record(new(time + 1, Context with { CanRecord = false }, "key", Text: "password", Trigger: true));
            store.Record(new(time + 2, Context, "key", Text: "after", Trigger: true));
        }
        var paths = Directory.GetFiles(_folder, "episode-*.json");
        Assert.Equal(2, paths.Length);
        var text = string.Join("", paths.Select(File.ReadAllText));
        Assert.DoesNotContain("password", text);
        Assert.Contains("public", text);
        Assert.Contains("after", text);
    }

    [Fact]
    public void Episode_window_has_usable_controls_at_its_minimum_size()
    {
        SaveEpisode("one");
        using var store = new TypingEpisodeStore(_folder);
        Ready(store);
        Analyze(store);
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(store, () => { });
            form.Size = form.MinimumSize;
            FormTestKit.Preview(form, "typing-episodes-small");
            var controls = FormTestKit.Descendants(form).ToArray();
            Assert.Contains(controls.OfType<Button>(), b => b.Text == "Analyze saved episodes" && b.Width >= b.PreferredSize.Width);
            var table = Assert.Single(controls.OfType<DataGridView>());
            var row = Assert.Single(table.Rows.Cast<DataGridViewRow>());
            Assert.Equal("http", row.Cells["word"].Value);
            Assert.Equal("реез", row.Cells["russian"].Value);
            Assert.Equal(table.Columns["word"]!.DisplayIndex + 1, table.Columns["russian"]!.DisplayIndex);
            Assert.True(table.Height > 50);
            var tabs = Assert.Single(controls.OfType<TabControl>());
            tabs.SelectedTab = Assert.Single(tabs.TabPages.Cast<TabPage>(), page => page.Text == "Dictionary");
            Assert.Empty(Assert.Single(controls.OfType<ListBox>()).Items.Cast<string>());
            Assert.Contains(controls.OfType<Label>(), label => label.Text.StartsWith("The dictionary is empty", StringComparison.Ordinal));
            FormTestKit.Preview(form, "typing-dictionary-empty");
        });
    }

    [Fact]
    public void The_dictionary_shows_approved_words_without_analysis_or_usage_and_accepts_manual_additions()
    {
        TypingEpisodeStore.WriteJson(ArchivePath, new TypingArchive { ApprovedWords = ["ast"] });
        using var store = new TypingEpisodeStore(_folder);
        Ready(store);
        Assert.Empty(store.Snapshot.Candidates);
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(store, () => { });
            var controls = FormTestKit.Descendants(form).ToArray();
            var tabs = Assert.Single(controls.OfType<TabControl>());
            tabs.SelectedTab = Assert.Single(tabs.TabPages.Cast<TabPage>(), page => page.Text == "Dictionary");
            FormTestKit.Preview(form, "typing-dictionary");
            var dictionary = Assert.Single(controls.OfType<ListBox>());
            Assert.Equal("ast (фые)", dictionary.GetItemText(Assert.Single(dictionary.Items.Cast<string>())));
            Assert.Single(controls.OfType<TextBox>(), box => !box.Multiline).Text = "LLM";
            Assert.Single(controls.OfType<Button>(), button => button.Text == "Add word").PerformClick();
            Assert.True(SpinWait.SpinUntil(() => store.Snapshot.ApprovedWords.Contains("llm"), TimeSpan.FromSeconds(5)));
        });
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
