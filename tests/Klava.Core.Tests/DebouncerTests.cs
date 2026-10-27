using Klava.Core;

namespace Klava.Core.Tests;

public class DebouncerTests
{
    [Fact]
    public void Blocks_phantom_press_right_after_release()
    {
        // From the diagnostic log: LShift up, then a phantom press 2.4 ms later.
        var kb = new Keyboard();
        Assert.False(kb.Down(Vk.LShift));
        kb.Wait(186);
        Assert.False(kb.Up(Vk.LShift));
        kb.Wait(2);
        Assert.True(kb.Down(Vk.LShift));
        kb.Wait(25);
        Assert.True(kb.Up(Vk.LShift));

        Assert.Equal([new Bounce(Vk.LShift, 2)], kb.Bounces);
    }

    [Fact]
    public void Blocks_bounce_in_the_middle_of_a_press()
    {
        // From the diagnostic log: M down, up after 13 ms, down again after 11 ms, up after 52 ms.
        var kb = new Keyboard();
        Assert.False(kb.Down('M'));
        kb.Wait(13);
        Assert.False(kb.Up('M'));
        kb.Wait(11);
        Assert.True(kb.Down('M'));
        kb.Wait(52);
        Assert.True(kb.Up('M'));
    }

    [Theory]
    // From the Klava log (16:53:59 and 16:54:05): two bounces in one press of M gave "ьь".
    [InlineData(15, 8, 52, 7)]
    [InlineData(8, 22, 23, 7)]
    public void Blocks_a_chain_of_bounces(int heldMs, int gapMs, int bounceHeldMs, int secondGapMs)
    {
        var kb = new Keyboard(KlavaConfig.DefaultJson.Replace("\"chatteringKeys\": {}", "\"chatteringKeys\": { \"M\": 50 }"));
        Assert.False(kb.Down('M'));
        kb.Wait(heldMs);
        Assert.False(kb.Up('M'));
        kb.Wait(gapMs);
        Assert.True(kb.Down('M'));
        kb.Wait(bounceHeldMs);
        Assert.True(kb.Up('M'));
        kb.Wait(secondGapMs);
        Assert.True(kb.Down('M'));
        kb.Wait(16);
        Assert.True(kb.Up('M'));
        kb.Wait(100);
        Assert.False(kb.Down('M'));
    }

    [Fact]
    public void Passes_fast_real_repress()
    {
        var kb = new Keyboard();
        kb.Tap(Vk.Backspace, holdMs: 50, afterMs: 45);
        Assert.False(kb.Down(Vk.Backspace));
        Assert.Empty(kb.Bounces);
    }

    [Fact]
    public void Blocks_autorepeat_of_a_blocked_press()
    {
        var kb = new Keyboard();
        kb.Tap('A', afterMs: 5);
        Assert.True(kb.Down('A'));
        kb.Wait(500);
        Assert.True(kb.Down('A'));
        Assert.True(kb.Up('A'));
        kb.Wait(100);
        Assert.False(kb.Down('A'));
    }

    [Fact]
    public void Uses_per_key_threshold()
    {
        var json = KlavaConfig.DefaultJson.Replace("\"chatteringKeys\": {}", "\"chatteringKeys\": { \"LShift\": 60 }");
        Assert.NotEqual(KlavaConfig.DefaultJson, json);
        var kb = new Keyboard(json);
        kb.Tap(Vk.LShift, afterMs: 45);
        Assert.True(kb.Down(Vk.LShift));
        kb.Up(Vk.LShift);
        kb.Wait(100);
        kb.Tap('A', afterMs: 45);
        Assert.False(kb.Down('A'));
    }

    [Fact]
    public void Treats_extended_flag_as_a_different_key()
    {
        var debouncer = new Debouncer(30);
        debouncer.ShouldBlock(new KeyEvent(Vk.Enter, 0x1C, false, true, 100), out _, out _);
        Assert.False(debouncer.ShouldBlock(new KeyEvent(Vk.Enter, 0x1C, true, false, 105), out _, out _));
        Assert.True(debouncer.ShouldBlock(new KeyEvent(Vk.Enter, 0x1C, false, false, 105), out _, out _));
    }

    [Fact]
    public void Reports_fast_repress_that_passes()
    {
        var kb = new Keyboard();
        var reports = new List<FastRepress>();
        kb.Engine.FastRepressPassed += reports.Add;
        kb.Tap('M', holdMs: 14, afterMs: 35);
        kb.Tap('M', holdMs: 60, afterMs: 150);
        kb.Tap('M');
        kb.Tap(0x27, holdMs: 60, afterMs: 45); // arrows are not printable: no report
        kb.Tap(0x27);
        Assert.Equal([new FastRepress('M', 35, 14)], reports);
    }

    [Fact]
    public void Disabled_filter_passes_everything()
    {
        var kb = new Keyboard(debounce: false);
        kb.Tap(Vk.LShift, afterMs: 2);
        Assert.False(kb.Down(Vk.LShift));
    }
}
