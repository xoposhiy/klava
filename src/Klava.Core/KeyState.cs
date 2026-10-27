namespace Klava.Core;

/// <summary>Physical keys that are down, as seen after the debounce filter.</summary>
public sealed class KeyState
{
    /// <summary>
    /// Scan code of the fake Left Ctrl that Windows adds to Right Alt on AltGr layouts
    /// (ru-RU on Windows 10 and later is one).
    /// </summary>
    public const int AltGrCtrlScanCode = 0x21D;

    private readonly HashSet<int> _down = [];
    private readonly Dictionary<int, long> _lastEventMs = [];
    private bool _ctrlIsAltGr;

    /// <summary>Applies the event. Returns true when a press is an autorepeat.</summary>
    public bool Update(in KeyEvent e)
    {
        _lastEventMs[e.Vk] = e.TimeMs;
        if (e.IsUp)
        {
            _down.Remove(e.Vk);
            if (e.Vk == Vk.LCtrl) _ctrlIsAltGr = false;
            // A blocked Right Alt release gets no fake Left Ctrl release, so drop the fake Ctrl here.
            if (e.Vk == Vk.RAlt && _ctrlIsAltGr)
            {
                _down.Remove(Vk.LCtrl);
                _ctrlIsAltGr = false;
            }
            return false;
        }

        var repeat = !_down.Add(e.Vk);
        if (e.Vk == Vk.LCtrl && !repeat) _ctrlIsAltGr = e.ScanCode == AltGrCtrlScanCode;
        return repeat;
    }

    /// <summary>Removes keys whose release Klava missed (for example, it went to an elevated window).</summary>
    /// <remarks>Call before <see cref="Update"/>: the OS key state does not include the current event yet.</remarks>
    public void Prune(Func<int, bool> isKeyDown)
    {
        _down.RemoveWhere(vk => !isKeyDown(vk));
        if (!_down.Contains(Vk.LCtrl)) _ctrlIsAltGr = false;
    }

    public bool IsDown(int vk) => _down.Contains(vk);

    /// <summary>Time of the last press, autorepeat or release of the key; long.MinValue if none.</summary>
    public long LastEventMs(int vk) => _lastEventMs.TryGetValue(vk, out var ms) ? ms : long.MinValue;

    public int Count => _down.Count;

    public bool Ctrl => IsDown(Vk.LCtrl) || IsDown(Vk.RCtrl);
    public bool Alt => IsDown(Vk.LAlt) || IsDown(Vk.RAlt);
    public bool Shift => IsDown(Vk.LShift) || IsDown(Vk.RShift);
    public bool Win => IsDown(Vk.LWin) || IsDown(Vk.RWin);

    public List<int> DownModifiers() => Vk.Modifiers.Where(IsDown).ToList();
}
