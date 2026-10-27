namespace Klava.Core;

/// <summary>A key event as the low-level keyboard hook reports it.</summary>
/// <param name="Vk">Virtual key code. Modifiers are side-specific (LShift, RShift, ...).</param>
/// <param name="TimeMs">High-resolution time of the event in milliseconds.</param>
/// <param name="Injected">True for events sent by SendInput (by Klava or other apps).</param>
public readonly record struct KeyEvent(int Vk, int ScanCode, bool Extended, bool IsUp, long TimeMs, bool Injected = false)
{
    /// <summary>Key identity for debounce: Enter and Numpad Enter are different keys.</summary>
    public int KeyId => Vk | (Extended ? 0x100 : 0);
}

/// <summary>One step of synthetic input that Klava sends.</summary>
public abstract record OutputStep;

public sealed record KeyStroke(int Vk, bool IsUp) : OutputStep
{
    public override string ToString() => $"{KeyNames.Name(Vk)} {(IsUp ? "up" : "down")}";
}

public sealed record TextStroke(string Text) : OutputStep
{
    public override string ToString() => $"text \"{Text}\"";
}

/// <summary>A physical key typed into the word buffer.</summary>
public readonly record struct TypedKey(int Vk, int ScanCode, bool Shift, bool CapsLock);

/// <summary>Everything the core needs from the operating system.</summary>
public interface IPlatform
{
    void Send(IReadOnlyList<OutputStep> steps);

    /// <summary>Switches the foreground window to the next layout and returns the layouts before and after.</summary>
    (nint From, nint To) SwitchToNextLayout();

    /// <summary>Returns the characters that the keys type in the layout.</summary>
    string TypeText(IReadOnlyList<TypedKey> keys, nint layout);

    /// <summary>
    /// Returns the key and the Shift state that type the character in the layout, or null when no
    /// key types it or the key needs Ctrl or AltGr. <see cref="TypedKey.CapsLock"/> is false.
    /// </summary>
    TypedKey? FindKey(char c, nint layout);

    /// <summary>
    /// Starts the selected text conversion (see "Selected text" in docs/spec.md) and returns at once.
    /// </summary>
    void ConvertSelection(nint from, nint to, int maxChars);

    /// <summary>Current physical-or-logical key state, used to drop keys whose release Klava missed.</summary>
    bool IsKeyDown(int vk);
}
