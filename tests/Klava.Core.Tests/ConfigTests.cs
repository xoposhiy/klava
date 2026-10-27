using Klava.Core;

namespace Klava.Core.Tests;

public class ConfigTests
{
    [Fact]
    public void Default_config_is_valid()
    {
        var settings = KlavaConfig.Load(KlavaConfig.DefaultJson);
        Assert.Equal(30, settings.Config.Debounce.ThresholdMs);
        Assert.Equal(["en-US", "ru-RU"], settings.Config.Switcher.Layouts);
        Assert.True(settings.HotkeyRules.Count > 40);
    }

    [Fact]
    public void Default_config_fixes_altgr_subscripts()
    {
        var settings = KlavaConfig.Load(KlavaConfig.DefaultJson);
        var rule = settings.HotkeyRules.Single(r => r.Triggers.Contains(Hotkey.Parse("<^>!+2")));
        Assert.Equal([new TextStroke("₂")], rule.Action);
    }

    [Theory]
    [InlineData("{ \"debounse\": {} }", "debounse")]
    [InlineData("{ \"debounce\": { \"thresholdMs\": 500 } }", "thresholdMs")]
    [InlineData("{ \"hotkeys\": { \"rules\": [ { \"keys\": [\">!Nope\"], \"text\": \"x\" } ] } }", "Nope")]
    [InlineData("{ \"hotkeys\": { \"rules\": [ { \"keys\": [\">!x\"] } ] } }", "exactly one")]
    [InlineData("{ \"switcher\": { \"switchMethod\": \"Magic\" } }", "Invalid JSON")]
    public void Invalid_config_explains_the_error(string json, string expected)
    {
        var ex = Assert.Throws<ConfigException>(() => KlavaConfig.Load(json));
        Assert.Contains(expected, ex.Message);
    }
}
