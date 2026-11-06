using System.ComponentModel;

namespace Klava.App;

internal static class TranslationColors
{
    public static readonly Color Background = Color.FromArgb(244, 245, 247);
    public static readonly Color Surface = Color.White;
    public static readonly Color Hover = Color.FromArgb(233, 237, 244);
    public static readonly Color Border = Color.FromArgb(207, 214, 226);
    public static readonly Color Text = Color.FromArgb(32, 37, 48);
    public static readonly Color Muted = Color.FromArgb(98, 109, 129);
    public static readonly Color Accent = Color.FromArgb(60, 87, 153);
    public static readonly Color AccentFill = Color.FromArgb(61, 87, 160);
    public static readonly Color Error = Color.FromArgb(177, 52, 48);
}

internal sealed class TranslationButton : Button
{
    public TranslationButton()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = TranslationColors.Border;
        FlatAppearance.MouseOverBackColor = TranslationColors.Hover;
        UseVisualStyleBackColor = false;
        BackColor = TranslationColors.Surface;
        ForeColor = TranslationColors.Text;
        Padding = new Padding(12, 6, 12, 6);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
    }
}

internal sealed class TranslationLanguageButton : ComboBox
{
    private static readonly string[] Codes = ["Auto", "EN", "RU", "DE"];
    private readonly bool _target;
    private bool _refreshing;
    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Value
    {
        get => Codes[Math.Max(0, SelectedIndex)];
        set => SelectedIndex = Array.IndexOf(Codes, value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? DetectedLanguage
    {
        set
        {
            _refreshing = true;
            try { Items[0] = value is null ? AutoName : $"Auto: {LanguageName(value)}"; }
            finally { _refreshing = false; }
        }
    }

    private string AutoName => _target ? "Automatic target" : "Detect language";

    public TranslationLanguageButton(bool target = false)
    {
        _target = target;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = TranslationColors.Surface;
        ForeColor = TranslationColors.Text;
        Margin = Padding.Empty;
        Items.AddRange([AutoName, "English", "Russian", "German"]);
        SelectedIndex = 0;
        SelectedIndexChanged += (_, _) => { if (!_refreshing) ValueChanged?.Invoke(this, EventArgs.Empty); };
    }

    private static string LanguageName(string language) => language switch
    { "EN" => "English", "RU" => "Russian", "DE" => "German", _ => language };
}
