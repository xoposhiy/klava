using System.Text;

namespace Klava.Core;

internal sealed class LineStartRetypeHeuristic() : TypingHeuristic("line-start-retype")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode)
    {
        var line = new List<TypingEvent>();
        var heldLetters = new HashSet<int>();
        var firstLine = true;
        foreach (var e in episode.Events)
        {
            if (e.Kind == "key")
            {
                if (e.Blocked || Vk.IsModifier(e.Vk) || e.Vk == Vk.CapsLock) continue;
                if (e.IsUp)
                {
                    // A release without its press can identify one missing key at the start of the recording.
                    if (e.Source != "physical" || !firstLine || e.Vk is < 'A' or > 'Z' || heldLetters.Remove(e.Vk)) continue;
                }
                else if (e.Source == "physical" && e.Vk is >= 'A' and <= 'Z') heldLetters.Add(e.Vk);
            }
            if (PlainKey(e, Vk.Enter))
            {
                if (Match(episode, line) is { } signal) yield return signal;
                line.Clear();
                firstLine = false;
            }
            else line.Add(e);
        }
        if (Match(episode, line) is { } last) yield return last;
    }

    private TypingSignal? Match(TypingEpisode episode, List<TypingEvent> line)
    {
        var i = 0;
        while (i < line.Count && line[i].Kind == "layout") i++;
        if (i == line.Count) return null;
        var originalLayout = line[i].Context.Layout;
        var original = new StringBuilder();
        int? missingKey = null;
        while (i < line.Count && line[i] is { Kind: "key", Source: "physical", Ctrl: false, Alt: false, Win: false } letter &&
            letter.Vk is >= 'A' and <= 'Z' && letter.Context.Layout == originalLayout)
        {
            if (letter.IsUp)
            {
                if (missingKey.HasValue) return null;
                missingKey = letter.Vk;
            }
            else if (letter.Text.Length == 1 && char.IsLetter(letter.Text[0])) original.Append(letter.Text);
            else return null;
            i++;
        }
        var length = original.Length + (missingKey.HasValue ? 1 : 0);
        if (original.Length == 0 || length > 100 || i == line.Count || !PlainKey(line[i], Vk.Space)) return null;

        // Text and paste after the space leave the recorded prefix in place. The clipboard contents are unknown.
        while (i < line.Count && (line[i].Kind == "layout" || IsTypedText(line[i]) ||
            line[i] is { Kind: "key", Source: "physical", Vk: 'V', Ctrl: true, Alt: false, Win: false, Shift: false, IsUp: false })) i++;
        if (i < line.Count && PlainKey(line[i], Vk.Home, shift: true)) i++;
        if (i == line.Count || !PlainKey(line[i++], Vk.Home)) return null;
        var deleted = 0;
        while (i < line.Count)
        {
            if (PlainKey(line[i], Vk.Delete)) deleted++;
            else if (line[i].Kind != "layout") break;
            i++;
        }
        if (deleted != length || i + length > line.Count) return null;

        var replacement = new StringBuilder();
        var replacementLayout = line[i].Context.Layout;
        if (replacementLayout == originalLayout || missingKey.HasValue && missingKey != line[i].Vk) return null;
        for (var end = i + length; i < end; i++)
        {
            var e = line[i];
            if (!IsTypedText(e) || e.Text.Length != 1 || !char.IsAsciiLetter(e.Text[0]) ||
                e.Context.Layout != replacementLayout) return null;
            replacement.Append(e.Text);
        }
        if (i < line.Count && !PlainKey(line[i], Vk.Space)) return null;
        var word = replacement.ToString();
        var observed = original.ToString();
        var expected = TypingLayouts.Convert(missingKey.HasValue ? word[1..] : word, false);
        if (!observed.Equals(expected, StringComparison.OrdinalIgnoreCase)) return null;
        return new(episode.Id, Id, Version, TypingAnalysis.EnglishCandidate(word), observed,
            missingKey.HasValue ? "The first press is missing. Its release and the recorded letters match the retyped word."
                : "The first word was deleted with Home and Delete and retyped in another layout.", missingKey.HasValue);
    }

    private static bool PlainKey(TypingEvent e, int vk, bool shift = false) =>
        e is { Kind: "key", Source: "physical", IsUp: false, Ctrl: false, Alt: false, Win: false } &&
        e.Vk == vk && e.Shift == shift;

    private static bool IsTypedText(TypingEvent e) =>
        e is { Kind: "key", Source: "physical", IsUp: false, Ctrl: false, Alt: false, Win: false, Text.Length: > 0 };
}
