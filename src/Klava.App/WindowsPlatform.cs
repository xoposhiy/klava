using System.Globalization;
using System.Runtime.InteropServices;
using Klava.Core;
using static Klava.App.Native;

namespace Klava.App;

/// <summary>The Win32 side of <see cref="IPlatform"/>.</summary>
internal sealed class WindowsPlatform : IPlatform
{
    /// <summary>dwExtraInfo of Klava's own synthetic input ("KLAV").</summary>
    public const nint Marker = 0x4B4C4156;

    private readonly SwitchMethod _method;
    private readonly int[] _languages;
    private readonly SelectionConverter? _selection;

    /// <param name="selection">Runs the selected text conversion; null turns it off.</param>
    public WindowsPlatform(SwitcherConfig config, SelectionConverter? selection = null)
    {
        _method = config.SwitchMethod;
        _selection = selection;
        _languages = config.Layouts.Select(LanguageId).Where(id => id != 0).ToArray();
    }

    public void Send(IReadOnlyList<OutputStep> steps) => SendSteps(steps);

    public static void SendSteps(IReadOnlyList<OutputStep> steps)
    {
        var inputs = new List<INPUT>(steps.Count * 2);
        foreach (var step in steps)
        {
            switch (step)
            {
                case KeyStroke key:
                    inputs.Add(KeyInput(key.Vk, key.IsUp));
                    break;
                case TextStroke text:
                    foreach (var c in text.Text)
                    {
                        inputs.Add(CharInput(c, false));
                        inputs.Add(CharInput(c, true));
                    }
                    break;
            }
        }
        if (inputs.Count == 0) return;
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) Log.Warn($"SendInput sent {sent} of {inputs.Count} events, error {Marshal.GetLastWin32Error()}.");
    }

    public (nint From, nint To) SwitchToNextLayout()
    {
        var foreground = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(foreground, out _);
        var current = GetKeyboardLayout(thread);
        var target = NextLayout(current);

        switch (_method)
        {
            case SwitchMethod.Message:
                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                var window = GetGUIThreadInfo(thread, ref info) && info.hwndFocus != 0 ? info.hwndFocus : foreground;
                if (!PostMessage(window, WM_INPUTLANGCHANGEREQUEST, 0, target))
                    Log.Warn($"PostMessage(WM_INPUTLANGCHANGEREQUEST) failed, error {Marshal.GetLastWin32Error()}.");
                break;
            case SwitchMethod.AltShift:
                SendChord(Vk.LAlt, Vk.LShift);
                break;
            case SwitchMethod.CtrlShift:
                SendChord(Vk.LCtrl, Vk.LShift);
                break;
            case SwitchMethod.WinSpace:
                SendChord(Vk.LWin, Vk.Space);
                break;
        }
        return (current, target);
    }

    public string TypeText(IReadOnlyList<TypedKey> keys, nint layout)
    {
        var text = new System.Text.StringBuilder();
        var state = new byte[256];
        var buffer = new char[8];
        foreach (var key in keys)
        {
            Array.Clear(state);
            if (key.Shift) state[VK_SHIFT] = 0x80;
            if (key.CapsLock) state[VK_CAPITAL] = 0x01;
            // Map by scan code: the same physical key can have a different virtual key in the target layout.
            var vk = key.Vk is >= 0x60 and <= 0x6F or Vk.Space ? 0u : MapVirtualKeyEx((uint)key.ScanCode, MAPVK_VSC_TO_VK, layout);
            if (vk == 0) vk = (uint)key.Vk;
            // Flag 4: do not change the keyboard state (no dead key side effects).
            var count = ToUnicodeEx(vk, (uint)key.ScanCode, state, buffer, buffer.Length, 4, layout);
            if (count > 0) text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    public TypedKey? FindKey(char c, nint layout)
    {
        var result = VkKeyScanEx(c, layout);
        if (result == -1) return null;
        var vk = result & 0xFF;
        var shiftState = (result >> 8) & 0xFF;
        if ((shiftState & ~1) != 0) return null; // Ctrl, Alt (AltGr is Ctrl+Alt), or Hankaku
        var scan = MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC, layout);
        return scan == 0 ? null : new TypedKey(vk, (int)scan, shiftState == 1, false);
    }

    public void ConvertSelection(nint from, nint to, int maxChars) => _selection?.Start(this, from, to, maxChars);

    public bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool IsCapsLockOn() => (GetKeyState(VK_CAPITAL) & 1) != 0;

    private nint NextLayout(nint current)
    {
        var count = GetKeyboardLayoutList(0, null);
        var installed = new nint[count];
        GetKeyboardLayoutList(count, installed);

        var cycle = _languages
            .Select(lang => installed.FirstOrDefault(hkl => (hkl & 0xFFFF) == lang))
            .Where(hkl => hkl != 0)
            .ToList();
        if (cycle.Count < 2) cycle = installed.ToList();
        if (cycle.Count == 0) return current;

        var index = cycle.FindIndex(hkl => (hkl & 0xFFFF) == (current & 0xFFFF));
        return cycle[(index + 1) % cycle.Count];
    }

    private void SendChord(int modifier, int key) => Send(
    [
        new KeyStroke(modifier, false), new KeyStroke(key, false),
        new KeyStroke(key, true), new KeyStroke(modifier, true),
    ]);

    // MapVirtualKey returns the numpad scan codes (no E0 prefix) for these keys, so mark them here.
    // Without the flag, apps see Numpad End instead of End, and Windows treats RAlt and RCtrl as left keys.
    private static readonly HashSet<int> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home Left Up Right Down
        0x2C, 0x2D, 0x2E, 0x6F, 0x90, 0x5D,             // PrintScreen Insert Delete NumpadDivide NumLock Apps
        Vk.RCtrl, Vk.RAlt, Vk.LWin, Vk.RWin,
    ];

    private static INPUT KeyInput(int vk, bool up)
    {
        var scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if (ExtendedKeys.Contains(vk) || (scan & 0xFF00) is 0xE000 or 0xE100) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)(scan & 0xFF), dwFlags = flags, dwExtraInfo = Marker } },
        };
    }

    private static INPUT CharInput(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = Marker },
        },
    };

    private static int LanguageId(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).LCID & 0xFFFF;
        }
        catch (CultureNotFoundException)
        {
            Log.Warn($"Unknown layout language \"{tag}\" in switcher.layouts.");
            return 0;
        }
    }
}
