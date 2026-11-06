using Klava.Core;

namespace Klava.Core.Tests;

public class ConfigSafetyTests
{
    [Theory]
    [InlineData("{\"debounce\":null}", "debounce")]
    [InlineData("{\"hotkeys\":null}", "hotkeys")]
    [InlineData("{\"switcher\":null}", "switcher")]
    [InlineData("{\"debug\":null}", "debug")]
    [InlineData("{\"debounce\":{\"chatteringKeys\":null}}", "chatteringKeys")]
    [InlineData("{\"hotkeys\":{\"rules\":null}}", "rules")]
    [InlineData("{\"hotkeys\":{\"rules\":[null]}}", "rule objects")]
    [InlineData("{\"hotkeys\":{\"rules\":[{\"keys\":null,\"text\":\"x\"}]}}", "keys")]
    [InlineData("{\"hotkeys\":{\"rules\":[{\"keys\":[null],\"text\":\"x\"}]}}", "key names")]
    [InlineData("{\"hotkeys\":{\"rules\":[{\"keys\":[\" \"],\"text\":\"x\"}]}}", "key names")]
    [InlineData("{\"switcher\":{\"layouts\":null}}", "layouts")]
    [InlineData("{\"switcher\":{\"layouts\":[null]}}", "language tags")]
    [InlineData("{\"switcher\":{\"layouts\":[\" \" ]}}", "language tags")]
    [InlineData("{\"switcher\":{\"switchMethod\":99}}", "switchMethod")]
    [InlineData("{\"debug\":{\"traceKeys\":null}}", "traceKeys")]
    [InlineData("{\"debug\":{\"traceKeys\":[null]}}", "key names")]
    [InlineData("{\"debug\":{\"traceKeys\":[\"\"]}}", "key names")]
    public void Rejects_invalid_structure_with_a_config_error(string json, string expected)
    {
        var error = Assert.Throws<ConfigException>(() => KlavaConfig.Load(json));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("{U+}")]
    [InlineData("{U+nope}")]
    [InlineData("{U+110000}")]
    [InlineData("{U+D800}")]
    [InlineData("{U+FFFFFFFF}")]
    [InlineData("{U+100000000}")]
    [InlineData("{")]
    [InlineData("abc{")]
    public void Rejects_invalid_send_text_with_a_config_error(string send)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            hotkeys = new { rules = new[] { new { keys = new[] { ">!x" }, send } } },
        });
        Assert.Throws<ConfigException>(() => KlavaConfig.Load(json));
    }

    [Fact]
    public void Runs_a_supplementary_unicode_hotkey_with_optional_empty_lists()
    {
        var kb = new Keyboard("""
            { "hotkeys": { "rules": [{ "keys": [">!x"], "send": "{U+1F642}" }] },
              "switcher": { "layouts": [] }, "debug": { "traceKeys": [] } }
            """);
        kb.Down(Vk.RAlt);
        kb.Wait(44).Tap('X', holdMs: 80, afterMs: 63);
        kb.Up(Vk.RAlt);
        Assert.Equal("🙂", kb.Platform.SentText);
    }
}
