using Klava.Core;
using Microsoft.Win32;

namespace Klava.App;

/// <summary>The config file and the autostart registry value.</summary>
internal static class ConfigStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Klava";

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klava", "config.json");

    /// <summary>Loads the config file and creates it from the default on first run.</summary>
    public static Settings Load()
    {
        if (!File.Exists(FilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, KlavaConfig.DefaultJson);
            Log.Info($"Created {FilePath}");
        }
        return KlavaConfig.Load(File.ReadAllText(FilePath));
    }

    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
