namespace Klava.Core;

/// <summary>A blocked bounce, for stats and the log.</summary>
public readonly record struct Bounce(int Vk, long GapMs);

/// <summary>A fast re-press of a printable key or a modifier that the filter let through, for tuning thresholds.</summary>
public readonly record struct FastRepress(int Vk, long GapMs, long PreviousHeldMs);

/// <summary>
/// Runs every physical key event through the stages from docs/spec.md:
/// debounce, key state, hotkeys, layout switcher.
/// </summary>
public sealed class Engine
{
    private readonly IPlatform _platform;
    private readonly Debouncer _debouncer;
    private readonly HotkeyEngine _hotkeys;
    private readonly DictionaryAutocorrect _autocorrect;
    private readonly bool _typingEnabled;
    private TypingContext? _typingContext;
    private KeyEvent _typingKey;
    private long? _requestedLayout;
    private readonly KeyState _keys = new();
    private readonly HashSet<int> _physicalDown = [];
    private volatile bool _enabled, _debounceEnabled, _hotkeysEnabled, _switcherEnabled;

    public Engine(Settings settings, IPlatform platform)
    {
        var config = settings.Config;
        _platform = platform;
        _debouncer = new Debouncer(config.Debounce.ThresholdMs, settings.DebounceKeyThresholds);
        _hotkeys = new HotkeyEngine(settings.HotkeyRules, platform);
        _typingEnabled = config.Typing.Enabled;
        _autocorrect = new DictionaryAutocorrect(config.Typing, platform);
        Switcher = new LayoutSwitcher(platform, config.Switcher.MaxTapMs, config.Switcher.DoubleTapMs,
            config.Switcher.MaxSelectionChars);
        Switcher.LayoutChanged += (from, to) =>
        {
            _requestedLayout = (long)to;
            EmitTyping("layout", reason: "shift-tap", previousLayout: (long)from, layout: (long)to, trigger: true);
        };
        Switcher.TextConverted += correction => EmitTyping("correction", correction: correction, trigger: true);
        if (settings.TraceKeys.Count > 0) Tracer = new KeyTracer(settings.TraceKeys);
        _enabled = config.Enabled;
        _debounceEnabled = config.Debounce.Enabled;
        _hotkeysEnabled = config.Hotkeys.Enabled;
        _switcherEnabled = config.Switcher.Enabled;
    }

    /// <summary>A passed re-press sooner than this is reported by <see cref="FastRepressPassed"/>.</summary>
    public const int FastRepressMs = 100;

    /// <summary>Raised on the hook thread for every new blocked bounce.</summary>
    public event Action<Bounce>? BounceBlocked;

    /// <summary>Raised on the hook thread for a passed re-press of a printable key or a modifier within <see cref="FastRepressMs"/>.</summary>
    public event Action<FastRepress>? FastRepressPassed;

    /// <summary>Every supported non-injected press, including bounces, excluding hold repeats. Also collected while paused.</summary>
    public event Action<int>? PressObserved;
    public event Action<TypingEvent>? TypingObserved;
    public void SetApprovedTypingWords(IEnumerable<string> words) => _autocorrect.SetApprovedWords(words);
    public void ObserveExternalTyping(TypingEvent e)
    {
        if (_typingEnabled && _enabled && e.Context.CanRecord) TypingObserved?.Invoke(e);
    }

    public LayoutSwitcher Switcher { get; }

    /// <summary>Set when <c>debug.traceKeys</c> is not empty.</summary>
    public KeyTracer? Tracer { get; }

    // The tray menu changes these flags from the UI thread.
    public bool Enabled { get => _enabled; set => _enabled = value; }
    public bool DebounceEnabled { get => _debounceEnabled; set => _debounceEnabled = value; }
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => _hotkeysEnabled = value; }
    public bool SwitcherEnabled { get => _switcherEnabled; set => _switcherEnabled = value; }

    /// <summary>Returns true to block the event.</summary>
    public bool OnKey(in KeyEvent e)
    {
        if (e.Vk is <= 0 or >= 255) return false;
        if (e.Injected)
        {
            if (_typingEnabled && _enabled)
                EmitTyping("key", text: !e.IsUp && e.Vk == 0xE7 ? ((char)e.ScanCode).ToString() : "",
                    source: e.OwnInput ? "klava" : "injected", observedContext: _platform.TypingContext, observedKey: e);
            if (!e.OwnInput) _autocorrect.Reset();
            return false;
        }
        if (_typingEnabled && _enabled)
        {
            var context = _platform.TypingContext;
            _typingKey = e;
            if (_typingContext is { } old && (old.Window != context.Window || old.Focus != context.Focus || old.CanRecord != context.CanRecord))
            {
                _autocorrect.Reset();
                if (old.Window != context.Window || old.Focus != context.Focus) Switcher.Reset();
            }
            if (_typingContext is { } previous && previous.Layout != context.Layout)
            {
                _typingContext = context;
                if (_requestedLayout != context.Layout)
                {
                    EmitTyping("layout", reason: "observed", previousLayout: previous.Layout, trigger: true);
                    Switcher.Reset();
                }
                _requestedLayout = null;
                _autocorrect.Reset();
            }
            _typingContext = context;
        }
        if (e.IsUp) _physicalDown.Remove(e.KeyId);
        else if (_physicalDown.Add(e.KeyId)) PressObserved?.Invoke(e.Vk);
        if (!_enabled)
        {
            if (_typingContext is { } previous)
                TypingObserved?.Invoke(new(e.TimeMs, previous with { CanRecord = false }, "pause"));
            _autocorrect.Reset();
            _typingContext = null;
            return false;
        }
        var block = Process(e);
        Tracer?.Observe(e, block);
        return block;
    }

    private bool Process(in KeyEvent e)
    {
        if (_debounceEnabled)
        {
            var block = _debouncer.ShouldBlock(e, out var gap, out var previousHeld);
            if (block && gap >= 0) BounceBlocked?.Invoke(new Bounce(e.Vk, gap));
            if (block) { EmitTyping("key", blocked: true); return true; }
            if (gap is >= 0 and < FastRepressMs && (Vk.IsPrintable(e.Vk) || Vk.IsModifier(e.Vk)))
                FastRepressPassed?.Invoke(new FastRepress(e.Vk, gap, previousHeld));
        }

        if (!e.IsUp && _keys.Count > 0) Prune(e.TimeMs);
        var repeat = _keys.Update(e);

        if (_hotkeysEnabled && _hotkeys.Handle(e, repeat, _keys))
        {
            Switcher.Reset();
            _autocorrect.Reset();
            EmitTyping("key", blocked: true);
            return true;
        }

        var text = !e.IsUp && !_keys.Ctrl && !_keys.Alt && !_keys.Win && Vk.IsPrintable(e.Vk)
            && _typingContext is { CanRecord: true } context
            ? _platform.TypeText([new(e.Vk, e.ScanCode, _keys.Shift, _platform.CapsLockOn)], (nint)context.Layout) : "";
        EmitTyping("key", text: text, trigger: e.Vk is Vk.Backspace or 0x2E || Vk.IsShift(e.Vk));
        if (_typingEnabled && _typingContext is { } typingContext &&
            _autocorrect.Observe(e, _keys, typingContext, text) is { } correction)
        {
            EmitTyping("correction", correction: correction, trigger: true);
            Switcher.Reset();
        }
        if (_switcherEnabled) Switcher.Observe(e, repeat, _keys, _platform.CapsLockOn);
        return false;
    }

    /// <summary>Raised after Klava sends input for a hotkey: the description and the sent steps.</summary>
    public event Action<string, IReadOnlyList<OutputStep>>? HotkeySent
    {
        add => _hotkeys.Sent += value;
        remove => _hotkeys.Sent -= value;
    }

    /// <summary>True when Klava saw the press of the key and not yet its release.</summary>
    public bool IsKeyDown(int vk) => _keys.IsDown(vk);

    /// <summary>
    /// Drops keys whose release Klava missed. Windows reports a modifier that Klava released for a
    /// hotkey as up even while the user holds it, so for those the proof of a hold is autorepeat:
    /// a held key repeats at least every second (the longest Windows repeat delay).
    /// </summary>
    private void Prune(long nowMs)
    {
        _keys.Prune(vk => _platform.IsKeyDown(vk)
            || _hotkeys.IsReleased(vk) && nowMs - _keys.LastEventMs(vk) < ReleasedHoldTimeoutMs);
        _hotkeys.ForgetReleased(_keys);
    }

    private const int ReleasedHoldTimeoutMs = 1500;

    public void OnMouseButton() { Switcher.OnMouse(); _autocorrect.Reset(); }
    public void OnMouseInput(long timeMs, string kind, int x, int y, bool injected, bool own)
    {
        if (!injected) OnMouseButton();
        if (!_typingEnabled || !_enabled) return;
        TypingObserved?.Invoke(new(timeMs, _platform.TypingContext, "mouse", own ? "klava" : injected ? "injected" : "physical",
            Trigger: !injected && kind != "move", Reason: kind, X: x, Y: y));
    }

    public void OnForegroundChanged() { Switcher.Reset(); _autocorrect.Reset(); }

    private void EmitTyping(string kind, string text = "", bool blocked = false, bool trigger = false,
        string? reason = null, TypingCorrection? correction = null, string source = "physical",
        long previousLayout = 0, long? layout = null, TypingContext? observedContext = null, KeyEvent? observedKey = null)
    {
        if (!_typingEnabled || !_enabled || (observedContext ?? _typingContext) is not { } context) return;
        var key = observedKey ?? _typingKey;
        if (!context.CanRecord)
        {
            TypingObserved?.Invoke(new(key.TimeMs, context, "privacy"));
            return;
        }
        if (correction is not null) source = "klava";
        if (layout is { } value) context = context with { Layout = value };
        TypingObserved?.Invoke(new(key.TimeMs, context, kind, source, key.Vk, key.ScanCode,
            key.Extended, key.IsUp, text, blocked, trigger, reason, correction, PreviousLayout: previousLayout,
            Shift: _keys.Shift, Ctrl: _keys.Ctrl, Alt: _keys.Alt, Win: _keys.Win, CapsLock: _platform.CapsLockOn));
    }
}
