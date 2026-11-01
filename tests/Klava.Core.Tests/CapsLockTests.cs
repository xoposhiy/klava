using Klava.Core;

namespace Klava.Core.Tests;

public class CapsLockTests
{
    [Fact]
    public void Injected_caps_lock_reset_does_not_make_the_converted_word_uppercase()
    {
        var kb = new Keyboard();
        kb.Tap(Vk.CapsLock);
        kb.Platform.CapsLockOn = true;
        Assert.False(kb.Engine.OnKey(new KeyEvent(Vk.CapsLock, 0x3A, false, false, kb.Now, Injected: true)));
        Assert.False(kb.Engine.OnKey(new KeyEvent(Vk.CapsLock, 0x3A, false, true, kb.Now + 10, Injected: true)));
        kb.Platform.CapsLockOn = false;

        ConvertWord(kb);

        Assert.Equal("привет ", kb.Platform.SentText);
    }

    [Fact]
    public void Caps_lock_press_blocked_by_another_hook_does_not_change_the_converted_case()
    {
        var kb = new Keyboard();
        kb.Tap(Vk.CapsLock);
        // A later hook blocks the press, so Windows keeps Caps Lock off.
        kb.Platform.CapsLockOn = false;

        ConvertWord(kb);

        Assert.Equal("привет ", kb.Platform.SentText);
    }

    [Fact]
    public void Caps_lock_changed_while_paused_is_used_after_resume()
    {
        var kb = new Keyboard();
        kb.Engine.Enabled = false;
        kb.Tap(Vk.CapsLock);
        kb.Platform.CapsLockOn = true;
        kb.Engine.Enabled = true;

        ConvertWord(kb);

        Assert.Equal("ПРИВЕТ ", kb.Platform.SentText);
    }

    [Fact]
    public void Each_key_keeps_the_caps_lock_state_from_when_it_was_typed()
    {
        var kb = new Keyboard();
        kb.Platform.CapsLockOn = true;
        kb.Type("ghb");
        kb.Platform.CapsLockOn = false;
        kb.Type("dtn ");
        DoubleShift(kb);

        Assert.Equal("ПРИвет ", kb.Platform.SentText);
    }

    private static void ConvertWord(Keyboard kb)
    {
        kb.Type("ghbdtn ");
        DoubleShift(kb);
    }

    private static void DoubleShift(Keyboard kb)
    {
        // Trace at 13:48:34 on 2026-11-01: first hold 60 ms, gap 83 ms, second hold 52 ms.
        kb.ShiftTap(holdMs: 60, afterMs: 83).ShiftTap(holdMs: 52);
    }
}
