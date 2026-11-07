using System.Diagnostics;
using System.Runtime.InteropServices;
using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

[CollectionDefinition("Input replay", DisableParallelization = true)]
public sealed class InputReplayCollection;

[Collection("Input replay")]
public sealed class TypingInputReplayTests
{
    private const nint TestMarker = 0x54595045;

    [Fact]
    public void Hook_correction_precedes_the_passed_physical_word_boundary()
    {
        if (Environment.GetEnvironmentVariable("KLAVA_INPUT_TESTS") != "1") return;
        var count = Native.GetKeyboardLayoutList(0, null);
        var layouts = new nint[count];
        Native.GetKeyboardLayoutList(count, layouts);
        var russian = layouts.FirstOrDefault(layout => (layout & 0xFFFF) == 0x419);
        if (russian == 0) return;
        FormTestKit.Run(() => CheckBoundaries(russian));
    }

    private static void CheckBoundaries(nint russian)
    {
        var previousWindow = Native.GetForegroundWindow();
        var previousLayout = Native.GetKeyboardLayout(0);
        using var form = new Form { Text = "Klava input test", Width = 400, Height = 200 };
        using var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true };
        form.Controls.Add(text);
        form.Show();
        SetForegroundWindow(form.Handle);
        text.Focus();
        ActivateKeyboardLayout(russian, 0);
        Pump();
        Assert.Equal(form.Handle, Native.GetForegroundWindow());
        var platform = new ReplayPlatform(russian);
        var engine = new Engine(KlavaConfig.Load("""{"switcher":{"enabled":false}}"""), platform);
        engine.SetApprovedTypingWords(["llm"]);
        Exception? callbackError = null;
        Native.HookProc callback = (code, wParam, lParam) =>
        {
            if (code >= 0)
            {
                var key = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                if (key.dwExtraInfo == TestMarker)
                {
                    try
                    {
                        if (engine.OnKey(new((int)key.vkCode, (int)key.scanCode, (key.flags & 1) != 0,
                            (key.flags & Native.LLKHF_UP) != 0, Clock.NowMs()))) return 1;
                    }
                    catch (Exception ex) { callbackError = ex; }
                }
            }
            return Native.CallNextHookEx(0, code, wParam, lParam);
        };
        var hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, callback, Native.GetModuleHandle(null), 0);
        Assert.NotEqual(0, hook);
        try
        {
            foreach (var boundary in new[] { Vk.Space, Vk.Enter, 0xBF })
            {
                text.Clear();
                foreach (var key in "LLM") Tap(key);
                Assert.Equal("ддь", text.Text);
                Tap(boundary);
                Assert.Null(callbackError);
                var suffix = boundary switch { Vk.Space => " ", Vk.Enter => "\r\n", _ => "." };
                Assert.Equal("llm" + suffix, text.Text);
                Assert.Equal(russian, Native.GetKeyboardLayout(0));
            }
            text.Clear();
            Key(Vk.LShift, false);
            foreach (var key in "LLM") Tap(key);
            Assert.Equal("ДДЬ", text.Text);
            Tap(0xBF);
            Assert.Equal("LLM,", text.Text);
            Assert.True(engine.IsKeyDown(Vk.LShift));
            Key(Vk.LShift, true);
        }
        finally
        {
            WindowsPlatform.SendSteps([new KeyStroke(Vk.LShift, true)]);
            Native.UnhookWindowsHookEx(hook);
            GC.KeepAlive(callback);
            ActivateKeyboardLayout(previousLayout, 0);
            form.Close();
            SetForegroundWindow(previousWindow);
        }

        void Tap(int vk)
        {
            Key(vk, false);
            Key(vk, true);
        }

        void Key(int vk, bool up)
        {
            Assert.Equal(form.Handle, Native.GetForegroundWindow());
            var scan = Native.MapVirtualKeyEx((uint)vk, Native.MAPVK_VK_TO_VSC, russian);
            var inputs = new Native.INPUT[1];
            for (var i = 0; i < inputs.Length; i++)
            {
                inputs[i].type = Native.INPUT_KEYBOARD;
                inputs[i].u.ki = new Native.KEYBDINPUT
                {
                    wVk = (ushort)vk, wScan = (ushort)scan, dwExtraInfo = TestMarker,
                    dwFlags = up ? Native.KEYEVENTF_KEYUP : 0
                };
            }
            Assert.Equal(1u, Native.SendInput(1, inputs, Marshal.SizeOf<Native.INPUT>()));
            Pump();
        }
    }

    private static void Pump()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(5); }
    }

    private sealed class ReplayPlatform(nint layout) : IPlatform
    {
        private readonly WindowsPlatform _windows = new(new SwitcherConfig());
        public TypingContext TypingContext => new(1, 1, "test", (long)layout);
        public bool CapsLockOn => _windows.CapsLockOn;
        public bool IsKeyDown(int vk) => _windows.IsKeyDown(vk);
        public void Send(IReadOnlyList<OutputStep> steps) => _windows.Send(steps);
        public string TypeText(IReadOnlyList<TypedKey> keys, nint target) => _windows.TypeText(keys, target);
        public TypedKey? FindKey(char c, nint target) => _windows.FindKey(c, target);
        public (nint From, nint To) SwitchToNextLayout() => (layout, layout);
        public void ConvertSelection(nint from, nint to, int maxChars) { }
    }

    [DllImport("user32.dll")]
    private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
}
