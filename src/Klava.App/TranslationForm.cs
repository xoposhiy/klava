using System.Diagnostics;
using System.Runtime.InteropServices;
using Klava.Core;

namespace Klava.App;

internal sealed class TranslationForm : Form
{
    private const uint EmScroll = 0x00B5;
    private const int SbTop = 6;
    private readonly string _text;
    private readonly Func<bool, ITranslationClient> _client;
    private readonly TranslationLanguageButton _source = new() { AccessibleName = "Source language" };
    private readonly TranslationLanguageButton _target = new(target: true) { AccessibleName = "Target language" };
    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None,
        BackColor = TranslationColors.Surface, ForeColor = TranslationColors.Text, AccessibleName = "Translation",
        ScrollBars = RichTextBoxScrollBars.Vertical,
    };
    private readonly TextBox _selection;
    private readonly Label _status, _resultTitle, _provider, _note;
    private readonly TranslationButton _copy = new() { Text = "Copy", AccessibleName = "Copy result", Enabled = false };
    private readonly TranslationButton _details = new() { Text = "AI details", BackColor = TranslationColors.AccentFill, ForeColor = Color.White, AccessibleName = "More details (AI)" };
    private readonly TranslationButton _retry = new() { Text = "Retry", AccessibleName = "Translate again with DeepL", Padding = new Padding(8, 3, 8, 3) };
    private readonly Label _skeleton = new() { Dock = DockStyle.Fill, Text = "Translating...", ForeColor = TranslationColors.Muted, BackColor = TranslationColors.Surface };
    private readonly System.Windows.Forms.Timer _copyFeedback = new() { Interval = 1800 };
    private readonly ToolTip _tips = new() { AutoPopDelay = 10000 };
    private readonly List<Font> _fonts = [];
    private readonly List<(Label Label, float Size, FontStyle Style)> _labelStyles = [];
    private CancellationTokenSource? _pending;
    private int _generation, _paragraphSpacing = 1;
    private bool _ready, _hasResult;
    private TranslationResult? _deepL;

    public TranslationForm(string text, Func<bool, ITranslationClient> client, string? error = null)
    {
        SuspendLayout();
        _text = text;
        _client = client;
        Text = "Klava translation";
        Font = OwnFont(10.5f);
        _output.Font = Typography(11);
        _details.FlatAppearance.BorderColor = TranslationColors.AccentFill;
        _details.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 101, 177);
        _details.FlatAppearance.MouseDownBackColor = Color.FromArgb(48, 70, 136);
        BackColor = TranslationColors.Background;
        ForeColor = TranslationColors.Text;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        ClientSize = new Size(540, 410);
        MinimumSize = new Size(440, 380);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));

        var languages = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 36, ColumnCount = 3, Margin = new Padding(0, 0, 0, 16) };
        languages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        languages.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        languages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _source.Anchor = _target.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var arrow = MakeLabel("→", 16, TranslationColors.Muted);
        arrow.TextAlign = ContentAlignment.MiddleCenter;
        languages.Controls.Add(_source, 0, 0); languages.Controls.Add(arrow, 1, 0); languages.Controls.Add(_target, 2, 0);
        _tips.SetToolTip(_source, "Source language · Auto detects English, Russian, or German");
        _tips.SetToolTip(_target, "Target language · Auto uses Russian, or English for Russian text");

        var sourceTitle = MakeLabel("Selected text", 9, TranslationColors.Muted);
        sourceTitle.AutoSize = true;
        sourceTitle.Margin = new Padding(2, 0, 0, 6);
        _selection = new TextBox
        {
            Text = text, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill, Height = 42, BackColor = BackColor, ForeColor = TranslationColors.Muted,
            AccessibleName = "Selected text", ScrollBars = ScrollBars.None, Margin = new Padding(2, 0, 2, 17),
        };

        var card = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = TranslationColors.Surface, Padding = new Padding(16) };
        var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = TranslationColors.Surface, Margin = Padding.Empty };
        resultLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var resultHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, BackColor = TranslationColors.Surface, Margin = new Padding(0, 0, 0, 12) };
        resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _resultTitle = MakeLabel("Translation", 9, TranslationColors.Muted);
        _resultTitle.TextAlign = ContentAlignment.MiddleLeft;
        _provider = MakeLabel("DeepL", 9, TranslationColors.Accent);
        _provider.AutoSize = true;
        _provider.Anchor = AnchorStyles.Right;
        _provider.Margin = new Padding(0, 0, 10, 0);
        resultHeader.Controls.Add(_resultTitle, 0, 0); resultHeader.Controls.Add(_provider, 1, 0); resultHeader.Controls.Add(_retry, 2, 0);
        var resultBody = new Panel { Dock = DockStyle.Fill, BackColor = TranslationColors.Surface, Margin = Padding.Empty };
        resultBody.Controls.Add(_output); resultBody.Controls.Add(_skeleton);
        _skeleton.BringToFront();
        _note = MakeLabel("", 9, TranslationColors.Muted);
        _note.AutoSize = true;
        _note.Visible = false;
        _note.Margin = new Padding(0, 12, 0, 0);
        resultLayout.Controls.Add(resultHeader, 0, 0); resultLayout.Controls.Add(resultBody, 0, 1); resultLayout.Controls.Add(_note, 0, 2);
        card.Controls.Add(resultLayout);
        _tips.SetToolTip(_retry, "Translate again with DeepL");

        _status = MakeLabel("", 9, TranslationColors.Error);
        _status.AccessibleName = "Translation status";
        _status.AutoSize = true;
        _status.Visible = false;
        _status.Margin = new Padding(2, 10, 2, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 16, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var settings = new TranslationButton { Text = "Settings", AccessibleName = "Open settings", Margin = new Padding(8, 0, 0, 0) };
        settings.Click += (_, _) => OpenSettings();
        _copy.Anchor = AnchorStyles.Right;
        footer.Controls.Add(_details, 0, 0); footer.Controls.Add(settings, 1, 0); footer.Controls.Add(_copy, 3, 0);
        _tips.SetToolTip(_details, "Word senses, examples, and alternative translations · OpenAI · Ctrl+Enter");
        _tips.SetToolTip(_copy, "Copy result · Ctrl+C");
        _details.Click += async (_, _) => await TranslateAsync(true);
        _retry.Click += async (_, _) => await TranslateAsync(false);
        _copy.Click += (_, _) => CopyResult();

        layout.Controls.Add(languages, 0, 0); layout.Controls.Add(sourceTitle, 0, 1);
        layout.Controls.Add(_selection, 0, 2); layout.Controls.Add(card, 0, 3);
        layout.Controls.Add(_status, 0, 4); layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);
        _source.ValueChanged += LanguageChanged; _target.ValueChanged += LanguageChanged;
        Resize += (_, _) => UpdateTextWidths();
        _copyFeedback.Tick += (_, _) => { _copy.Text = "Copy"; _copyFeedback.Stop(); };
        Shown += async (_, _) =>
        {
            UpdateTextWidths();
            _selection.Select(0, 0);
            _ready = true;
            _details.Focus();
            if (error is not null) ShowError(error); else await TranslateAsync(false);
        };
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(performLayout: true);
        UpdateTextWidths();
    }

    private Font OwnFont(float size, FontStyle style = FontStyle.Regular)
    {
        var existing = _fonts.FirstOrDefault(font => Math.Abs(font.Size - size) < .01 && font.Style == style);
        if (existing is not null) return existing;
        var font = new Font("Segoe UI", size, style); _fonts.Add(font); return font;
    }

    private Font Typography(float size, FontStyle style = FontStyle.Regular) => OwnFont(size * Font.Size / 10.5f, style);

    private Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        var label = new Label { Text = text, Font = Typography(size, style), ForeColor = color, Dock = DockStyle.Fill, Margin = Padding.Empty };
        _labelStyles.Add((label, size, style));
        return label;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_labelStyles is null || Controls.Count == 0) return;
        foreach (var (label, size, style) in _labelStyles) label.Font = Typography(size, style);
        _output.Font = Typography(11);
        UpdateTextWidths();
    }

    private void UpdateTextWidths()
    {
        if (_status is null || _note is null || _selection is null) return;
        var width = Math.Max(120, ClientSize.Width - Padding.Horizontal - 4);
        _status.MaximumSize = new Size(width, 0);
        _note.MaximumSize = new Size(Math.Max(100, width - 40 * DeviceDpi / 96), 0);
        var measured = TextRenderer.MeasureText(_text, _selection.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var line = _selection.Font.Height;
        _selection.Height = Math.Clamp(measured.Height + 4, line + 4, line * 3 + 4);
        _selection.ScrollBars = measured.Height > _selection.Height ? ScrollBars.Vertical : ScrollBars.None;
        Invalidate();
    }

    private async void LanguageChanged(object? sender, EventArgs e)
    {
        if (!_ready || _text.Length == 0) return;
        _deepL = null;
        _source.DetectedLanguage = null; _target.DetectedLanguage = null;
        await TranslateAsync(false);
    }

    internal async Task TranslateAsync(bool details)
    {
        _pending?.Cancel(); _pending?.Dispose();
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(details ? 40 : 25));
        _pending = pending;
        var generation = ++_generation;
        var provider = details ? "AI (OpenAI)" : "DeepL";
        try
        {
            if (string.IsNullOrWhiteSpace(_text)) throw new TranslationException("Select text and try again.");
            var client = _client(details);
            var source = _source.Value; var target = _target.Value;
            if (details && _deepL is { } previous)
            {
                if (source == "Auto") source = previous.Source;
                if (target == "Auto") target = previous.Target;
            }
            if (source == target && source != "Auto") throw new TranslationException("Choose a different target language.");
            _status.Visible = false;
            _provider.Text = details ? "OpenAI" : "DeepL";
            _resultTitle.Text = "Translating...";
            _details.Enabled = !details;
            if (!details) { _output.Clear(); _hasResult = false; _copy.Enabled = false; _note.Visible = false; }
            _skeleton.Visible = !_hasResult;
            var result = await client.TranslateAsync(_text, source, target, pending.Token);
            if (IsDisposed || generation != _generation) return;
            if (!details) _deepL = result;
            _source.DetectedLanguage = result.Source; _target.DetectedLanguage = result.Target;
            Render(result, details);
            Log.Info($"Translation: completed with {provider} ({_text.Length} characters)");
        }
        catch (Exception ex)
        {
            if (IsDisposed || generation != _generation) return;
            var message = ex switch
            {
                TranslationException => ex.Message,
                OperationCanceledException => $"{provider} timed out. Try again.",
                System.Net.Http.HttpRequestException => $"Cannot reach {provider}. Check the connection and try again.",
                _ => "Cannot translate the selection. Try again.",
            };
            ShowError(message);
            // Exception messages and response bodies can contain selected text or credentials.
            Log.Warn($"Translation: failed with {provider} ({ex.GetType().Name})");
        }
        finally
        {
            if (generation == _generation && !IsDisposed) { _pending = null; _details.Enabled = true; }
        }
    }

    private void ShowError(string message)
    {
        _skeleton.Visible = false;
        var setup = message.StartsWith("Add translation.", StringComparison.Ordinal);
        var service = message.Contains("openaiKey", StringComparison.Ordinal) ? "OpenAI" : "DeepL";
        var description = setup ? $"Add a {service} API key in settings, then reload the config from the tray." : message;
        _status.Text = description; _status.Visible = _hasResult;
        _tips.SetToolTip(_status, message);
        if (!_hasResult)
        {
            _resultTitle.Text = setup ? "Setup required" : "Translation failed";
            _output.Clear();
            Append(setup ? $"Connect {service}" : "Translation unavailable", Typography(15, FontStyle.Bold), TranslationColors.Text);
            Append(description, Typography(9), TranslationColors.Muted);
            _tips.SetToolTip(_output, message);
        }
        else _resultTitle.Text = "Translation";
        _copy.Enabled = _hasResult;
    }

    private void CopyResult()
    {
        if (!_copy.Enabled) return;
        try
        {
            Clipboard.SetText(_output.Text);
            _copy.Text = "Copied";
            _copyFeedback.Stop(); _copyFeedback.Start();
        }
        catch (ExternalException) { ShowError("The clipboard is busy. Try copying again."); }
    }

    private void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true }); Close(); }
        catch (Exception) { ShowError("Cannot open settings. Open the config from the Klava tray menu."); }
    }

    private void Append(string text, Font font, Color color, int spacing = 1)
    {
        if (string.IsNullOrEmpty(text)) return;
        _output.SelectionStart = _output.TextLength; _output.SelectionLength = 0;
        _output.SelectionFont = Typography(9);
        if (_output.TextLength > 0) _output.AppendText(new string('\n', _paragraphSpacing));
        _output.SelectionFont = font; _output.SelectionColor = color;
        _output.AppendText(text);
        _paragraphSpacing = spacing;
    }

    private void Render(TranslationResult result, bool details)
    {
        _tips.SetToolTip(_output, null);
        _tips.SetToolTip(_status, null);
        _skeleton.Visible = false; _output.Clear();
        _resultTitle.Text = details ? TranslationText.IsWord(_text) ? "Word meanings" : "Alternative translations" : "Translation";
        if (result.Headword.Length > 0) Append(result.Headword, Typography(18, FontStyle.Bold), TranslationColors.Text, 2);
        foreach (var entry in result.Entries)
        {
            if (entry.PartOfSpeech.Length > 0) Append(entry.PartOfSpeech.ToUpperInvariant(), Typography(9), TranslationColors.Accent);
            Append(entry.Text, details ? Typography(14, FontStyle.Bold) : Typography(TranslationText.IsWord(_text) ? 20 : 15), TranslationColors.Text);
            Append(entry.Meaning, Typography(11), TranslationColors.Muted);
            if (entry.Example.Length > 0) Append(entry.Example + "\n" + entry.ExampleTranslation, Typography(11), TranslationColors.Muted);
            if (details) _paragraphSpacing = 2;
        }
        Append(result.Note, Typography(11), TranslationColors.Muted);
        _note.Text = "AI-generated · Some meanings may be missing."; _note.Visible = details;
        _hasResult = result.Entries.Length > 0; _copy.Enabled = _hasResult;
        _output.SelectionStart = 0; _output.SelectionLength = 0; _output.ScrollToCaret();
        SendMessage(_output.Handle, EmScroll, SbTop, 0);
        var desiredHeight = details ? 620 : result.Entries.Any(entry => entry.Text.Length > 140 || entry.Text.Contains('\n')) ? 560 : 410;
        if (Height < desiredHeight * DeviceDpi / 96)
        {
            var area = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(desiredHeight * DeviceDpi / 96, area.Height - 32);
            if (Left > -10000) Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == (Keys.Control | Keys.Enter)) { if (_details.Enabled) _ = TranslateAsync(true); return true; }
        if (keyData == (Keys.Control | Keys.C) && !(_output.Focused && _output.SelectionLength > 0)
            && !(_selection.Focused && _selection.SelectionLength > 0) && _copy.Enabled)
        { CopyResult(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ++_generation; _pending?.Cancel(); _pending = null;
            _copyFeedback.Dispose(); _tips.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) foreach (var font in _fonts) font.Dispose();
    }
}
