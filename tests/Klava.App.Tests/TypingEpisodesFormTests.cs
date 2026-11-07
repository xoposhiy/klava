using System.ComponentModel;
using Klava.App;

namespace Klava.App.Tests;

public sealed class TypingEpisodesFormTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-typing-form-tests-" + Guid.NewGuid());
    private readonly TypingEpisodeStore _store;

    public TypingEpisodesFormTests()
    {
        _store = new TypingEpisodeStore(_folder);
        Assert.True(SpinWait.SpinUntil(() => !_store.Snapshot.Status.StartsWith("Loading", StringComparison.Ordinal), TimeSpan.FromSeconds(5)));
    }

    private static TypingSnapshot Snapshot() => new("Ready", 100, 0,
        Enumerable.Range(0, 70).Select(i => new TypingCandidate($"word{(char)('a' + i / 26)}{(char)('a' + i % 26)}",
            i + 1, 70 - i, $"signal: {i}", false)).ToArray(),
        Enumerable.Range(0, 70).Select(i => $"approved{(char)('a' + i / 26)}{(char)('a' + i % 26)}").ToArray(),
        Enumerable.Range(0, 70).Select(i => $"Pattern {i}").ToArray());

    [Fact]
    public void Equal_data_keeps_rows_text_selection_and_dictionary_validation()
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            FormTestKit.Preview(form, "typing-refresh-initial");
            var snapshot = Snapshot();
            form.ShowSnapshot(snapshot);
            var controls = FormTestKit.Descendants(form).ToArray();
            var table = Assert.Single(controls.OfType<DataGridView>());
            var patterns = Assert.Single(controls.OfType<TextBox>(), box => box.Multiline);
            var draft = Assert.Single(controls.OfType<TextBox>(), box => !box.Multiline);
            table.Sort(table.Columns["word"]!, ListSortDirection.Descending);
            var row = table.Rows[20];
            table.CurrentCell = row.Cells["signals"];
            table.FirstDisplayedScrollingRowIndex = 10;
            patterns.Select(50, 20);
            patterns.ScrollToCaret();
            var tabs = Assert.Single(controls.OfType<TabControl>());
            tabs.SelectedIndex = 1;
            draft.Text = "123";
            Assert.Single(controls.OfType<Button>(), button => button.Text == "Add word").PerformClick();

            form.ShowSnapshot(snapshot with
            {
                Status = "Updated counts", Episodes = 101, Candidates = snapshot.Candidates.ToArray(),
                ApprovedWords = snapshot.ApprovedWords.ToArray(), Patterns = snapshot.Patterns.ToArray()
            });

            Assert.Same(row, table.CurrentRow);
            Assert.Equal(SortOrder.Descending, table.SortOrder);
            Assert.Equal(10, table.FirstDisplayedScrollingRowIndex);
            Assert.Equal(50, patterns.SelectionStart);
            Assert.Equal(20, patterns.SelectionLength);
            Assert.Equal("123", draft.Text);
            Assert.Equal(1, tabs.SelectedIndex);
            Assert.Contains(controls.OfType<Label>(), label => label.Text == "Enter an English word of 1..100 letters.");
        });
    }

    [Theory]
    [InlineData("usage", ListSortDirection.Ascending)]
    [InlineData("usage", ListSortDirection.Descending)]
    [InlineData("errors", ListSortDirection.Ascending)]
    [InlineData("errors", ListSortDirection.Descending)]
    [InlineData("word", ListSortDirection.Descending)]
    [InlineData("russian", ListSortDirection.Ascending)]
    [InlineData("signals", ListSortDirection.Descending)]
    public void Changed_candidates_keep_sort_selection_column_and_scroll(string column, ListSortDirection direction)
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            FormTestKit.Preview(form, "typing-sort-initial");
            var snapshot = Snapshot();
            form.ShowSnapshot(snapshot);
            var table = Assert.Single(FormTestKit.Descendants(form).OfType<DataGridView>());
            table.Sort(table.Columns[column]!, direction);
            table.CurrentCell = table.Rows[20].Cells["signals"];
            var selected = ((TypingCandidate)table.CurrentRow!.Tag!).Word;
            table.FirstDisplayedScrollingRowIndex = 10;
            var top = ((TypingCandidate)table.Rows[10].Tag!).Word;

            form.ShowSnapshot(snapshot with
            {
                Candidates = snapshot.Candidates.Select(c => c.Word == selected ? c with { Usage = 1000, Errors = 1000 } : c).ToArray()
            });

            Assert.Equal(column, table.SortedColumn!.Name);
            Assert.Equal(direction == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending, table.SortOrder);
            Assert.Equal(selected, ((TypingCandidate)table.CurrentRow!.Tag!).Word);
            Assert.Equal("signals", table.CurrentCell!.OwningColumn!.Name);
            Assert.True(table.CurrentRow.Selected);
            Assert.Equal(top, ((TypingCandidate)table.Rows[table.FirstDisplayedScrollingRowIndex].Tag!).Word);
            if (column is "usage" or "errors")
            {
                var values = table.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToInt64(row.Cells[column].Value)).ToArray();
                Assert.Equal(direction == ListSortDirection.Ascending ? values.Order() : values.OrderDescending(), values);
            }
        });
    }

    [Fact]
    public void Dictionary_updates_keep_selection_scroll_and_an_unfinished_word()
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            FormTestKit.Preview(form, "typing-dictionary-refresh");
            var snapshot = Snapshot();
            form.ShowSnapshot(snapshot);
            var controls = FormTestKit.Descendants(form).ToArray();
            Assert.Single(controls.OfType<TabControl>()).SelectedIndex = 1;
            var dictionary = Assert.Single(controls.OfType<ListBox>());
            dictionary.SelectedIndex = 25;
            dictionary.TopIndex = 10;
            var selected = dictionary.SelectedItem;
            var top = dictionary.Items[dictionary.TopIndex];
            var draft = Assert.Single(controls.OfType<TextBox>(), box => !box.Multiline);
            draft.Text = "unfinished";
            draft.Select(2, 4);
            form.ShowSnapshot(snapshot with { ApprovedWords = snapshot.ApprovedWords.Append("aaa").ToArray() });
            Assert.Equal(selected, dictionary.SelectedItem);
            Assert.Equal(top, dictionary.Items[dictionary.TopIndex]);
            Assert.Equal("unfinished", draft.Text);
            Assert.Equal(2, draft.SelectionStart);
            Assert.Equal(4, draft.SelectionLength);
        });
    }

    [Fact]
    public void Removed_rows_fall_back_to_a_valid_selection_and_an_empty_table_keeps_its_sort()
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            FormTestKit.Preview(form, "typing-removed-rows");
            var snapshot = Snapshot();
            form.ShowSnapshot(snapshot);
            var table = Assert.Single(FormTestKit.Descendants(form).OfType<DataGridView>());
            table.Sort(table.Columns["usage"]!, ListSortDirection.Descending);
            table.CurrentCell = table.Rows[69].Cells[0];
            table.FirstDisplayedScrollingRowIndex = 50;
            form.ShowSnapshot(snapshot with { Candidates = snapshot.Candidates.Take(2).ToArray() });
            Assert.Equal(2, table.Rows.Count);
            Assert.NotNull(table.CurrentCell);
            form.ShowSnapshot(snapshot with { Candidates = [] });
            Assert.Empty(table.Rows.Cast<DataGridViewRow>());
            form.ShowSnapshot(snapshot);
            Assert.Equal("usage", table.SortedColumn!.Name);
            Assert.Equal(SortOrder.Descending, table.SortOrder);
            Assert.Equal(70L, table.Rows[0].Cells["usage"].Value);
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Minimum_size_keeps_text_and_actions_inside_the_layout(float scale)
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            form.Scale(new SizeF(scale, scale));
            form.Font = new Font(form.Font.FontFamily, form.Font.Size * scale);
            form.Size = new Size((int)(650 * scale), (int)(450 * scale));
            FormTestKit.Preview(form, $"typing-minimum-{scale:0.0}");
            form.ShowSnapshot(Snapshot() with { Status = "Analysis complete; 100 unreadable episodes were skipped." });
            var controls = FormTestKit.Descendants(form).ToArray();
            Assert.Single(controls.OfType<TabControl>()).SelectedIndex = 1;
            Assert.Single(controls.OfType<TextBox>(), box => !box.Multiline).Text = "123";
            Assert.Single(controls.OfType<Button>(), button => button.Text == "Add word").PerformClick();
            form.PerformLayout();
            foreach (var label in controls.OfType<Label>())
                Assert.True(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"Clipped label: {label.Text}");
            foreach (var button in controls.OfType<Button>())
                Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds), $"Clipped action: {button.Text}");
            Assert.True(Assert.Single(controls.OfType<DataGridView>()).Height > 50 * scale);
            FormTestKit.Preview(form, $"typing-dictionary-minimum-{scale:0.0}");
        });
    }

    [Fact]
    public void Actions_follow_the_visible_selection_and_analysis_state()
    {
        FormTestKit.Run(() =>
        {
            using var form = new TypingEpisodesForm(_store, () => { });
            FormTestKit.Preview(form, "typing-actions");
            var snapshot = Snapshot();
            form.ShowSnapshot(snapshot);
            var controls = FormTestKit.Descendants(form).ToArray();
            var approve = Assert.Single(controls.OfType<Button>(), button => button.Text == "Approve selected word");
            var analyze = Assert.Single(controls.OfType<Button>(), button => button.Text == "Analyze saved episodes");
            var table = Assert.Single(controls.OfType<DataGridView>());
            table.CurrentCell = table.Rows[0].Cells[0];
            Assert.True(approve.Enabled);
            var tabs = Assert.Single(controls.OfType<TabControl>());
            tabs.SelectedIndex = 1;
            Assert.False(approve.Enabled);
            tabs.SelectedIndex = 0;
            table.ClearSelection();
            Assert.False(approve.Enabled);
            form.ShowSnapshot(snapshot with { IsAnalyzing = true });
            Assert.False(analyze.Enabled);
            form.ShowSnapshot(snapshot with { IsAnalyzing = true, Episodes = 200 });
            Assert.False(analyze.Enabled);
            form.ShowSnapshot(snapshot with { Candidates = [] });
            Assert.True(analyze.Enabled);
            Assert.False(approve.Enabled);
        });
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_folder, recursive: true);
    }
}
