using System.Globalization;

namespace Klava.Core;

/// <summary>Key names in AutoHotkey style, mapped to virtual key codes (US key positions).</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = Vk.Backspace, ["BS"] = Vk.Backspace, ["Tab"] = Vk.Tab, ["Enter"] = Vk.Enter,
        ["Esc"] = Vk.Esc, ["Escape"] = Vk.Esc, ["Space"] = Vk.Space, ["CapsLock"] = Vk.CapsLock,
        ["PgUp"] = 0x21, ["PgDn"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Ins"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
        ["LWin"] = Vk.LWin, ["RWin"] = Vk.RWin, ["AppsKey"] = 0x5D,
        ["LShift"] = Vk.LShift, ["RShift"] = Vk.RShift, ["LCtrl"] = Vk.LCtrl, ["RCtrl"] = Vk.RCtrl,
        ["LAlt"] = Vk.LAlt, ["RAlt"] = Vk.RAlt, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91, ["Pause"] = 0x13,
    };

    private static readonly Dictionary<char, int> ByChar = new()
    {
        [';'] = 0xBA, ['='] = 0xBB, [','] = 0xBC, ['-'] = 0xBD, ['.'] = 0xBE, ['/'] = 0xBF,
        ['`'] = 0xC0, ['['] = 0xDB, ['\\'] = 0xDC, [']'] = 0xDD, ['\''] = 0xDE,
    };

    private static readonly Dictionary<int, string> Names = new();

    static KeyNames()
    {
        for (var i = 1; i <= 24; i++) ByName[$"F{i}"] = 0x70 + i - 1;
        for (var i = 0; i <= 9; i++) ByName[$"Numpad{i}"] = 0x60 + i;
        foreach (var (name, vk) in ByName) Names.TryAdd(vk, name);
        foreach (var (ch, vk) in ByChar) Names[vk] = ch.ToString();
    }

    public static bool TryGetVk(string name, out int vk)
    {
        vk = 0;
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
            return ByChar.TryGetValue(c, out vk);
        }
        if (name.Length is 3 or 4 && name.StartsWith("vk", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 0xFF;
        return ByName.TryGetValue(name, out vk);
    }

    public static string Name(int vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        return Names.TryGetValue(vk, out var name) ? name : $"vk{vk:X2}";
    }
}
