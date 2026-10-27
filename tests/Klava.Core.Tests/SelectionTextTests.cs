using Klava.Core;

namespace Klava.Core.Tests;

public class SelectionTextTests
{
    private static readonly string[] TextOnly = ["CF_UNICODETEXT"];

    [Fact]
    public void Converts_english_keys_to_russian()
    {
        Assert.Equal("Привет", SelectionText.Convert("Ghbdtn", 1, 2, new FakePlatform()));
    }

    [Fact]
    public void Converts_russian_keys_to_english()
    {
        Assert.Equal("ghbdtn vbh", SelectionText.Convert("привет мир", 2, 1, new FakePlatform()));
    }

    [Fact]
    public void Keeps_characters_with_no_key_in_the_source_layout()
    {
        // '!' has no key in the fake layouts; Cyrillic has no key in the English layout.
        Assert.Equal("привет 42! мир", SelectionText.Convert("ghbdtn 42! мир", 1, 2, new FakePlatform()));
    }

    [Fact]
    public void Accepts_a_short_single_line()
    {
        Assert.Null(SelectionText.SkipReason("ghbdtn", TextOnly, null, 20));
    }

    [Theory]
    [InlineData(null, "no text")]
    [InlineData("", "no text")]
    [InlineData("ghbdtn ghbdtn ghbdtn!", "longer than 20 characters")]
    [InlineData("ghbdtn\r\n", "has a line break")]
    [InlineData("ghbdtn\n", "has a line break")]
    public void Skips_text_that_is_not_a_short_line(string? text, string reason)
    {
        Assert.Equal(reason, SelectionText.SkipReason(text, TextOnly, null, 20));
    }

    [Theory]
    [InlineData("MSDEVLineSelect")]
    [InlineData("VisualStudioEditorOperationsLineCutCopyClipboardTag")]
    public void Skips_a_whole_line_copy_with_a_marker_format(string marker)
    {
        Assert.Equal($"whole-line copy ({marker})", SelectionText.SkipReason("ghbdtn", ["CF_UNICODETEXT", marker], null, 20));
    }

    [Fact]
    public void Skips_a_vs_code_copy_of_an_empty_selection()
    {
        const string data = """{"version":1,"isFromEmptySelection":true,"multicursorText":null,"mode":"plaintext"}""";
        Assert.Equal("whole-line copy (VS Code)", SelectionText.SkipReason("ghbdtn", TextOnly, data, 20));
    }

    [Fact]
    public void Accepts_a_vs_code_copy_of_a_selection()
    {
        const string data = """{"version":1,"isFromEmptySelection":false,"multicursorText":null,"mode":"plaintext"}""";
        Assert.Null(SelectionText.SkipReason("ghbdtn", TextOnly, data, 20));
    }
}
