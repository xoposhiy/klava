namespace Klava.Core;

/// <summary>Fires hotkey rules. See "Hotkeys" in docs/spec.md for the send sequence.</summary>
public sealed class HotkeyEngine(IReadOnlyList<HotkeyRule> rules, IPlatform platform)
{
    private readonly HashSet<int> _consumed = [];

    // Modifiers the user still holds but Klava already released for Windows.
    private readonly HashSet<int> _released = [];

    /// <summary>Raised after Klava sends input for a hotkey: the description and the sent steps.</summary>
    public event Action<string, IReadOnlyList<OutputStep>>? Sent;

    /// <summary>True when the user holds the modifier but Windows has it released.</summary>
    public bool IsReleased(int vk) => _released.Contains(vk);

    /// <summary>Drops the released mark of modifiers that are no longer down in <paramref name="keys"/>.</summary>
    public void ForgetReleased(KeyState keys) => _released.RemoveWhere(vk => !keys.IsDown(vk));

    /// <summary>Returns true to block the event. <paramref name="keys"/> already includes the event.</summary>
    public bool Handle(in KeyEvent e, bool repeat, KeyState keys)
    {
        if (Vk.IsModifier(e.Vk))
        {
            // Windows has the modifier up: an autorepeat would press it again, a release is not needed.
            if (e.IsUp)
            {
                var block = _released.Remove(e.Vk);
                ForgetReleased(keys); // the fake AltGr Ctrl goes with Right Alt
                return block;
            }
            if (repeat) return _released.Contains(e.Vk);
            _released.Remove(e.Vk);
            return false;
        }

        if (e.IsUp) return _consumed.Remove(e.Vk);

        if (Find(e.Vk, keys) is not var (trigger, rule)) return false;
        _consumed.Add(e.Vk);
        Fire(trigger, rule, keys);
        return true;
    }

    private (Hotkey, HotkeyRule)? Find(int vk, KeyState keys)
    {
        foreach (var rule in rules)
            foreach (var trigger in rule.Triggers)
                if (trigger.Vk == vk && trigger.Matches(keys))
                    return (trigger, rule);
        return null;
    }

    /// <summary>
    /// Releases the held modifiers once, so the action reaches the app without them, then sends the
    /// action. The modifiers stay released until the user lets them go.
    /// </summary>
    private void Fire(Hotkey trigger, HotkeyRule rule, KeyState keys)
    {
        var held = keys.DownModifiers().Where(m => !_released.Contains(m)).ToList();
        var steps = new List<OutputStep>();
        if (held.Any(m => m is Vk.LAlt or Vk.RAlt or Vk.LWin or Vk.RWin))
        {
            // Stops the Alt or Win release from opening the menu bar or the Start menu.
            steps.Add(new KeyStroke(Vk.MenuMask, false));
            steps.Add(new KeyStroke(Vk.MenuMask, true));
        }
        foreach (var m in held)
        {
            steps.Add(new KeyStroke(m, true));
            _released.Add(m);
        }
        steps.AddRange(rule.Action);
        platform.Send(steps);
        Sent?.Invoke($"hotkey {trigger}", steps);
    }
}
