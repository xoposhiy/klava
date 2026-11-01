using Klava.Core;

namespace Klava.App;

internal sealed class ChatterHistoryForm : Form
{
    private readonly ChatterHistoryStore _store;
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155 };
    private readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly DataGridView _keys = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false,
    };
    private readonly ChatterChart _chart = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private ChatterDay[]? _lastSnapshot;
    private ChatterDay[] _filtered = [];
    private DateOnly _today;
    private DateTime _now;
    private bool _refreshing;

    public ChatterHistoryForm(ChatterHistoryStore store)
    {
        SuspendLayout();
        _store = store;
        Text = "Klava - Chatter history";
        ClientSize = new Size(1150, 780);
        MinimumSize = new Size(780, 620);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Chatter history", AutoSize = true, Font = new Font(Font.FontFamily, 19, FontStyle.Bold) }, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        filters.Controls.Add(new Label { Text = "Period", AutoSize = true, Padding = new Padding(0, 5, 6, 0) });
        _period.Items.AddRange(["Last 7 days", "Last 30 days", "Last 90 days", "All time", "Today", "Last hour"]);
        _period.SelectedIndex = 1;
        filters.Controls.Add(_period);
        filters.Controls.Add(new Label { Text = "Chart", AutoSize = true, Padding = new Padding(18, 5, 6, 0) });
        _view.Items.AddRange(["Daily history", "Re-press gaps"]);
        _view.SelectedIndex = 0;
        filters.Controls.Add(_view);
        layout.Controls.Add(filters, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "Blocked repeats were stopped. Passed fast repeats were allowed and may be intentional. Holds count once.",
            AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 10),
        }, 0, 2);
        foreach (var name in new[] { "Key", "Blocked bounces", "Passed fast re-presses", "Total presses", "Blocked %", "Fast passed %" })
            _keys.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, SortMode = DataGridViewColumnSortMode.NotSortable });
        _keys.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _keys.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        layout.Controls.Add(_keys, 0, 3);
        layout.Controls.Add(new Label
        {
            Text = "Blue: blocked bounces    Orange: passed fast re-presses",
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 6),
        }, 0, 4);
        layout.Controls.Add(_chart, 0, 5);
        layout.Controls.Add(_status, 0, 6);
        Controls.Add(layout);
        // All constructor sizes use 96-DPI units, including on a scaled primary monitor.
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
        _period.SelectedIndexChanged += (_, _) => RefreshHistory(force: true);
        _view.SelectedIndexChanged += (_, _) => UpdateChart();
        _keys.SelectionChanged += (_, _) => { if (!_refreshing) UpdateChart(); };
        _timer.Tick += (_, _) => RefreshHistory();
        _timer.Start();
        RefreshHistory(force: true);
    }

    private int? SelectedKey => _keys.SelectedRows.Count == 0 ? null : _keys.SelectedRows[0].Tag as int?;

    private void RefreshHistory(bool force = false)
    {
        _status.Text = _store.Status;
        var snapshot = _store.Snapshot;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!force && ReferenceEquals(snapshot, _lastSnapshot) && today == _today && _period.SelectedIndex != 5) return;
        _lastSnapshot = snapshot;
        _today = today;
        _now = DateTime.Now;
        var start = PeriodStart(snapshot);
        _filtered = snapshot.Where(d => d.Date >= start && d.Date <= today
            && (_period.SelectedIndex != 5 || d.Minute >= ChatterHistory.RoundMinute(_now.AddHours(-1)))
            && (d.Minute is null || d.Minute <= _now)).ToArray();
        var selected = SelectedKey;
        _refreshing = true;
        try
        {
            _keys.Rows.Clear();
            AddRow("All keys", _filtered);
            foreach (var key in _filtered.GroupBy(d => d.Vk)
                         .OrderByDescending(g => g.Sum(d => d.BlockedGaps.Sum())).ThenBy(g => KeyNames.Name(g.Key)))
            {
                var row = AddRow(KeyNames.Name(key.Key), key.ToArray());
                row.Tag = key.Key;
            }
            _keys.ClearSelection();
            var selectedRow = _keys.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Equals(r.Tag, selected)) ?? _keys.Rows[0];
            selectedRow.Selected = true;
            _keys.CurrentCell = selectedRow.Cells[0];
        }
        finally { _refreshing = false; }
        UpdateChart();
    }

    private DataGridViewRow AddRow(string name, ChatterDay[] days)
    {
        var total = days.Sum(d => d.TotalPresses);
        var blocked = days.Sum(d => d.BlockedGaps.Sum());
        var passed = days.Sum(d => d.PassedGaps.Sum());
        return _keys.Rows[_keys.Rows.Add(name, blocked, passed, total.ToString("N0"),
            FormatRate(blocked, total), FormatRate(passed, total))];
    }

    internal static string FormatRate(long count, long total)
    {
        if (total == 0) return "N/A";
        var percent = 100.0 * count / total;
        return count > 0 && percent < 0.01 ? $"<{0.01:F2}%" : $"{percent:F2}%";
    }

    private DateOnly PeriodStart(ChatterDay[] days) => _period.SelectedIndex switch
    {
        0 => _today.AddDays(-6),
        1 => _today.AddDays(-29),
        2 => _today.AddDays(-89),
        4 => _today,
        5 => DateOnly.FromDateTime(_now.AddHours(-1)),
        _ => days.Length == 0 ? _today : days.Min(d => d.Date),
    };

    private void UpdateChart()
    {
        var selected = SelectedKey;
        var days = _filtered.Where(d => selected is null || d.Vk == selected).ToArray();
        var keyName = selected is { } vk ? KeyNames.Name(vk) : "All keys";
        if (_view.SelectedIndex == 1)
        {
            var points = Enumerable.Range(0, ChatterHistory.GapBucketCount).Select(i => new ChatterChart.Point(
                $"{i * 10}-{i * 10 + 9}", days.Sum(d => d.BlockedGaps[i]), days.Sum(d => d.PassedGaps[i]),
                days.Sum(d => d.TotalPresses))).ToArray();
            _chart.SetData(points, $"{keyName} - Release-to-press gap (ms)");
            return;
        }
        if (_period.SelectedIndex == 5)
        {
            var minutes = days.GroupBy(d => d.Minute).ToDictionary(g => g.Key!.Value, g => g.ToArray());
            var startMinute = ChatterHistory.RoundMinute(_now.AddHours(-1));
            var points = Enumerable.Range(0, 61).Select(i =>
            {
                var time = startMinute.AddMinutes(i);
                var entries = minutes.GetValueOrDefault(time) ?? [];
                return new ChatterChart.Point(time.ToString("HH:mm"), entries.Sum(d => d.BlockedGaps.Sum()),
                    entries.Sum(d => d.PassedGaps.Sum()), entries.Sum(d => d.TotalPresses));
            }).ToArray();
            _chart.SetData(points, $"{keyName} - Events per minute (last hour, minute precision)");
            return;
        }
        var start = PeriodStart(_lastSnapshot ?? []);
        var count = Math.Max(1, _today.DayNumber - start.DayNumber + 1);
        // Keep bars readable for long histories; each bar then covers several days.
        var step = Math.Max(1, (int)Math.Ceiling(count / 60.0));
        var grouped = days.GroupBy(d => (d.Date.DayNumber - start.DayNumber) / step)
            .ToDictionary(g => g.Key, g => (Blocked: g.Sum(d => d.BlockedGaps.Sum()), Passed: g.Sum(d => d.PassedGaps.Sum()), Total: g.Sum(d => d.TotalPresses)));
        var daily = Enumerable.Range(0, (count + step - 1) / step).Select(i =>
        {
            var totals = grouped.GetValueOrDefault(i);
            return new ChatterChart.Point(start.AddDays(i * step).ToString("dd MMM yy"), totals.Blocked, totals.Passed, totals.Total);
        }).ToArray();
        _chart.SetData(daily, $"{keyName} - Events per {(step == 1 ? "day" : $"{step} days")}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ChatterChart : Control
{
    internal sealed record Point(string Label, long Blocked, long Passed, long Total = 0);
    private Point[] _points = [];
    private string _title = "";
    private readonly ToolTip _tooltip = new();
    private int _hover = -1;

    public ChatterChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        AccessibleName = "Chatter history chart";
    }

    public void SetData(Point[] points, string title)
    {
        _points = points;
        _title = title;
        _hover = -1;
        _tooltip.SetToolTip(this, null);
        Invalidate();
    }

    private int Units(int value) => (int)Math.Ceiling(value * DeviceDpi / 96.0);

    private int LabelWidth => Math.Max(Units(92), _points.Length == 0 ? 0 :
        _points.Max(p => TextRenderer.MeasureText(p.Label, Font).Width) + Units(8));

    private Rectangle Plot
    {
        get
        {
            var max = Math.Max(4, _points.Length == 0 ? 4 : _points.Max(p => Math.Max(p.Blocked, p.Passed)));
            var axisWidth = TextRenderer.MeasureText((Math.Ceiling(max / 4.0) * 4).ToString("N0"), Font).Width;
            var left = Math.Max(Units(65), axisWidth + Units(12));
            var top = Font.Height + Units(14);
            var right = LabelWidth / 2;
            return new Rectangle(left, top, Math.Max(1, Width - left - right),
                Math.Max(1, Height - top - Font.Height - Units(18)));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        TextRenderer.DrawText(g, _title, Font, new Rectangle(0, Units(5), Width, Font.Height),
            Color.FromArgb(35, 45, 60), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (_points.Length == 0 || _points.All(p => p.Blocked == 0 && p.Passed == 0))
        {
            TextRenderer.DrawText(g, "No events in this period.", Font, ClientRectangle, Color.DimGray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var plot = Plot;
        var max = Math.Max(4, _points.Max(p => Math.Max(p.Blocked, p.Passed)));
        var scale = Math.Ceiling(max / 4.0) * 4;
        using var grid = new Pen(Color.FromArgb(230, 234, 240));
        using var blue = new SolidBrush(Color.FromArgb(37, 99, 235));
        using var orange = new SolidBrush(Color.FromArgb(217, 119, 6));
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(g, (scale * i / 4).ToString("N0"), Font,
                new Rectangle(0, y - Font.Height / 2, plot.Left - Units(8), Font.Height), Color.DimGray, TextFormatFlags.Right);
        }
        var width = plot.Width / (float)_points.Length;
        var barWidth = Math.Min(Units(24), width * .35f);
        var labelStep = Math.Max(1, (int)Math.Ceiling(_points.Length * (double)LabelWidth / plot.Width));
        for (var i = 0; i < _points.Length; i++)
        {
            var center = plot.Left + width * (i + .5f);
            var blockedHeight = (float)(_points[i].Blocked / scale * plot.Height);
            var passedHeight = (float)(_points[i].Passed / scale * plot.Height);
            g.FillRectangle(blue, center - barWidth, plot.Bottom - blockedHeight, barWidth, blockedHeight);
            g.FillRectangle(orange, center, plot.Bottom - passedHeight, barWidth, passedHeight);
            if (i % labelStep == 0)
                TextRenderer.DrawText(g, _points[i].Label, Font,
                    new Rectangle((int)center - LabelWidth / 2, plot.Bottom + Units(8), LabelWidth, Font.Height),
                    Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        }
    }

    private static string Rate(long count, long total) => total is > 0
        ? $" ({ChatterHistoryForm.FormatRate(count, total)} of presses)" : " (rate unavailable)";

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = Plot.Contains(e.Location) && _points.Length > 0
            ? Math.Min(_points.Length - 1, (e.X - Plot.Left) * _points.Length / Plot.Width) : -1;
        if (index == _hover) return;
        _hover = index;
        _tooltip.SetToolTip(this, index < 0 ? null :
            $"{_points[index].Label}\nBlocked: {_points[index].Blocked:N0}{Rate(_points[index].Blocked, _points[index].Total)}\nPassed fast re-presses: {_points[index].Passed:N0}{Rate(_points[index].Passed, _points[index].Total)}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltip.Dispose();
        base.Dispose(disposing);
    }
}
