using Klava.Core;

namespace Klava.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData(">!x", Side.None, Side.Right, Side.None, Side.None, 'X')]
    [InlineData("<^>!x", Side.Left, Side.Right, Side.None, Side.None, 'X')]
    [InlineData("#^Right", Side.Either, Side.None, Side.None, Side.Either, 0x27)]
    [InlineData(">!+,", Side.None, Side.Right, Side.Either, Side.None, 0xBC)]
    [InlineData(">!+-", Side.None, Side.Right, Side.Either, Side.None, 0xBD)]
    [InlineData(">!Space", Side.None, Side.Right, Side.None, Side.None, Vk.Space)]
    [InlineData(">!\\", Side.None, Side.Right, Side.None, Side.None, 0xDC)]
    public void Parses_autohotkey_syntax(string text, Side ctrl, Side alt, Side shift, Side win, int vk)
    {
        Assert.Equal(new Hotkey(ctrl, alt, shift, win, vk), Hotkey.Parse(text));
    }

    [Theory]
    [InlineData(">!+,", "RAlt+Shift+,")]
    [InlineData("<^>!x", "LCtrl+RAlt+X")]
    [InlineData("#^Right", "Win+Ctrl+Right")]
    public void Describes_hotkeys_for_the_log(string text, string expected)
    {
        Assert.Equal(expected, Hotkey.Parse(text).ToString());
    }

    [Fact]
    public void Reports_sent_steps()
    {
        var kb = new Keyboard();
        var log = new List<string>();
        kb.Engine.HotkeySent += (what, steps) => log.Add($"{what}: {string.Join(", ", steps)}");
        kb.Down(Vk.RAlt);
        kb.Tap('X');
        kb.Up(Vk.RAlt);
        Assert.Equal(["hotkey RAlt+X: vkE8 down, vkE8 up, RAlt up, text \"✕\""], log);
    }

    [Theory]
    [InlineData(">!Nope")]
    [InlineData("!>")]
    [InlineData("")]
    public void Rejects_invalid_hotkeys(string text)
    {
        Assert.Throws<FormatException>(() => Hotkey.Parse(text));
    }

    [Fact]
    public void Parses_send_strings()
    {
        Assert.Equal(
            [new KeyStroke(Vk.LCtrl, false), new KeyStroke(0x23, false), new KeyStroke(0x23, true), new KeyStroke(Vk.LCtrl, true)],
            SendParser.Parse("^{End}"));
        Assert.Equal([new TextStroke("✕ab")], SendParser.Parse("{U+2715}ab"));
    }

    [Fact]
    public void Right_alt_rule_releases_alt_and_sends_text()
    {
        var kb = new Keyboard();
        Assert.False(kb.Down(Vk.RAlt));
        Assert.True(kb.Down('X'));
        Assert.Equal(
            [new KeyStroke(Vk.MenuMask, false), new KeyStroke(Vk.MenuMask, true), new KeyStroke(Vk.RAlt, true), new TextStroke("✕")],
            kb.Platform.Sent);

        // Windows already has RAlt up: block its autorepeat, pass its physical release.
        kb.Platform.Sent.Clear();
        kb.Wait(500);
        Assert.True(kb.Down(Vk.RAlt));
        Assert.True(kb.Up('X'));
        Assert.False(kb.Up(Vk.RAlt));
        Assert.Empty(kb.Platform.Sent);
        Assert.False(kb.Engine.IsKeyDown(Vk.RAlt));
    }

    [Fact]
    public void AltGr_hotkey_releases_the_fake_ctrl_and_leaves_nothing_down()
    {
        // The bug from the log: on ru-RU, Right Alt is AltGr, and Left Ctrl stayed down.
        var kb = new Keyboard();
        kb.AltGrDown();
        Assert.True(kb.Down(0xBD));
        Assert.Equal(
            [
                new KeyStroke(Vk.MenuMask, false), new KeyStroke(Vk.MenuMask, true),
                new KeyStroke(Vk.LCtrl, true), new KeyStroke(Vk.RAlt, true), new TextStroke("—"),
            ],
            kb.Platform.Sent);

        kb.Platform.Sent.Clear();
        kb.Up(0xBD);
        Assert.False(kb.Up(Vk.RAlt)); // Windows sends no fake Ctrl release when it has Left Ctrl up
        Assert.Empty(kb.Platform.Sent);
        Assert.False(kb.Engine.IsKeyDown(Vk.LCtrl));
        Assert.False(kb.Engine.IsKeyDown(Vk.RAlt));
    }

    [Fact]
    public void Physical_release_of_a_released_modifier_reaches_hooks_that_skip_injected_input()
    {
        // The bug from the log: ChatGPT's hotkey helper ignores injected input. It saw the physical
        // Right Alt press but not Klava's injected release, and Klava blocked the physical release.
        // ChatGPT then saw both Alt keys down on every later Left Alt press and opened AppShots.
        var kb = new Keyboard();
        kb.AltGrDown();
        kb.Wait(44);
        Assert.True(kb.Down('0')); // AltGr+0 sends "→"
        kb.Wait(80);
        Assert.True(kb.Up('0'));
        kb.Wait(63);
        Assert.False(kb.Up(Vk.LCtrl, KeyState.AltGrCtrlScanCode));
        Assert.False(kb.Up(Vk.RAlt));
        kb.Wait(20_000);
        Assert.False(kb.Down(Vk.LAlt));
        Assert.False(kb.Up(Vk.LAlt));
    }

    [Fact]
    public void New_press_of_a_released_modifier_passes()
    {
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Tap('X');
        kb.Up(Vk.RAlt);
        kb.Wait(200);
        Assert.False(kb.Down(Vk.RAlt));
        Assert.False(kb.Up(Vk.RAlt));
    }

    [Fact]
    public void Missed_release_of_a_released_modifier_expires()
    {
        // Right Alt goes up in an elevated window: Klava never sees the release.
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Tap('X');
        kb.Platform.Sent.Clear();
        kb.Platform.IsKeyDownFunc = _ => false;
        kb.Wait(3000);
        Assert.False(kb.Down('X')); // a plain x, not ✕
        Assert.Empty(kb.Platform.Sent);
        kb.Up('X');
        kb.Wait(100);
        Assert.False(kb.Down(Vk.RAlt)); // a new press is not mistaken for an autorepeat
    }

    [Fact]
    public void Held_released_modifier_stays_held_while_it_autorepeats()
    {
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Tap('X');
        kb.Platform.Sent.Clear();
        kb.Platform.IsKeyDownFunc = _ => false;
        kb.Wait(500);
        for (var i = 0; i < 60; i++)
        {
            Assert.True(kb.Down(Vk.RAlt)); // autorepeat, blocked
            kb.Wait(33);
        }
        Assert.True(kb.Down('A'));
        Assert.Equal("α", kb.Platform.SentText);
    }

    [Fact]
    public void Second_hotkey_in_one_hold_sends_only_text()
    {
        var kb = new Keyboard();
        kb.AltGrDown();
        kb.Tap('A');
        kb.Platform.Sent.Clear();
        kb.Platform.IsKeyDownFunc = _ => false; // Windows has the modifiers up, the user still holds them
        Assert.True(kb.Down('B'));
        Assert.Equal([new TextStroke("β")], kb.Platform.Sent);
    }

    [Fact]
    public void Shifted_rule_needs_shift()
    {
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Down(Vk.LShift);
        Assert.True(kb.Down(0xBC));
        Assert.Equal("≤", kb.Platform.SentText);
    }

    [Fact]
    public void Extra_modifier_prevents_a_match()
    {
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Down(Vk.LShift);
        Assert.False(kb.Down('X')); // there is no rule for RAlt+Shift+X
        Assert.Empty(kb.Platform.Sent);
    }

    [Fact]
    public void AltGr_rule_matches_left_ctrl_and_right_alt()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LCtrl);
        kb.Down(Vk.RAlt);
        Assert.True(kb.Down('A'));
        Assert.Equal("α", kb.Platform.SentText);
    }

    [Fact]
    public void Win_ctrl_right_sends_end_with_modifiers_released()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LWin);
        kb.Down(Vk.LCtrl);
        Assert.True(kb.Down(0x27));
        Assert.Equal(
            [
                new KeyStroke(Vk.MenuMask, false), new KeyStroke(Vk.MenuMask, true),
                new KeyStroke(Vk.LCtrl, true), new KeyStroke(Vk.LWin, true),
                new KeyStroke(0x23, false), new KeyStroke(0x23, true),
            ],
            kb.Platform.Sent);
    }

    [Fact]
    public void Autorepeat_fires_again()
    {
        var kb = new Keyboard();
        kb.Down(Vk.RAlt);
        kb.Down('X');
        kb.Wait(500);
        kb.Down('X');
        Assert.Equal("✕✕", kb.Platform.SentText);
    }

    [Fact]
    public void Fired_hotkey_clears_the_word()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn");
        kb.Down(Vk.RAlt);
        kb.Tap('X');
        kb.Up(Vk.RAlt);
        kb.Wait(100);
        kb.Platform.Sent.Clear();
        kb.ShiftTap().ShiftTap();
        Assert.Empty(kb.Platform.Sent);
    }
}
