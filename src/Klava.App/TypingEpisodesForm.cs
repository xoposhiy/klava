using Klava.Core;

namespace Klava.App;

internal sealed class TypingEpisodesForm : Form
{
    private readonly TypingEpisodeStore _store;
    private readonly Action _applyDictionary;
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly DataGridView _table = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    };
    private readonly TextBox _patterns = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ListBox _dictionary = new() { Dock = DockStyle.Fill };
    private readonly Label _dictionaryStatus = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly TextBox _newWord = new() { Width = 200, MaxLength = 100 };
    private readonly Label _wordError = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly Button _analyze = new() { Text = "Analyze saved episodes", AutoSize = true };
    private readonly Button _approve = new() { Text = "Approve selected word", AutoSize = true, Enabled = false };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private TypingSnapshot? _shown;

    public TypingEpisodesForm(TypingEpisodeStore store, Action applyDictionary)
    {
        SuspendLayout();
        _store = store;
        _applyDictionary = applyDictionary;
        Text = "Typing episodes";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(650, 450);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var note = new Label
        {
            Text = "Episodes contain local typed text and expire after 7 days. Password fields are excluded when recognized.\n"
                + "Usage counts completed words. Evidence episodes contain correction or layout-visit signals, not confirmed mistakes.\n"
                + "Signals are heuristic evidence. Only approval adds a word; the active keyboard layout stays unchanged.",
            Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(1000, 0), Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(note);
        layout.Controls.Add(_status);
        _table.Columns.Add("word", "English word");
        _table.Columns.Add("russian", "Russian layout");
        _table.Columns.Add("usage", "Uses");
        _table.Columns.Add("errors", "Evidence episodes");
        _table.Columns.Add("signals", "Signals");
        _table.Columns["signals"]!.FillWeight = 250;
        var candidates = new TabPage("Candidates");
        candidates.Controls.Add(_table);
        var dictionary = new TabPage("Dictionary");
        _dictionary.FormattingEnabled = true;
        _dictionary.Format += (_, e) =>
        {
            if (e.ListItem is string word) e.Value = $"{word} ({TypingLayouts.Convert(word, false)})";
        };
        var dictionaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6) };
        dictionaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dictionaryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dictionaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dictionaryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dictionaryLayout.Controls.Add(_dictionaryStatus);
        dictionaryLayout.Controls.Add(_dictionary);
        var dictionaryActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var addWord = new Button { Text = "Add word", AutoSize = true };
        addWord.Click += (_, _) =>
        {
            var word = TypingAnalysis.EnglishCandidate(_newWord.Text);
            if (word.Length == 0) { _wordError.Text = "Enter an English word of 1..100 letters."; return; }
            _store.ApproveWord(word);
            _newWord.Clear();
        };
        _newWord.TextChanged += (_, _) => _wordError.Text = "";
        dictionaryActions.Controls.AddRange([_newWord, addWord, _wordError]);
        dictionaryLayout.Controls.Add(dictionaryActions);
        dictionary.Controls.Add(dictionaryLayout);
        _tabs.TabPages.AddRange([candidates, dictionary]);
        _tabs.SelectedIndexChanged += (_, _) => UpdateApproval();
        _table.SelectionChanged += (_, _) => UpdateApproval();
        layout.Controls.Add(_tabs);
        layout.Controls.Add(_patterns);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _analyze.Click += (_, _) => { _analyze.Enabled = false; _store.AnalyzeNow(); _status.Text = "Analysis requested..."; };
        _approve.Click += (_, _) => Approve();
        buttons.Controls.AddRange([_analyze, _approve]);
        layout.Controls.Add(buttons);
        layout.SizeChanged += (_, _) =>
        {
            var width = Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal);
            note.MaximumSize = new Size(width, 0);
            _status.MaximumSize = new Size(width, 0);
        };
        Controls.Add(layout);
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
        _timer.Tick += (_, _) => { RefreshSnapshot(); _applyDictionary(); };
        _timer.Start();
        RefreshSnapshot();
    }

    private void Approve()
    {
        if (!_approve.Enabled || _table.CurrentRow?.Tag is not TypingCandidate candidate || candidate.Approved) return;
        if (MessageBox.Show(this, $"Automatically correct Russian-layout input for '{candidate.Word}' at word boundaries?",
            "Approve dictionary word", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _store.ApproveWord(candidate.Word);
    }

    private void UpdateApproval() => _approve.Enabled = _tabs.SelectedIndex == 0
        && _table.CurrentRow is { Selected: true, Tag: TypingCandidate { Approved: false } };

    private void RefreshSnapshot() => ShowSnapshot(_store.Snapshot);

    internal void ShowSnapshot(TypingSnapshot snapshot)
    {
        if (ReferenceEquals(snapshot, _shown)) return;
        var previous = _shown;
        _shown = snapshot;
        var newCandidates = snapshot.Candidates.Count(c => !c.Approved);
        _status.Text = $"New candidates: {newCandidates}. Saved episodes: {snapshot.Episodes}. Expiring soon: {snapshot.PendingExpiry}. {snapshot.Status}";
        _analyze.Enabled = !snapshot.IsAnalyzing;
        var candidates = snapshot.Candidates.Where(c => !c.Approved).ToArray();
        if (previous is null || !candidates.SequenceEqual(previous.Candidates.Where(c => !c.Approved)))
        {
            static object? Key(DataGridViewRow row) => (row.Tag as TypingCandidate)?.Word;
            var state = new TableViewState(_table, Key);
            _table.Rows.Clear();
            foreach (var candidate in candidates)
            {
                var index = _table.Rows.Add(candidate.Word, TypingLayouts.Convert(candidate.Word, false),
                    candidate.Usage, candidate.Errors, candidate.Signals);
                _table.Rows[index].Tag = candidate;
            }
            state.Restore(_table, Key);
        }
        var words = snapshot.ApprovedWords.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!_dictionary.Items.Cast<string>().SequenceEqual(words))
        {
            var selectedWord = _dictionary.SelectedItem as string;
            var topIndex = _dictionary.TopIndex;
            var topWord = topIndex >= 0 && topIndex < _dictionary.Items.Count ? _dictionary.Items[topIndex] : null;
            _dictionary.BeginUpdate();
            try
            {
                _dictionary.Items.Clear();
                _dictionary.Items.AddRange(words);
                if (selectedWord is not null) _dictionary.SelectedItem = selectedWord;
                var topWordIndex = topWord is string word ? Array.IndexOf(words, word) : -1;
                if (words.Length > 0) _dictionary.TopIndex = topWordIndex >= 0
                    ? topWordIndex : Math.Clamp(topIndex, 0, words.Length - 1);
            }
            finally { _dictionary.EndUpdate(); }
        }
        _dictionaryStatus.Text = snapshot.ApprovedWords.Count == 0
            ? "The dictionary is empty. Add a word here or approve a candidate."
            : $"Words: {snapshot.ApprovedWords.Count}. Only these words are corrected.";
        var patterns = "Repeated edit patterns (exploratory):" + Environment.NewLine + string.Join(Environment.NewLine, snapshot.Patterns);
        if (_patterns.Text != patterns) _patterns.Text = patterns;
        UpdateApproval();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
