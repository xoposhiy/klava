using Klava.Core;

namespace Klava.Core.Tests;

/// <summary>Records what the engine sends. Layout 1 is en-US, layout 2 is ru-RU.</summary>
public sealed class FakePlatform : IPlatform
{
    private const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string RuKeys = "йцукенгшщзхъфывапролджэячсмитьбюё";

    public List<OutputStep> Sent { get; } = [];
    public int Switches { get; private set; }
    public nint Layout { get; private set; } = 1;
    public Func<int, bool> IsKeyDownFunc { get; set; } = _ => true;
    public List<(nint From, nint To, int MaxChars)> SelectionRequests { get; } = [];

    public void Send(IReadOnlyList<OutputStep> steps) => Sent.AddRange(steps);

    public (nint From, nint To) SwitchToNextLayout()
    {
        Switches++;
        var from = Layout;
        Layout = Layout == 1 ? 2 : 1;
        return (from, Layout);
    }

    public string TypeText(IReadOnlyList<TypedKey> keys, nint layout) =>
        string.Concat(keys.Select(k => Char(k, layout)));

    public TypedKey? FindKey(char c, nint layout)
    {
        if (c == ' ') return new TypedKey(Vk.Space, Vk.Space, false, false);
        var lower = char.ToLowerInvariant(c);
        var index = (layout == 2 ? RuKeys : EnKeys).IndexOf(lower);
        var en = index >= 0 ? EnKeys[index] : lower;
        if (layout == 2 && index < 0 || !KeyNames.TryGetVk(en.ToString(), out var vk)) return null;
        return new TypedKey(vk, vk, c != lower, false);
    }

    public void ConvertSelection(nint from, nint to, int maxChars) => SelectionRequests.Add((from, to, maxChars));

    public bool IsKeyDown(int vk) => IsKeyDownFunc(vk);

    public string SentText => string.Concat(Sent.OfType<TextStroke>().Select(t => t.Text));

    public int SentBackspaces => Sent.Count(s => s is KeyStroke { Vk: Vk.Backspace, IsUp: false });

    private static string Char(TypedKey key, nint layout)
    {
        if (key.Vk == Vk.Space) return " ";
        var en = KeyNames.Name(key.Vk).ToLowerInvariant();
        var index = EnKeys.IndexOf(en, StringComparison.Ordinal);
        var c = layout == 2 && index >= 0 ? RuKeys[index].ToString() : en;
        return key.Shift ^ key.CapsLock ? c.ToUpperInvariant() : c;
    }
}

/// <summary>Drives an engine with timed key events. Time starts at 1000 ms.</summary>
public sealed class Keyboard
{
    public Keyboard(string? configJson = null, bool debounce = true)
    {
        var settings = KlavaConfig.Load(configJson ?? KlavaConfig.DefaultJson);
        Engine = new Engine(settings, Platform, capsLockOn: false) { DebounceEnabled = debounce };
        Engine.BounceBlocked += b => Bounces.Add(b);
    }

    public FakePlatform Platform { get; } = new();
    public Engine Engine { get; }
    public List<Bounce> Bounces { get; } = [];
    public long Now { get; private set; } = 1000;

    public Keyboard Wait(long ms)
    {
        Now += ms;
        return this;
    }

    /// <summary>Sends a press; returns true when the engine blocks it.</summary>
    public bool Down(int vk, int? scanCode = null) => Engine.OnKey(new KeyEvent(vk, scanCode ?? ScanCode(vk), false, false, Now));

    public bool Up(int vk, int? scanCode = null) => Engine.OnKey(new KeyEvent(vk, scanCode ?? ScanCode(vk), false, true, Now));

    /// <summary>Right Alt on an AltGr layout (ru-RU on Windows 10+): Windows adds a fake Left Ctrl.</summary>
    public bool AltGrDown()
    {
        Down(Vk.LCtrl, KeyState.AltGrCtrlScanCode);
        return Down(Vk.RAlt);
    }

    /// <summary>Press, hold, release, then wait.</summary>
    public Keyboard Tap(int vk, long holdMs = 80, long afterMs = 80)
    {
        Down(vk);
        Wait(holdMs);
        Up(vk);
        return Wait(afterMs);
    }

    /// <summary>Types lowercase letters, digits and spaces as key taps.</summary>
    public Keyboard Type(string text)
    {
        foreach (var c in text)
        {
            if (!KeyNames.TryGetVk(c == ' ' ? "Space" : c.ToString(), out var vk))
                throw new ArgumentException($"No key for '{c}'.");
            Tap(vk);
        }
        return this;
    }

    public Keyboard ShiftTap(long holdMs = 190, long afterMs = 150) => Tap(Vk.LShift, holdMs, afterMs);

    private static int ScanCode(int vk) => vk; // the fake platform maps by virtual key
}
