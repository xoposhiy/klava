using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klava.Core;

public enum SwitchMethod { Message, AltShift, CtrlShift, WinSpace }

/// <summary>The config file model (<c>%APPDATA%\Klava\config.json</c>).</summary>
public sealed class KlavaConfig
{
    public bool Enabled { get; set; } = true;
    public DebounceConfig Debounce { get; set; } = new();
    public HotkeysConfig Hotkeys { get; set; } = new();
    public SwitcherConfig Switcher { get; set; } = new();
    public DebugConfig Debug { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The default config file text, with comments.</summary>
    public static string DefaultJson { get; } = ReadDefaultJson();

    /// <summary>Parses and validates config text. Throws <see cref="ConfigException"/> on any error.</summary>
    public static Settings Load(string json)
    {
        KlavaConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<KlavaConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Invalid JSON: {ex.Message}");
        }
        return (config ?? throw new ConfigException("The config is empty.")).Compile();
    }

    private Settings Compile()
    {
        Check(Debounce.ThresholdMs is >= 0 and <= 200, "debounce.thresholdMs must be 0..200.");
        Check(Switcher.MaxTapMs > 0, "switcher.maxTapMs must be positive.");
        Check(Switcher.DoubleTapMs > 0, "switcher.doubleTapMs must be positive.");
        Check(Switcher.MaxSelectionChars is >= 0 and <= 1000, "switcher.maxSelectionChars must be 0..1000.");

        var perKey = new Dictionary<int, int>();
        foreach (var (name, ms) in Debounce.ChatteringKeys)
        {
            Check(KeyNames.TryGetVk(name, out var vk), $"debounce.chatteringKeys: unknown key \"{name}\".");
            Check(ms is >= 0 and <= 200, $"debounce.chatteringKeys.{name} must be 0..200.");
            perKey[vk] = ms;
        }

        var traceKeys = new HashSet<int>();
        foreach (var name in Debug.TraceKeys)
        {
            Check(KeyNames.TryGetVk(name, out var vk), $"debug.traceKeys: unknown key \"{name}\".");
            traceKeys.Add(vk);
        }

        var rules = new List<HotkeyRule>();
        foreach (var rule in Hotkeys.Rules)
        {
            var label = string.Join(", ", rule.Keys);
            Check(rule.Keys.Count > 0, "hotkeys.rules: a rule has no keys.");
            Check((rule.Text is null) != (rule.Send is null), $"Hotkey [{label}]: set exactly one of \"text\" and \"send\".");
            try
            {
                var triggers = rule.Keys.Select(Hotkey.Parse).ToList();
                var action = rule.Text is not null ? [new TextStroke(rule.Text)] : SendParser.Parse(rule.Send!);
                rules.Add(new HotkeyRule(triggers, action));
            }
            catch (FormatException ex)
            {
                throw new ConfigException(ex.Message);
            }
        }

        return new Settings(this, perKey, rules, traceKeys);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new ConfigException(message);
    }

    private static string ReadDefaultJson()
    {
        using var stream = typeof(KlavaConfig).Assembly.GetManifestResourceStream("Klava.Core.default-config.json")
            ?? throw new InvalidOperationException("The default config resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public sealed class DebounceConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Threshold for all keys.</summary>
    public int ThresholdMs { get; set; } = 30;

    /// <summary>Keys that chatter more than the threshold allows, with their own threshold: <c>"M": 50</c>.</summary>
    public Dictionary<string, int> ChatteringKeys { get; set; } = [];
}

public sealed class HotkeysConfig
{
    public bool Enabled { get; set; } = true;
    public List<HotkeyRuleConfig> Rules { get; set; } = [];
}

public sealed class HotkeyRuleConfig
{
    public List<string> Keys { get; set; } = [];
    public string? Text { get; set; }
    public string? Send { get; set; }
}

public sealed class SwitcherConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxTapMs { get; set; } = 500;
    public int DoubleTapMs { get; set; } = 400;

    /// <summary>Longest selected text that a double tap converts. 0 turns the selected text conversion off.</summary>
    public int MaxSelectionChars { get; set; } = 20;

    /// <summary>Layouts to cycle by language tag. Empty: all installed layouts.</summary>
    public List<string> Layouts { get; set; } = ["en-US", "ru-RU"];

    public SwitchMethod SwitchMethod { get; set; } = SwitchMethod.Message;
}

public sealed class DebugConfig
{
    /// <summary>Keys to trace in the log with their neighbor keys and pauses. Logs fragments of typed text.</summary>
    public List<string> TraceKeys { get; set; } = [];
}

/// <summary>A validated config, ready for the engine.</summary>
public sealed record Settings(
    KlavaConfig Config,
    IReadOnlyDictionary<int, int> DebounceKeyThresholds,
    IReadOnlyList<HotkeyRule> HotkeyRules,
    IReadOnlySet<int> TraceKeys);

public sealed class ConfigException(string message) : Exception(message);
