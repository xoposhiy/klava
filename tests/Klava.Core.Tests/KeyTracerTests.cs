using Klava.Core;

namespace Klava.Core.Tests;

public class KeyTracerTests
{
    private static readonly string TraceConfig =
        KlavaConfig.DefaultJson.Replace("\"traceKeys\": []", "\"traceKeys\": [\"M\", \"LShift\"]");

    private static (Keyboard, List<KeyTrace>) Start()
    {
        var kb = new Keyboard(TraceConfig);
        var traces = new List<KeyTrace>();
        kb.Engine.Tracer!.Traced += traces.Add;
        return (kb, traces);
    }

    [Fact]
    public void Traces_neighbors_and_pauses()
    {
        var (kb, traces) = Start();
        kb.Tap('T', holdMs: 50, afterMs: 70);   // T down at 1000, M down at 1120
        kb.Tap('M', holdMs: 60, afterMs: 90);   // M up at 1180, Space down at 1270
        kb.Tap(Vk.Space);

        var trace = Assert.Single(traces);
        Assert.Equal(new KeyTrace('M', 1120, false, 'T', 120, -1, 60, 0, Vk.Space, 90, false), trace);
        Assert.Equal("M | previous: T, 120 ms before | held: 60 ms | next: Space, 90 ms after release", trace.ToString());
    }

    [Fact]
    public void Traces_blocked_bounce_as_its_own_press()
    {
        var (kb, traces) = Start();
        kb.Tap('M', holdMs: 13, afterMs: 11);
        kb.Tap('M', holdMs: 52, afterMs: 100);
        kb.Tap('A');

        Assert.Equal(2, traces.Count);
        Assert.Equal("M | previous: none | held: 13 ms | next: M (BLOCKED), 11 ms after release", traces[0].ToString());
        Assert.True(traces[1].Blocked);
        Assert.Equal(11, traces[1].SameKeyGapMs);
    }

    [Fact]
    public void Shows_a_key_pressed_during_the_hold()
    {
        var (kb, traces) = Start();
        kb.Down(Vk.LShift);
        kb.Wait(40).Tap('H', holdMs: 60, afterMs: 20);
        kb.Up(Vk.LShift);
        kb.Wait(100).Tap('E');

        var trace = Assert.Single(traces);
        Assert.Equal("LShift | previous: none | held: 120 ms | next: H, during the hold (40 ms after press)", trace.ToString());
    }

    [Fact]
    public void Untraced_keys_and_default_config_produce_nothing()
    {
        var kb = new Keyboard();
        Assert.Null(kb.Engine.Tracer);
    }
}
