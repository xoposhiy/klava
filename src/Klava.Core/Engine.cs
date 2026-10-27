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
    private readonly KeyState _keys = new();
    private volatile bool _enabled, _debounceEnabled, _hotkeysEnabled, _switcherEnabled;
    private bool _capsLock;

    public Engine(Settings settings, IPlatform platform, bool capsLockOn)
    {
        var config = settings.Config;
        _platform = platform;
        _capsLock = capsLockOn;
        _debouncer = new Debouncer(config.Debounce.ThresholdMs, settings.DebounceKeyThresholds);
        _hotkeys = new HotkeyEngine(settings.HotkeyRules, platform);
        Switcher = new LayoutSwitcher(platform, config.Switcher.MaxTapMs, config.Switcher.DoubleTapMs,
            config.Switcher.MaxSelectionChars);
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
        if (e.Injected || !_enabled) return false;
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
            if (block) return true;
            if (gap is >= 0 and < FastRepressMs && (Vk.IsPrintable(e.Vk) || Vk.IsModifier(e.Vk)))
                FastRepressPassed?.Invoke(new FastRepress(e.Vk, gap, previousHeld));
        }

        if (!e.IsUp && _keys.Count > 0) Prune(e.TimeMs);
        var repeat = _keys.Update(e);
        if (!e.IsUp && !repeat && e.Vk == Vk.CapsLock) _capsLock = !_capsLock;

        if (_hotkeysEnabled && _hotkeys.Handle(e, repeat, _keys))
        {
            Switcher.Reset();
            return true;
        }

        if (_switcherEnabled) Switcher.Observe(e, repeat, _keys, _capsLock);
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

    public void OnMouseButton() => Switcher.OnMouse();

    public void OnForegroundChanged() => Switcher.Reset();
}
