using System.Text;

namespace Klava.Core;

/// <summary>Which side of a modifier a hotkey requires.</summary>
public enum Side { None, Either, Left, Right }

/// <summary>A hotkey trigger in AutoHotkey syntax, for example <c>&gt;!+,</c> or <c>#^Right</c>.</summary>
public sealed record Hotkey(Side Ctrl, Side Alt, Side Shift, Side Win, int Vk)
{
    public static Hotkey Parse(string text)
    {
        Side ctrl = Side.None, alt = Side.None, shift = Side.None, win = Side.None;
        Side? pendingSide = null;
        var i = 0;
        for (; i < text.Length - 1; i++)
        {
            var c = text[i];
            if (c is '<' or '>')
            {
                pendingSide = c == '<' ? Side.Left : Side.Right;
                continue;
            }
            var side = pendingSide ?? Side.Either;
            if (c == '#') win = side;
            else if (c == '^') ctrl = side;
            else if (c == '!') alt = side;
            else if (c == '+') shift = side;
            else break;
            pendingSide = null;
        }

        var keyName = text[i..];
        if (pendingSide is not null || keyName.Length == 0)
            throw new FormatException($"Hotkey \"{text}\": '<' or '>' must come before a modifier.");
        if (!KeyNames.TryGetVk(keyName, out var vk))
            throw new FormatException($"Hotkey \"{text}\": unknown key \"{keyName}\".");
        return new Hotkey(ctrl, alt, shift, win, vk);
    }

    /// <summary>Exact match: required modifiers are down, modifiers that are not required are up.</summary>
    public bool Matches(KeyState keys) =>
        GroupMatches(Ctrl, keys.IsDown(Core.Vk.LCtrl), keys.IsDown(Core.Vk.RCtrl))
        && GroupMatches(Alt, keys.IsDown(Core.Vk.LAlt), keys.IsDown(Core.Vk.RAlt))
        && GroupMatches(Shift, keys.IsDown(Core.Vk.LShift), keys.IsDown(Core.Vk.RShift))
        && GroupMatches(Win, keys.IsDown(Core.Vk.LWin), keys.IsDown(Core.Vk.RWin));

    /// <summary>Readable form for the log, for example <c>RAlt+Shift+,</c>.</summary>
    public override string ToString()
    {
        static string Part(Side side, string name) => side switch
        {
            Side.None => "",
            Side.Left => $"L{name}+",
            Side.Right => $"R{name}+",
            _ => $"{name}+",
        };
        return Part(Win, "Win") + Part(Ctrl, "Ctrl") + Part(Alt, "Alt") + Part(Shift, "Shift") + KeyNames.Name(Vk);
    }

    private static bool GroupMatches(Side required, bool left, bool right) => required switch
    {
        Side.None => !left && !right,
        Side.Either => left || right,
        Side.Left => left,
        _ => right,
    };
}

/// <summary>A hotkey rule: any of the triggers sends the action.</summary>
public sealed record HotkeyRule(IReadOnlyList<Hotkey> Triggers, IReadOnlyList<OutputStep> Action);

/// <summary>Parses AutoHotkey-like send strings: <c>^{End}</c>, <c>{U+2715}</c>, <c>abc</c>.</summary>
public static class SendParser
{
    public static List<OutputStep> Parse(string text)
    {
        var steps = new List<OutputStep>();
        var mods = new List<int>();
        var pendingText = new StringBuilder();

        void FlushText()
        {
            if (pendingText.Length == 0) return;
            steps.Add(new TextStroke(pendingText.ToString()));
            pendingText.Clear();
        }

        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c is '^' or '!' or '+' or '#' && i + 1 < text.Length)
            {
                mods.Add(c switch { '^' => Vk.LCtrl, '!' => Vk.LAlt, '+' => Vk.LShift, _ => Vk.LWin });
                i++;
                continue;
            }

            string keyName;
            if (c == '{')
            {
                var close = text.IndexOf('}', i + 2);
                if (close < 0) throw new FormatException($"Send \"{text}\": missing '}}'.");
                keyName = text[(i + 1)..close];
                i = close + 1;
                if (keyName.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
                {
                    pendingText.Append(char.ConvertFromUtf32(Convert.ToInt32(keyName[2..], 16)));
                    mods.Clear();
                    continue;
                }
            }
            else
            {
                keyName = c.ToString();
                i++;
                if (mods.Count == 0)
                {
                    pendingText.Append(c);
                    continue;
                }
            }

            if (!KeyNames.TryGetVk(keyName, out var vk))
                throw new FormatException($"Send \"{text}\": unknown key \"{keyName}\".");
            FlushText();
            foreach (var m in mods) steps.Add(new KeyStroke(m, false));
            steps.Add(new KeyStroke(vk, false));
            steps.Add(new KeyStroke(vk, true));
            for (var m = mods.Count - 1; m >= 0; m--) steps.Add(new KeyStroke(mods[m], true));
            mods.Clear();
        }

        FlushText();
        return steps;
    }
}
