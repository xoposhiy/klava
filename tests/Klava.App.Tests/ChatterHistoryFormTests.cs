using Klava.App;

namespace Klava.App.Tests;

public sealed class ChatterHistoryFormTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-form-tests-" + Guid.NewGuid());

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Keeps_text_inside_layout_rows_with_larger_fonts(float scale)
    {
        Directory.CreateDirectory(_folder);
        using var store = new ChatterHistoryStore(Path.Combine(_folder, "history.json"));
        FormTestKit.Run(() =>
        {
            using var form = new ChatterHistoryForm(store);
            FormTestKit.Preview(form, "layout-initial");
            var layout = form.Controls.OfType<TableLayoutPanel>().Single();
            var title = (Label)layout.GetControlFromPosition(0, 0)!;
            form.Scale(new SizeF(scale, scale));
            form.Font = new Font(form.Font.FontFamily, 10 * scale);
            title.Font = new Font(title.Font.FontFamily, 19 * scale, FontStyle.Bold);
            form.ClientSize = new Size((int)(780 * scale), (int)(620 * scale));
            FormTestKit.Preview(form, $"layout-{scale:0.0}");

            foreach (var label in layout.Controls.OfType<Label>())
            {
                var preferred = label.GetPreferredSize(new Size(label.Width, 0));
                Assert.True(label.Height >= preferred.Height, $"Clipped label: {label.Text}");
            }
            var rows = layout.Controls.Cast<Control>().OrderBy(c => layout.GetRow(c)).ToArray();
            for (var i = 1; i < rows.Length; i++)
                Assert.True(rows[i - 1].Bottom <= rows[i].Top, "Layout rows overlap.");
            var filters = layout.Controls.OfType<FlowLayoutPanel>().Single();
            foreach (Control control in filters.Controls)
                Assert.True(filters.ClientRectangle.Contains(control.Bounds), "A filter is clipped.");
            var grid = FormTestKit.Descendants(form).OfType<DataGridView>().Single();
            Assert.True(grid.Rows[0].Height >= grid.Font.Height, "Table text is clipped.");
        });
    }

    [Fact]
    public void Filters_saved_history_by_period_and_renders_both_charts()
    {
        Directory.CreateDirectory(_folder);
        var today = DateTime.Today;
        var history = new Klava.Core.ChatterHistory();
        history.RecordPress(today.AddDays(-1), 'M');
        history.Record(today.AddDays(-1), 'M', 68, true);
        history.RecordPress(today.AddDays(-2), 'M');
        history.Record(today.AddDays(-2), 'M', 76, false);
        history.RecordPress(today.AddDays(-10), Klava.Core.Vk.LShift);
        history.Record(today.AddDays(-10), Klava.Core.Vk.LShift, 2, true);
        var path = Path.Combine(_folder, "history.json");
        FormTestKit.SaveHistory(path, history);
        using var store = new ChatterHistoryStore(path);
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Length == 3, TimeSpan.FromSeconds(5)));

        FormTestKit.Run(() =>
        {
            using var form = new ChatterHistoryForm(store);
            var grid = FormTestKit.Descendants(form).OfType<DataGridView>().Single();
            var selectors = FormTestKit.Descendants(form).OfType<ComboBox>().ToArray();
            Assert.Equal(3, grid.Rows.Count);
            Assert.Equal(2L, grid.Rows[0].Cells[1].Value);
            Assert.Equal(1L, grid.Rows[0].Cells[2].Value);
            FormTestKit.Preview(form, "history-daily");

            selectors[0].SelectedIndex = 0;
            Assert.Equal(2, grid.Rows.Count);
            Assert.Equal(1L, grid.Rows[0].Cells[1].Value);
            grid.CurrentCell = grid.Rows[1].Cells[0];
            selectors[1].SelectedIndex = 1;
            FormTestKit.Preview(form, "history-gaps");

            form.ClientSize = new Size(780, 620);
            FormTestKit.Preview(form, "history-small");
            selectors[0].SelectedIndex = 3;
            Assert.Equal(3, grid.Rows.Count);
            Assert.Equal((int)'M', grid.SelectedRows[0].Tag);
        });
    }

    [Fact]
    public void Shows_matching_totals_and_rates_for_recent_periods()
    {
        Directory.CreateDirectory(_folder);
        var now = DateTime.Now;
        var history = new Klava.Core.ChatterHistory();
        history.RecordPress(now, 'M');
        history.RecordPress(now, 'M');
        history.Record(now, 'M', 2, true);
        history.RecordPress(now.AddHours(-2), 'N');
        var path = Path.Combine(_folder, "history.json");
        FormTestKit.SaveHistory(path, history);
        using var store = new ChatterHistoryStore(path);
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Length == 2, TimeSpan.FromSeconds(5)));
        FormTestKit.Run(() =>
        {
            using var form = new ChatterHistoryForm(store);
            var grid = FormTestKit.Descendants(form).OfType<DataGridView>().Single();
            var period = FormTestKit.Descendants(form).OfType<ComboBox>().First();
            Assert.Equal("3", grid.Rows[0].Cells[3].Value);
            Assert.Equal(1L, grid.Rows[0].Cells[1].Value);
            Assert.Equal($"{100.0 / 3:F2}%", grid.Rows[0].Cells[4].Value);
            period.SelectedItem = "Today";
            Assert.True(grid.Rows.Count >= 2);
            period.SelectedItem = "Last hour";
            Assert.Equal(2, grid.Rows.Count);
            Assert.Equal("2", grid.Rows[0].Cells[3].Value);
            Assert.Equal(1L, grid.Rows[0].Cells[1].Value);
            Assert.Equal($"{50.0:F2}%", grid.Rows[0].Cells[4].Value);
            FormTestKit.Preview(form, "history-hour");
        });
    }

    [Fact]
    public void Shows_passed_fast_represses_with_zero_blocked_represses()
    {
        Directory.CreateDirectory(_folder);
        var now = DateTime.Now;
        var history = new Klava.Core.ChatterHistory();
        for (var i = 0; i < 66; i++) history.RecordPress(now, 'M');
        for (var i = 0; i < 10; i++) history.Record(now, 'M', 68, false);
        var path = Path.Combine(_folder, "history.json");
        FormTestKit.SaveHistory(path, history);
        using var store = new ChatterHistoryStore(path);
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Length == 1, TimeSpan.FromSeconds(5)));
        FormTestKit.Run(() =>
        {
            using var form = new ChatterHistoryForm(store);
            var grid = FormTestKit.Descendants(form).OfType<DataGridView>().Single();
            Assert.Empty(FormTestKit.Descendants(form).OfType<CheckBox>());
            Assert.Equal("66", grid.Rows[0].Cells[3].Value);
            Assert.Equal(0L, grid.Rows[0].Cells[1].Value);
            Assert.Equal($"{0.0:F2}%", grid.Rows[0].Cells[4].Value);
            Assert.Equal(10L, grid.Rows[0].Cells[2].Value);
            Assert.Equal($"{1000.0 / 66:F2}%", grid.Rows[0].Cells[5].Value);
            FormTestKit.Preview(form, "history-consistent-rates");
        });
    }

    [Fact]
    public void Refresh_keeps_the_selected_key_column_and_scroll_position()
    {
        Directory.CreateDirectory(_folder);
        var history = new Klava.Core.ChatterHistory();
        for (var key = 33; key < 130; key++) history.RecordPress(DateTime.Now, key);
        var path = Path.Combine(_folder, "history.json");
        FormTestKit.SaveHistory(path, history);
        using var store = new ChatterHistoryStore(path);
        Assert.True(SpinWait.SpinUntil(() => store.Snapshot.Length >= 97, TimeSpan.FromSeconds(5)));
        FormTestKit.Run(() =>
        {
            using var form = new ChatterHistoryForm(store);
            FormTestKit.Preview(form, "history-refresh");
            var table = Assert.Single(FormTestKit.Descendants(form).OfType<DataGridView>());
            table.CurrentCell = table.Rows[30].Cells[2];
            var selected = table.CurrentRow!.Tag;
            table.FirstDisplayedScrollingRowIndex = 10;
            var top = table.Rows[10].Tag;
            form.RefreshHistory(force: true);
            Assert.Equal(selected, table.CurrentRow!.Tag);
            Assert.Equal(2, table.CurrentCell!.ColumnIndex);
            Assert.Equal(top, table.Rows[table.FirstDisplayedScrollingRowIndex].Tag);
        });
    }

    [Fact]
    public void Does_not_round_a_positive_rate_to_zero()
    {
        Assert.Equal($"<{0.01:F2}%", ChatterHistoryForm.FormatRate(1, 100_000));
        Assert.Equal($"{0.0:F2}%", ChatterHistoryForm.FormatRate(0, 100_000));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
