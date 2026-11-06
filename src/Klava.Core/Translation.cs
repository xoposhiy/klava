using System.Text;
using System.Text.RegularExpressions;

namespace Klava.Core;

public sealed record TranslateSelection : OutputStep;

public sealed class TranslationConfig
{
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "^<!t";
    public string DeeplKey { get; set; } = "";
    public string OpenaiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-6-luna";
    public int MaxChars { get; set; } = 5000;
}

public sealed record TranslationEntry(string Text, string PartOfSpeech, string Meaning,
    string Example, string ExampleTranslation);

public sealed record TranslationResult(string Source, string Target, string Headword,
    TranslationEntry[] Entries, string Note);

/// <summary>Text rules shared by selection capture, translation requests, and the result window.</summary>
public static partial class TranslationText
{
    public static readonly IReadOnlyList<string> Languages = ["EN", "RU", "DE"];

    public static string DefaultTarget(string source) => source == "RU" ? "EN" : "RU";

    public static bool IsWord(string text) => Word().IsMatch(text.Trim().Trim('"', '“', '”', '«', '»', '.', ',', '!', '?', ':', ';', '(', ')'));

    public static string? SelectionError(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Select text and try again.";
        if (text.EnumerateRunes().Count() > maxChars) return $"The selection is too long. The limit is {maxChars} characters.";
        return null;
    }

    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}]*(?:['’\-][\p{L}\p{M}]+)*$")]
    private static partial Regex Word();
}
