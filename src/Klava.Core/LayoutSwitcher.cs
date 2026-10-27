namespace Klava.Core;

/// <summary>
/// Single Shift tap switches the layout; double tap retypes the last word in the new layout,
/// or converts the selected text when there is no last word. See "Layout switcher" in docs/spec.md.
/// </summary>
/// <param name="maxSelectionChars">0 turns off the selected text conversion.</param>
public sealed class LayoutSwitcher(IPlatform platform, int maxTapMs, int doubleTapMs, int maxSelectionChars)
{
    private const int BufferCapacity = 100;

    private readonly List<TypedKey> _word = [];
    private (int Vk, long DownMs)? _tap;
    private (long UpMs, nint From, nint To)? _lastTap;
    private bool _clearWordOnNextKey;

    /// <summary>Raised after a single tap switched the layout.</summary>
    public event Action? Switched;

    /// <summary>Raised after a double tap retyped a word; the argument is the number of keys retyped.</summary>
    public event Action<int>? Converted;
    public int WordLength => _word.Count;

    /// <param name="keys">Key state that already includes the event.</param>
    public void Observe(in KeyEvent e, bool repeat, KeyState keys, bool capsLock)
    {
        if (!e.IsUp)
        {
            OnPress(e, repeat, keys, capsLock);
            return;
        }

        if (_tap is not { } tap || tap.Vk != e.Vk) return;
        _tap = null;

        if (e.TimeMs - tap.DownMs > maxTapMs)
        {
            _lastTap = null;
            return;
        }

        if (_lastTap is { } last && tap.DownMs - last.UpMs <= doubleTapMs)
        {
            _lastTap = null;
            if (!ConvertLastWord(last.To)) ConvertSelection(last.From, last.To);
            return;
        }

        var (from, to) = platform.SwitchToNextLayout();
        _lastTap = (e.TimeMs, from, to);
        _clearWordOnNextKey = true;
        Switched?.Invoke();
    }

    /// <summary>A mouse click or wheel: cancels taps and clears the word.</summary>
    public void OnMouse()
    {
        Reset();
    }

    /// <summary>Clears taps and the word, for example after a hotkey or a foreground window change.</summary>
    public void Reset()
    {
        _tap = null;
        _lastTap = null;
        _clearWordOnNextKey = false;
        _word.Clear();
    }

    private void OnPress(in KeyEvent e, bool repeat, KeyState keys, bool capsLock)
    {
        if (Vk.IsShift(e.Vk))
        {
            if (!repeat) _tap = keys.Count == 1 ? (e.Vk, e.TimeMs) : null;
            return;
        }

        _tap = null;
        _lastTap = null;
        if (e.Vk == Vk.CapsLock) return;

        var chord = keys.Ctrl || keys.Alt || keys.Win;
        if (!chord && e.Vk == Vk.Backspace)
        {
            if (_word.Count > 0) _word.RemoveAt(_word.Count - 1);
            return;
        }

        if (!chord && Vk.IsPrintable(e.Vk))
        {
            if (_clearWordOnNextKey) _word.Clear();
            _clearWordOnNextKey = false;
            if (_word.Count == BufferCapacity) _word.RemoveAt(0);
            _word.Add(new TypedKey(e.Vk, e.ScanCode, keys.Shift, capsLock));
            return;
        }

        _word.Clear();
        _clearWordOnNextKey = false;
    }

    private bool ConvertLastWord(nint layout)
    {
        var keys = LastWordWithTrailingSpaces();
        if (keys.Count == 0) return false;

        var text = platform.TypeText(keys, layout);
        var steps = new List<OutputStep>(keys.Count * 2 + 1);
        for (var i = 0; i < keys.Count; i++)
        {
            steps.Add(new KeyStroke(Vk.Backspace, false));
            steps.Add(new KeyStroke(Vk.Backspace, true));
        }
        steps.Add(new TextStroke(text));
        platform.Send(steps);

        // The user may go on typing the same word in the new layout.
        _clearWordOnNextKey = false;
        Converted?.Invoke(keys.Count);
        return true;
    }

    private void ConvertSelection(nint from, nint to)
    {
        if (maxSelectionChars <= 0) return;
        // The word buffer is empty here and stays empty: the paste is Klava's own marked input.
        platform.ConvertSelection(from, to, maxSelectionChars);
    }

    private List<TypedKey> LastWordWithTrailingSpaces()
    {
        var i = _word.Count;
        while (i > 0 && _word[i - 1].Vk == Vk.Space) i--;
        var wordEnd = i;
        while (i > 0 && _word[i - 1].Vk != Vk.Space) i--;
        return i == wordEnd ? [] : _word.GetRange(i, _word.Count - i);
    }
}
