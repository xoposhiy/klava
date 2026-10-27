namespace Klava.Core;

/// <summary>Windows virtual key codes used by the core.</summary>
public static class Vk
{
    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int CapsLock = 0x14;
    public const int Esc = 0x1B;
    public const int Space = 0x20;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LCtrl = 0xA2;
    public const int RCtrl = 0xA3;
    public const int LAlt = 0xA4;
    public const int RAlt = 0xA5;

    /// <summary>Unassigned key that AutoHotkey also uses to stop Alt and Win releases from opening menus.</summary>
    public const int MenuMask = 0xE8;

    /// <summary>Modifiers in the order Klava releases and presses them again.</summary>
    public static readonly int[] Modifiers = [LCtrl, RCtrl, LAlt, RAlt, LShift, RShift, LWin, RWin];

    public static bool IsModifier(int vk) => Array.IndexOf(Modifiers, vk) >= 0;

    public static bool IsShift(int vk) => vk is LShift or RShift;

    public static bool IsPrintable(int vk) =>
        vk == Space
        || vk is >= 0x30 and <= 0x39   // digits
        || vk is >= 0x41 and <= 0x5A   // letters
        || vk is >= 0x60 and <= 0x6F   // numpad digits and operators
        || vk is >= 0xBA and <= 0xC0   // ; = , - . / `
        || vk is >= 0xDB and <= 0xDF   // [ \ ] '
        || vk == 0xE2;                 // extra key on ISO keyboards
}
