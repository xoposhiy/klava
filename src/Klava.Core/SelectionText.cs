using System.Text;
using System.Text.RegularExpressions;

namespace Klava.Core;

/// <summary>
/// Pure steps of the selected text conversion: the checks of the copied text and the character
/// conversion. See "Selected text" in docs/spec.md.
/// </summary>
public static partial class SelectionText
{
    /// <summary>Formats that editors add when Ctrl+C copies the whole line of an empty selection.</summary>
    public static readonly IReadOnlyList<string> WholeLineFormats =
        ["MSDEVLineSelect", "VisualStudioEditorOperationsLineCutCopyClipboardTag"];

    /// <summary>
    /// Formats that can hold the VS Code editor data. Chromium on Windows puts custom web formats
    /// into one pickled format, so VS Code data can be in either one.
    /// </summary>
    public static readonly IReadOnlyList<string> EditorDataFormats =
        ["vscode-editor-data", "Chromium Web Custom MIME Data Format"];

    /// <summary>Returns why Klava must not convert the copied text, or null to convert it.</summary>
    /// <param name="formats">Names of all formats on the clipboard.</param>
    /// <param name="editorData">The data of <see cref="EditorDataFormats"/> as text, if any.</param>
    public static string? SkipReason(string? text, IReadOnlyCollection<string> formats, string? editorData, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "no text";
        if (text.Length > maxChars) return $"longer than {maxChars} characters";
        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0) return "has a line break";
        if (WholeLineFormats.FirstOrDefault(formats.Contains) is { } format) return $"whole-line copy ({format})";
        if (editorData is not null && EmptySelectionMarker().IsMatch(editorData)) return "whole-line copy (VS Code)";
        return null;
    }

    /// <summary>
    /// Converts each character to the character that the same key and Shift state type in the
    /// target layout. Keeps a character that has no such key or no result.
    /// </summary>
    public static string Convert(string text, nint from, nint to, IPlatform platform)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var typed = platform.FindKey(c, from) is { } key ? platform.TypeText([key], to) : "";
            if (typed.Length > 0) result.Append(typed);
            else result.Append(c);
        }
        return result.ToString();
    }

    [GeneratedRegex("\"isFromEmptySelection\"\\s*:\\s*true")]
    private static partial Regex EmptySelectionMarker();
}
