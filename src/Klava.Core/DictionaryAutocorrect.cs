namespace Klava.Core;

public sealed class TypingConfig
{
    public bool Enabled { get; set; } = true;
    public bool Autocorrect { get; set; } = true;
}

/// <summary>Matches completed words against an explicitly approved English dictionary.</summary>
public sealed class DictionaryAutocorrect(TypingConfig config, IPlatform platform)
{
    private HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);
    public void SetApprovedWords(IEnumerable<string> words) =>
        Volatile.Write(ref _words, new(words, StringComparer.OrdinalIgnoreCase));
    private string _word = "";
    private long _layout;
    private bool _overflow;
    public void Reset() { _word = ""; _overflow = false; }

    public TypingCorrection? Observe(in KeyEvent e, KeyState keys, TypingContext context, string text)
    {
        if (!context.CanRecord) { Reset(); return null; }
        if (e.IsUp || Vk.IsShift(e.Vk) || e.Vk == Vk.CapsLock) return null;
        if (keys.Ctrl || keys.Alt || keys.Win) { Reset(); return null; }
        if (context.Layout != _layout) { Reset(); _layout = context.Layout; }
        if (e.Vk == Vk.Backspace)
        {
            if (_word.Length > 0) _word = _word[..^1];
            return null;
        }
        if (text.Length > 0 && text.All(char.IsLetterOrDigit))
        {
            if (_word.Length >= 100) _overflow = true;
            else _word += text;
            return null;
        }
        var original = _word;
        var overflow = _overflow;
        Reset();
        if (!config.Autocorrect || overflow || original.Length == 0 ||
            !(e.Vk is Vk.Space or Vk.Enter || text.Any(char.IsPunctuation))) return null;
        var replacement = TypingLayouts.Convert(original, toEnglish: true);
        if (original == replacement || !Volatile.Read(ref _words).Contains(replacement)) return null;
        var steps = new List<OutputStep>();
        for (var i = 0; i < original.Length; i++)
        {
            steps.Add(new KeyStroke(Vk.Backspace, false));
            steps.Add(new KeyStroke(Vk.Backspace, true));
        }
        steps.Add(new TextStroke(replacement));
        platform.Send(steps);
        return new(original, replacement, "dictionary", "dictionary:" + replacement.ToLowerInvariant());
    }
}
