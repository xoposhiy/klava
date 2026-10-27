using Klava.Core;

namespace Klava.Core.Tests;

public class LayoutSwitcherTests
{
    [Fact]
    public void Single_tap_switches_layout()
    {
        var kb = new Keyboard();
        kb.Type("hello ").ShiftTap();
        Assert.Equal(1, kb.Platform.Switches);
        Assert.Empty(kb.Platform.Sent);
    }

    [Fact]
    public void Long_hold_does_not_switch()
    {
        var kb = new Keyboard();
        kb.ShiftTap(holdMs: 600);
        Assert.Equal(0, kb.Platform.Switches);
    }

    [Fact]
    public void Shift_used_for_a_capital_letter_does_not_switch()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LShift);
        kb.Wait(50).Tap('H');
        kb.Up(Vk.LShift);
        Assert.Equal(0, kb.Platform.Switches);
    }

    [Fact]
    public void Shift_pressed_while_another_key_is_down_does_not_switch()
    {
        var kb = new Keyboard();
        kb.Down('H');
        kb.Wait(30).ShiftTap();
        Assert.Equal(0, kb.Platform.Switches);
    }

    [Fact]
    public void Shift_click_does_not_switch()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LShift);
        kb.Wait(100);
        kb.Engine.OnMouseButton();
        kb.Wait(100);
        kb.Up(Vk.LShift);
        Assert.Equal(0, kb.Platform.Switches);
    }

    [Fact]
    public void Double_tap_retypes_last_word_in_new_layout()
    {
        var kb = new Keyboard();
        kb.Type("ok ghbdtn").ShiftTap().ShiftTap();

        Assert.Equal(1, kb.Platform.Switches);
        Assert.Equal(6, kb.Platform.SentBackspaces);
        Assert.Equal("привет", kb.Platform.SentText);
    }

    [Fact]
    public void Double_tap_keeps_trailing_spaces()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn  ").ShiftTap().ShiftTap();
        Assert.Equal(8, kb.Platform.SentBackspaces);
        Assert.Equal("привет  ", kb.Platform.SentText);
    }

    [Fact]
    public void Double_tap_keeps_capital_letters()
    {
        var kb = new Keyboard();
        kb.Down(Vk.LShift);
        kb.Wait(50).Tap('G');
        kb.Up(Vk.LShift);
        kb.Wait(80).Type("jdf").ShiftTap().ShiftTap();
        Assert.Equal("Пова", kb.Platform.SentText);
    }

    [Fact]
    public void Backspace_edits_the_word()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtnn").Tap(Vk.Backspace).ShiftTap().ShiftTap();
        Assert.Equal("привет", kb.Platform.SentText);
    }

    [Fact]
    public void Enter_clears_the_word()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn").Tap(Vk.Enter).ShiftTap().ShiftTap();
        Assert.Empty(kb.Platform.Sent);
    }

    [Fact]
    public void Text_typed_after_a_switch_starts_a_new_word()
    {
        var kb = new Keyboard();
        kb.Type("hello").ShiftTap().Type("vbh").ShiftTap().ShiftTap();
        Assert.Equal(2, kb.Platform.Switches);
        Assert.Equal("vbh", kb.Platform.SentText); // layout 2 -> 1 retypes the keys as English
    }

    [Fact]
    public void Slow_second_tap_is_a_new_single_tap()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn").ShiftTap(afterMs: 500).ShiftTap();
        Assert.Equal(2, kb.Platform.Switches);
        Assert.Empty(kb.Platform.Sent);
    }

    [Fact]
    public void Third_tap_switches_again()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn").ShiftTap().ShiftTap().ShiftTap();
        Assert.Equal(2, kb.Platform.Switches);
    }

    [Fact]
    public void Shift_bounce_is_one_tap_with_debounce()
    {
        // The original bug: a phantom press 2 ms after the release looked like a second tap.
        var kb = new Keyboard();
        kb.Type("ghbdtn").ShiftTap(holdMs: 190, afterMs: 2).Tap(Vk.LShift, holdMs: 25, afterMs: 300);
        Assert.Equal(1, kb.Platform.Switches);
        Assert.Empty(kb.Platform.Sent);
    }

    [Fact]
    public void Shift_bounce_without_debounce_converts_the_word()
    {
        var kb = new Keyboard(debounce: false);
        kb.Type("ghbdtn").ShiftTap(holdMs: 190, afterMs: 2).Tap(Vk.LShift, holdMs: 25, afterMs: 300);
        Assert.Equal("привет", kb.Platform.SentText);
    }

    [Fact]
    public void Missed_release_does_not_block_taps()
    {
        var kb = new Keyboard();
        kb.Down(Vk.Enter); // the release went to an elevated window
        kb.Wait(1000);
        kb.Platform.IsKeyDownFunc = _ => false;
        kb.ShiftTap();
        Assert.Equal(1, kb.Platform.Switches);
    }

    [Fact]
    public void Double_tap_after_shift_home_converts_the_selection()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn");
        kb.Down(Vk.LShift);
        kb.Wait(60).Tap(0x24); // Home
        kb.Up(Vk.LShift);
        kb.Wait(300).ShiftTap().ShiftTap();

        Assert.Equal(1, kb.Platform.Switches);
        Assert.Empty(kb.Platform.Sent);
        Assert.Equal([(1, 2, 20)], kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Double_tap_after_a_click_converts_the_selection()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn ");
        kb.Engine.OnMouseButton(); // a double-click selects a word
        kb.Wait(500).ShiftTap().ShiftTap();
        Assert.Equal([(1, 2, 20)], kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Double_tap_with_a_last_word_does_not_convert_the_selection()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn").ShiftTap().ShiftTap();
        Assert.Empty(kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Single_tap_does_not_convert_the_selection()
    {
        var kb = new Keyboard();
        kb.Tap(Vk.Enter).ShiftTap(afterMs: 500);
        Assert.Empty(kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Selection_conversion_goes_back_from_the_second_layout()
    {
        var kb = new Keyboard();
        kb.ShiftTap(afterMs: 500).Tap(Vk.Enter).ShiftTap().ShiftTap();
        Assert.Equal([(2, 1, 20)], kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Zero_max_selection_chars_turns_off_the_selection_conversion()
    {
        var kb = new Keyboard("""{ "switcher": { "maxSelectionChars": 0 } }""");
        kb.Tap(Vk.Enter).ShiftTap().ShiftTap();
        Assert.Equal(1, kb.Platform.Switches);
        Assert.Empty(kb.Platform.SelectionRequests);
    }

    [Fact]
    public void Foreground_change_clears_the_word()
    {
        var kb = new Keyboard();
        kb.Type("ghbdtn");
        kb.Engine.OnForegroundChanged();
        kb.ShiftTap().ShiftTap();
        Assert.Empty(kb.Platform.Sent);
    }
}
