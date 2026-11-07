using System.Text;

namespace Klava.Core;

public sealed record TypingSignal(string EpisodeId, string Heuristic, string Version,
    string Candidate, string Original, string Description, bool Partial = false);

public interface ITypingHeuristic
{
    string Id { get; }
    string Version { get; }
    IEnumerable<TypingSignal> Analyze(TypingEpisode episode);
}

public static class TypingAnalysis
{
    public static IReadOnlyList<ITypingHeuristic> Heuristics { get; } =
        [new CorrectionHeuristic(), new DictionaryCorrectionHeuristic(), new LayoutVisitHeuristic(), new RetypeHeuristic(false), new RetypeHeuristic(true),
            new LineStartRetypeHeuristic(), new FollowupHeuristic(), new PatternHeuristic()];

    public static string Candidate(string text)
    {
        text = text.Trim();
        var latin = TypingLayouts.Convert(text, true);
        return latin.Length is > 0 and <= 100 && latin.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            ? latin.ToLowerInvariant() : "";
    }

    public static string EnglishCandidate(string text)
    {
        text = text.Trim();
        return text.Length is > 0 and <= 100 && text.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            ? text.ToLowerInvariant() : "";
    }

    public static IEnumerable<string> Words(IEnumerable<TypingEvent> events)
    {
        var word = new StringBuilder();
        foreach (var e in events)
        {
            if (e.Kind is "mouse" or "layout")
            {
                if (word.Length > 0) yield return word.ToString();
                word.Clear();
                continue;
            }
            if (e.Kind != "key" || e.IsUp || e.Blocked || e.Source != "physical") continue;
            if (e.Ctrl || e.Alt || e.Win) { word.Clear(); continue; }
            if (e.Vk == Vk.Backspace) { if (word.Length > 0) word.Length--; continue; }
            foreach (var c in e.Text)
            {
                if (char.IsLetter(c)) { if (word.Length < 101) word.Append(c); }
                else if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
            }
            if (e.Vk is Vk.Enter or Vk.Tab && word.Length > 0) { yield return word.ToString(); word.Clear(); }
        }
        if (word.Length > 0) yield return word.ToString();
    }
}

internal abstract class TypingHeuristic(string id, string version = "1") : ITypingHeuristic
{
    public string Id => id;
    public string Version => version;
    public abstract IEnumerable<TypingSignal> Analyze(TypingEpisode episode);
    protected TypingSignal Signal(TypingEpisode episode, string text, string original, string description) =>
        new(episode.Id, Id, Version, TypingAnalysis.EnglishCandidate(text), original, description);
}

internal sealed class CorrectionHeuristic() : TypingHeuristic("shift-shift")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode) => episode.Events
        .Where(e => e.Correction?.Type is "shift-shift" or "shift-shift-selection")
        .Select(e => Signal(episode, e.Correction!.Replacement, e.Correction.Original, "Shift-Shift converted a word."));
}

internal sealed class DictionaryCorrectionHeuristic() : TypingHeuristic("dictionary")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode) => episode.Events
        .Where(e => e.Correction?.Type == "dictionary")
        .Select(e => Signal(episode, e.Correction!.Replacement, e.Correction.Original, "Dictionary corrected a completed word."));
}

internal sealed class LayoutVisitHeuristic() : TypingHeuristic("temporary-layout", "2")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode)
    {
        var events = episode.Events;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Kind != "layout" || events[i].PreviousLayout == 0) continue;
            var end = i + 1;
            while (end < events.Count && !(events[end].Kind == "layout" && events[end].Context.Layout == events[i].PreviousLayout)) end++;
            if (end == events.Count) continue;
            var visit = events.Skip(i + 1).Take(end - i - 1).ToArray();
            if (visit.Any(e => e.Correction is not null || e.Kind == "mouse" ||
                e is { Kind: "key", Source: "physical", IsUp: false, Blocked: false } &&
                (e.Ctrl || e.Alt || e.Win || e.Vk is Vk.Backspace or >= 0x21 and <= 0x2E))) continue;
            // The first Shift of Shift-Shift returns to the earlier layout before conversion runs.
            var afterReturn = events.Skip(end + 1).TakeWhile(e => e.Kind != "layout" &&
                !(e is { Kind: "key", Source: "physical", IsUp: false, Blocked: false } &&
                    !Vk.IsModifier(e.Vk) && e.Vk != Vk.CapsLock));
            if (afterReturn.Any(e => e.Correction?.Type == "shift-shift")) continue;
            var words = TypingAnalysis.Words(visit).ToArray();
            if (words.Length != 1 || words[0].Length > 20) continue;
            yield return Signal(episode, words[0], "", "A short word was typed before returning to the earlier layout.");
        }
    }
}

internal sealed class RetypeHeuristic(bool requireSwitch) : TypingHeuristic(requireSwitch ? "switch-retype" : "delete-retype")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode)
    {
        var events = episode.Events;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not { Kind: "key", Vk: Vk.Backspace, IsUp: false, Blocked: false, Source: "physical" }) continue;
            var before = TypingAnalysis.Words(events.Take(i)).LastOrDefault();
            if (before is null) continue;
            var end = i;
            var deleted = 0;
            while (end < events.Count && (events[end].Text.Length == 0 || events[end].Vk == Vk.Backspace))
            {
                if (events[end] is { Vk: Vk.Backspace, IsUp: false, Blocked: false, Source: "physical" }) deleted++;
                end++;
            }
            if (deleted < before.Length || end == events.Count) continue;
            var after = TypingAnalysis.Words(events.Skip(end)).FirstOrDefault();
            if (after is null || before == after || TypingAnalysis.Candidate(before) != TypingAnalysis.Candidate(after)) continue;
            var lastTextTime = events.Take(i).LastOrDefault(e => e.Kind == "key" && !e.IsUp && e.Text.Length > 0)?.TimeMs ?? 0;
            var switched = events.Take(end + 1).Any(e => e.Kind == "layout" && e.TimeMs >= lastTextTime);
            if (requireSwitch && !switched) continue;
            yield return Signal(episode, after, before, requireSwitch
                ? "A layout switch was followed by retyping a deleted word."
                : "A word was deleted and retyped with the same physical letters in another layout.");
        }
    }
}

internal sealed class FollowupHeuristic() : TypingHeuristic("correction-followup", "2")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode)
    {
        for (var i = 0; i < episode.Events.Count; i++)
        {
            if (episode.Events[i].Correction is not { } correction) continue;
            if (TouchesCorrection(episode, i, correction))
                yield return Signal(episode, correction.Replacement, correction.Original,
                    "Possible false positive: Backspace reached the corrected word, or Undo immediately followed the correction.");
        }
    }

    private static bool TouchesCorrection(TypingEpisode episode, int index, TypingCorrection correction)
    {
        var trigger = episode.Events[index];
        var suffix = correction.Replacement[correction.Replacement.TrimEnd().Length..];
        if (correction.Type == "dictionary")
        {
            if (trigger.Vk == Vk.Enter) return false;
            suffix += episode.Events.Take(index).LastOrDefault(e => e.Kind == "key" && e.Source == "physical" &&
                !e.IsUp && e.TimeMs == trigger.TimeMs && e.Vk == trigger.Vk)?.Text ?? "";
        }
        var typedAfter = false;
        foreach (var e in episode.Events.Skip(index + 1).TakeWhile(e => e.TimeMs - trigger.TimeMs <= 3000))
        {
            if (e.Kind == "mouse" || e.Correction is not null) break;
            if (e is not { Kind: "key", Source: "physical", IsUp: false, Blocked: false }) continue;
            if (Vk.IsModifier(e.Vk) || e.Vk == Vk.CapsLock) continue;
            if (e.Vk == 0x5A && e.Ctrl && !e.Alt && !e.Win) return !typedAfter;
            if (e.Vk == Vk.Backspace && !e.Alt && !e.Win)
            {
                if (suffix.Length == 0 || e.Ctrl && string.IsNullOrWhiteSpace(suffix)) return true;
                if (e.Ctrl)
                {
                    suffix = suffix.TrimEnd();
                    while (suffix.Length > 0 && !char.IsWhiteSpace(suffix[^1])) suffix = suffix[..^1];
                }
                else suffix = suffix[..^1];
                continue;
            }
            if (e.Ctrl || e.Alt || e.Win || e.Vk is Vk.Enter or Vk.Tab or Vk.Esc or >= 0x21 and <= 0x2D) break;
            if (e.Vk == 0x2E) continue;
            if (e.Text.Length == 0) break;
            suffix += e.Text;
            typedAfter = true;
        }
        return false;
    }
}

internal sealed class PatternHeuristic() : TypingHeuristic("edit-pattern")
{
    public override IEnumerable<TypingSignal> Analyze(TypingEpisode episode)
    {
        var edits = episode.Events.Count(e => !e.IsUp && !e.Blocked && e.Source == "physical" && e.Vk is Vk.Backspace or 0x2E);
        var switches = episode.Events.Count(e => e.Kind == "layout");
        if (edits + switches >= 2)
            yield return new(episode.Id, Id, Version, "", "", $"Edits: {edits}; layout switches: {switches}.");
    }
}
