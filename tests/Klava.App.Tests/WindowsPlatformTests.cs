using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

/// <summary>
/// Integration tests against the real Windows layout API. They need the en-US and ru-RU
/// layouts installed and pass without checks on machines that do not have them.
/// </summary>
public class WindowsPlatformTests
{
    private const int English = 0x0409, Russian = 0x0419;

    [Fact]
    public void Types_keys_in_russian_layout()
    {
        if (FindLayout(Russian) is not { } ru) return;
        var platform = new WindowsPlatform(new SwitcherConfig());

        // Scan codes of G H B D T N on a US keyboard, the last two with Shift.
        TypedKey[] keys =
        [
            new('G', 0x22, false, false), new('H', 0x23, false, false), new('B', 0x30, false, false),
            new('D', 0x20, false, false), new('T', 0x14, true, false), new('N', 0x31, false, true),
            new(Vk.Space, 0x39, false, false), new(0xBC, 0x33, false, false),
        ];
        Assert.Equal("привЕТ б", platform.TypeText(keys, ru));
    }

    [Fact]
    public void Types_keys_in_english_layout()
    {
        if (FindLayout(English) is not { } en) return;
        var platform = new WindowsPlatform(new SwitcherConfig());
        TypedKey[] keys = [new('G', 0x22, true, false), new(0xBE, 0x34, true, false), new(0x31, 0x02, true, false)];
        Assert.Equal("G>!", platform.TypeText(keys, en));
    }

    [Fact]
    public void Converts_selected_text_between_english_and_russian()
    {
        if (FindLayout(English) is not { } en || FindLayout(Russian) is not { } ru) return;
        var platform = new WindowsPlatform(new SwitcherConfig());
        Assert.Equal("Привет, мир?", SelectionText.Convert("Ghbdtn? vbh&", en, ru, platform));
        Assert.Equal("Ghbdtn? vbh&", SelectionText.Convert("Привет, мир?", ru, en, platform));
    }

    [Fact]
    public void Keeps_characters_that_need_altgr_or_have_no_key()
    {
        if (FindLayout(English) is not { } en || FindLayout(Russian) is not { } ru) return;
        var platform = new WindowsPlatform(new SwitcherConfig());
        Assert.Null(platform.FindKey('₽', ru)); // AltGr+8 on ru-RU
        Assert.Null(platform.FindKey('п', en));
        Assert.Equal("привет ₽ ✕", SelectionText.Convert("ghbdtn ₽ ✕", en, ru, platform));
    }

    [Fact]
    public void SendInput_accepts_the_input_struct()
    {
        // vkE8 is unassigned, so sending it has no visible effect.
        var inputs = new Native.INPUT[2];
        for (var i = 0; i < 2; i++)
        {
            inputs[i].type = Native.INPUT_KEYBOARD;
            inputs[i].u.ki = new Native.KEYBDINPUT { wVk = Vk.MenuMask, dwFlags = i == 1 ? Native.KEYEVENTF_KEYUP : 0 };
        }
        Assert.Equal(2u, Native.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>()));
    }

    private static nint? FindLayout(int language)
    {
        var count = Native.GetKeyboardLayoutList(0, null);
        var layouts = new nint[count];
        Native.GetKeyboardLayoutList(count, layouts);
        return layouts.Cast<nint?>().FirstOrDefault(hkl => (hkl!.Value & 0xFFFF) == language);
    }
}
