using System.Runtime.InteropServices;
using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

/// <summary>
/// Replays "physical" key sequences through a real engine and real SendInput, then checks the
/// Windows key state. The keys go to the foreground window, so these tests run only when
/// KLAVA_INPUT_TESTS=1. The hotkey actions send the unassigned key vkE8, so nothing is typed.
/// </summary>
public class InputReplayTests
{
    private const string Config = """
        { "switcher": { "enabled": false },
          "hotkeys": { "rules": [
            { "keys": [">!x"], "send": "{vkE8}" },
            { "keys": ["#^Right"], "send": "{vkE8}" } ] } }
        """;

    private static bool Enabled => Environment.GetEnvironmentVariable("KLAVA_INPUT_TESTS") == "1";

    [Fact]
    public void Right_alt_hotkey_leaves_no_modifier_down()
    {
        if (!Enabled) return;
        var replay = new Replay();
        replay.Key(Vk.RAlt, false);
        replay.Key('X', false);
        replay.Key('X', true);
        replay.Key(Vk.RAlt, true);
        replay.AssertAllUp();
    }

    [Fact]
    public void Right_alt_released_before_trigger_leaves_no_modifier_down()
    {
        if (!Enabled) return;
        var replay = new Replay();
        replay.Key(Vk.RAlt, false);
        replay.Key('X', false);
        replay.Key(Vk.RAlt, true);
        replay.Key('X', true);
        replay.AssertAllUp();
    }

    [Fact]
    public void Win_ctrl_hotkey_leaves_no_modifier_down()
    {
        if (!Enabled) return;
        var replay = new Replay();
        replay.Key(Vk.LWin, false);
        replay.Key(Vk.LCtrl, false);
        replay.Key(0x27, false);
        replay.Key(0x27, true);
        replay.Key(Vk.LCtrl, true);
        replay.Key(Vk.LWin, true);
        replay.AssertAllUp();
    }

    private sealed class Replay
    {
        private readonly Engine _engine;
        private readonly WindowsPlatform _platform;
        private long _now = 1000;

        public Replay()
        {
            var settings = KlavaConfig.Load(Config);
            _platform = new WindowsPlatform(settings.Config.Switcher);
            _engine = new Engine(settings, _platform, capsLockOn: false);
        }

        /// <summary>Feeds the engine like the hook does; a passed event is sent as if it were physical.</summary>
        public void Key(int vk, bool up)
        {
            _now += 100;
            var scan = (int)(Native.MapVirtualKey((uint)vk, Native.MAPVK_VK_TO_VSC_EX) & 0xFF);
            var extended = vk is Vk.RAlt or Vk.RCtrl or Vk.LWin or Vk.RWin or 0x27;
            var blocked = _engine.OnKey(new KeyEvent(vk, scan, extended, up, _now));
            if (!blocked) SendRaw(vk, scan, extended, up);
            Thread.Sleep(60);
        }

        public void AssertAllUp()
        {
            Thread.Sleep(100);
            int[] keys = [0x12, Vk.LAlt, Vk.RAlt, 0x11, Vk.LCtrl, Vk.RCtrl, Vk.LWin, 0x10];
            var down = keys.Where(_platform.IsKeyDown).Select(vk => $"vk{vk:X2}").ToList();
            foreach (var vk in keys) SendRaw(vk, 0, false, true); // clean up whatever is stuck
            Assert.Empty(down);
        }

        private static void SendRaw(int vk, int scan, bool extended, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = (ushort)scan,
                dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0),
            };
            Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
        }
    }
}
