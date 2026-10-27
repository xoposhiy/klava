using System.Runtime.InteropServices;
using Klava.Core;
using static Klava.App.Native;

namespace Klava.App;

/// <summary>
/// Low-level keyboard and mouse hooks on a dedicated thread. The thread does no UI work, so the
/// callbacks start right after the event and the debounce timing stays accurate.
/// </summary>
internal sealed class InputHook : IDisposable
{
    private const uint ModifierCheckDelayMs = 150;

    private readonly Func<Engine> _engine;
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly TimerProc _modifierCheckProc;
    private nuint _modifierTimer;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private nint _keyboardHook, _mouseHook;
    private nint _foreground;
    private Exception? _startError;

    public InputHook(Func<Engine> engine)
    {
        _engine = engine;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _modifierCheckProc = CheckModifiers;
        _thread = new Thread(Run) { IsBackground = true, Name = "Klava input hook", Priority = ThreadPriority.Highest };
    }

    public void Start()
    {
        _thread.Start();
        _started.Wait();
        if (_startError is not null) throw _startError;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0); // create the message queue before anyone posts WM_QUIT
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == 0 || _mouseHook == 0)
            _startError = new InvalidOperationException($"SetWindowsHookEx failed, error {Marshal.GetLastWin32Error()}.");
        _started.Set();
        if (_startError is not null) return;

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_keyboardHook);
        UnhookWindowsHookEx(_mouseHook);
    }

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var timeMs = Clock.NowMs();
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var e = new KeyEvent(
                (int)k.vkCode,
                (int)k.scanCode,
                (k.flags & LLKHF_EXTENDED) != 0,
                (k.flags & LLKHF_UP) != 0,
                timeMs,
                (k.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0);
            try
            {
                var engine = _engine();
                if (!e.IsUp && !e.Injected) CheckForeground(engine);
                if (e.IsUp && !e.Injected && Vk.IsModifier(e.Vk)) ScheduleModifierCheck();
                if (engine.OnKey(e)) return 1;
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook", ex);
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (uint)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                or WM_XBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
        {
            try
            {
                _engine().OnMouseButton();
            }
            catch (Exception ex)
            {
                Log.Error("Mouse hook", ex);
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    // Safety net: after the physical release of a modifier, every modifier that Klava sees released
    // must be released for Windows too. Checking all of them catches a stuck key that is not the
    // released one (the fake AltGr Ctrl stayed down after a Right Alt release in v0.1).
    private void ScheduleModifierCheck()
    {
        if (_modifierTimer == 0) _modifierTimer = SetTimer(0, 0, ModifierCheckDelayMs, _modifierCheckProc);
    }

    private void CheckModifiers(nint hwnd, uint msg, nuint id, uint time)
    {
        KillTimer(0, _modifierTimer);
        _modifierTimer = 0;
        try
        {
            var engine = _engine();
            foreach (var vk in Vk.Modifiers)
            {
                if (engine.IsKeyDown(vk) || (GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                Log.Warn($"Windows reports {KeyNames.Name(vk)} down, but it is not held {ModifierCheckDelayMs} ms after a modifier release; releasing it.");
                var steps = new List<OutputStep>();
                if (vk is Vk.LAlt or Vk.RAlt or Vk.LWin or Vk.RWin)
                {
                    steps.Add(new KeyStroke(Vk.MenuMask, false));
                    steps.Add(new KeyStroke(Vk.MenuMask, true));
                }
                steps.Add(new KeyStroke(vk, true));
                WindowsPlatform.SendSteps(steps);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Modifier check", ex);
        }
    }

    private void CheckForeground(Engine engine)
    {
        var foreground = GetForegroundWindow();
        if (foreground == _foreground) return;
        _foreground = foreground;
        engine.OnForegroundChanged();
    }
}
