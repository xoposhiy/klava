using Klava.Core;

namespace Klava.Core.Tests;

public class TranslationTests
{
    [Fact]
    public void Translation_hotkey_fires_once_per_hold_and_passes_physical_modifier_releases()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LCtrl);
        kb.Wait(42).Down(Vk.LAlt);
        Assert.True(kb.Wait(68).Down('T'));
        Assert.True(kb.Wait(500).Down('T'));
        Assert.False(kb.Wait(53).Up(Vk.LAlt));
        Assert.False(kb.Wait(37).Up(Vk.LCtrl));
        Assert.True(kb.Wait(100).Down('T'));
        Assert.True(kb.Wait(70).Up('T'));
        Assert.Single(kb.Platform.Sent.OfType<TranslateSelection>());
        Assert.Contains(new KeyStroke(Vk.LCtrl, true), kb.Platform.Sent);
        Assert.Contains(new KeyStroke(Vk.LAlt, true), kb.Platform.Sent);
        Assert.Equal(0, kb.Platform.Switches);
    }

    [Fact]
    public void AltGr_does_not_trigger_translation()
    {
        var kb = new Keyboard();
        kb.AltGrDown();
        kb.Wait(80).Tap('T');
        kb.Up(Vk.RAlt);
        Assert.Empty(kb.Platform.Sent.OfType<TranslateSelection>());
    }

    [Theory]
    [InlineData("{\"translation\":{\"enabled\":false}}")]
    [InlineData("{\"enabled\":false}")]
    [InlineData("{\"hotkeys\":{\"enabled\":false}}")]
    public void Disabled_translation_passes_the_trigger(string config)
    {
        var kb = new Keyboard(config);
        kb.Down(Vk.LCtrl);
        kb.Wait(60).Down(Vk.LAlt);
        Assert.False(kb.Wait(60).Down('T'));
        Assert.Empty(kb.Platform.Sent.OfType<TranslateSelection>());
    }

    [Fact]
    public void Custom_hotkey_fires_instead_of_the_default()
    {
        var kb = new Keyboard("""{"translation":{"hotkey":"#F12"}}""");
        kb.Down(Vk.LWin);
        Assert.True(kb.Wait(60).Down(0x7B));
        kb.Wait(80).Up(0x7B);
        Assert.False(kb.Wait(30).Up(Vk.LWin));
        Assert.Single(kb.Platform.Sent.OfType<TranslateSelection>());
    }

    [Theory]
    [InlineData("translation", "null")]
    [InlineData("translation", "{\"hotkey\":\"T\"}")]
    [InlineData("translation", "{\"hotkey\":\"^LCtrl\"}")]
    [InlineData("translation", "{\"hotkey\":null}")]
    [InlineData("translation", "{\"hotkey\":\"^Unknown\"}")]
    [InlineData("translation", "{\"deeplKey\":null}")]
    [InlineData("translation", "{\"openaiKey\":null}")]
    [InlineData("translation", "{\"model\":\"\"}")]
    [InlineData("translation", "{\"maxChars\":0}")]
    [InlineData("translation", "{\"maxChars\":5001}")]
    public void Rejects_invalid_translation_config(string section, string value) =>
        Assert.Throws<ConfigException>(() => KlavaConfig.Load($"{{\"{section}\":{value}}}"));

    [Theory]
    [InlineData("word", true)]
    [InlineData("  «слово»  ", true)]
    [InlineData("Wörterbücher", true)]
    [InlineData("don't", true)]
    [InlineData("mother-in-law", true)]
    [InlineData("café", true)]
    [InlineData("word.", true)]
    [InlineData("two words", false)]
    [InlineData("two\nwords", false)]
    [InlineData("one,two", false)]
    [InlineData("123", false)]
    [InlineData("", false)]
    public void Distinguishes_words_from_phrases(string text, bool word) => Assert.Equal(word, TranslationText.IsWord(text));

    [Fact]
    public void Allows_multiline_text_and_counts_Unicode_characters()
    {
        Assert.Null(TranslationText.SelectionError("one\ntwo", 7));
        Assert.Null(TranslationText.SelectionError("я😀", 2));
        Assert.NotNull(TranslationText.SelectionError("я😀a", 2));
        Assert.NotNull(TranslationText.SelectionError(" \n ", 5000));
        Assert.NotNull(TranslationText.SelectionError(null, 5000));
    }

    [Theory]
    [InlineData("EN", "RU")]
    [InlineData("RU", "EN")]
    [InlineData("DE", "RU")]
    public void Defaults_to_the_main_language_pair(string source, string target) =>
        Assert.Equal(target, TranslationText.DefaultTarget(source));
}
