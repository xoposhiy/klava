using Klava.Core;

namespace Klava.Core.Tests;

public class TypingTests
{
    private static Keyboard Recording(string? config = null)
    {
        var keyboard = new Keyboard(config);
        keyboard.Platform.RecordTyping = true;
        return keyboard;
    }

    [Theory]
    [InlineData("llm", "llm")]
    [InlineData("ast", "ast")]
    [InlineData("ai", "ai")]
    [InlineData("http", "http")]
    [InlineData("markdown", "markdown")]
    public void Dictionary_corrects_only_at_a_word_boundary_and_keeps_the_layout(string keys, string expected)
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords([expected]);
        var observed = new List<TypingEvent>();
        k.Engine.TypingObserved += observed.Add;
        k.ShiftTap();
        k.Type(keys);
        Assert.Empty(k.Platform.Sent);
        Assert.False(k.Down(Vk.Space));
        Assert.Equal(expected, k.Platform.SentText);
        Assert.Equal(keys.Length, k.Platform.SentBackspaces);
        Assert.Equal(2, k.Platform.Layout);
        Assert.Equal(1, k.Platform.Switches);
        Assert.Contains(observed, e => e.Correction is { Type: "dictionary" } c && c.Replacement == expected);
    }

    [Theory]
    [InlineData(Vk.Enter)]
    [InlineData(0xBF)]
    public void Enter_and_punctuation_finish_a_word_without_blocking_the_physical_event(int boundary)
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords(["ast"]);
        k.ShiftTap().Type("ast");
        Assert.False(k.Down(boundary));
        Assert.Equal("ast", k.Platform.SentText);
    }

    [Fact]
    public void Dictionary_preserves_uppercase_and_handles_backspace()
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords(["llm"]);
        k.ShiftTap().Down(Vk.LShift);
        k.Type("llx").Tap(Vk.Backspace).Type("m ");
        Assert.Equal("LLM", k.Platform.SentText);
    }

    [Theory]
    [InlineData("llmx ")]
    [InlineData("ordinary ")]
    [InlineData("llm1 ")]
    public void Dictionary_does_not_correct_nonmatching_words(string input)
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords(["llm"]);
        k.ShiftTap().Type(input);
        Assert.Empty(k.Platform.Sent);
    }

    [Fact]
    public void Mouse_focus_changes_chords_and_private_fields_clear_the_pending_word()
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords(["llm"]);
        k.ShiftTap().Type("llm");
        k.Engine.OnMouseButton();
        k.Type(" ");
        Assert.Empty(k.Platform.Sent);
        k.Type("llm");
        k.Platform.Focus++;
        k.Type(" ");
        Assert.Empty(k.Platform.Sent);
        k.Type("llm");
        k.Platform.RecordTyping = false;
        var events = new List<TypingEvent>();
        k.Engine.TypingObserved += events.Add;
        k.Type(" ");
        Assert.Empty(k.Platform.Sent);
        Assert.All(events, e => { Assert.Empty(e.Text); Assert.Null(e.Correction); Assert.Equal(0, e.Vk); });
    }

    [Fact]
    public void Disabled_collection_and_disabled_autocorrection_are_independent()
    {
        var k = Recording("""{"typing":{"enabled":true,"autocorrect":false}}""");
        k.Engine.SetApprovedTypingWords(["llm"]);
        var events = new List<TypingEvent>();
        k.Engine.TypingObserved += events.Add;
        k.ShiftTap().Type("llm ");
        Assert.Empty(k.Platform.Sent);
        Assert.NotEmpty(events);
        var off = Recording("""{"typing":{"enabled":false}}""");
        off.Engine.SetApprovedTypingWords(["llm"]);
        off.Engine.TypingObserved += _ => Assert.Fail("Collection is disabled.");
        off.ShiftTap().Type("llm ");
        Assert.Empty(off.Platform.Sent);
    }

    [Fact]
    public void Injected_input_is_observed_but_never_corrected_or_blocked()
    {
        var k = Recording();
        var events = new List<TypingEvent>();
        k.Engine.TypingObserved += events.Add;
        Assert.False(k.Engine.OnKey(new(0xE7, 'x', false, false, k.Now, true, true)));
        Assert.Equal("klava", Assert.Single(events).Source);
        Assert.Equal("x", events[0].Text);
        Assert.Empty(k.Platform.Sent);
    }

    [Fact]
    public void Approved_words_take_effect_without_losing_held_modifiers()
    {
        var k = Recording();
        k.ShiftTap().Type("ast ");
        Assert.Empty(k.Platform.Sent);
        k.Down(Vk.LShift);
        k.Engine.SetApprovedTypingWords(["ast"]);
        Assert.True(k.Engine.IsKeyDown(Vk.LShift));
        k.Type("ast ");
        Assert.Equal("AST", k.Platform.SentText);
    }

    [Fact]
    public void Episodes_merge_triggers_keep_twenty_chars_and_end_after_idle()
    {
        var k = Recording();
        var collector = new TypingEpisodeCollector();
        var episodes = new List<TypingEpisode>();
        k.Engine.TypingObserved += collector.Observe;
        collector.Completed += episodes.Add;
        k.Type("abcdefghijklmnopqrstuvwxyz").Tap(Vk.Backspace).Type("abcdefghijklmnopqrstuv").Tap(Vk.Backspace);
        Assert.Empty(episodes);
        collector.Advance(k.Now + 3000);
        var episode = Assert.Single(episodes);
        Assert.Equal("idle", episode.EndReason);
        Assert.Equal("ghijklmnopqrstuvwxyz", string.Concat(episode.Events.TakeWhile(e => !e.Trigger).Select(e => e.Text)));
        Assert.True(episode.Events.Count(e => e.Trigger && !e.IsUp) >= 2);
    }

    [Fact]
    public void Idle_and_private_focus_prevent_context_from_crossing_boundaries()
    {
        var k = Recording();
        var collector = new TypingEpisodeCollector();
        var episodes = new List<TypingEpisode>();
        collector.Completed += episodes.Add;
        k.Engine.TypingObserved += collector.Observe;
        k.Type("secret");
        k.Platform.RecordTyping = false;
        k.Type("password");
        k.Platform.RecordTyping = true;
        k.Type("public").Tap(Vk.Backspace);
        collector.Advance(k.Now + 3000);
        Assert.Equal("public", string.Concat(Assert.Single(episodes).Events.Select(e => e.Text)));
    }

    [Fact]
    public void Shift_shift_saves_original_and_replacement_for_analysis()
    {
        var k = Recording();
        var collector = new TypingEpisodeCollector();
        var episodes = new List<TypingEpisode>();
        k.Engine.TypingObserved += collector.Observe;
        collector.Completed += episodes.Add;
        k.ShiftTap().Wait(500).Type("http").ShiftTap().ShiftTap();
        collector.Advance(k.Now + 3000);
        var correction = Assert.Single(episodes.SelectMany(e => e.Events), e => e.Correction is not null).Correction!;
        Assert.Equal("реез", correction.Original);
        Assert.Equal("http", correction.Replacement);
        Assert.Contains(TypingAnalysis.Heuristics.SelectMany(h => h.Analyze(episodes[0])), s => s.Candidate == "http" && s.Heuristic == "shift-shift");
    }

    [Theory]
    [InlineData("{\"typing\":null}")]
    [InlineData("{\"typing\":{\"words\":null}}")]
    [InlineData("{\"typing\":{\"words\":[null]}}")]
    [InlineData("{\"typing\":{\"words\":[\"two words\"]}}")]
    public void Invalid_typing_config_is_rejected(string config) => Assert.Throws<ConfigException>(() => KlavaConfig.Load(config));

    private static List<TypingSignal> AnalyzeKeys(Action<Keyboard> type)
    {
        var k = Recording("""{"typing":{"autocorrect":false}}""");
        var collector = new TypingEpisodeCollector();
        var episodes = new List<TypingEpisode>();
        k.Engine.TypingObserved += collector.Observe;
        collector.Completed += episodes.Add;
        type(k);
        collector.Advance(k.Now + 3000);
        return episodes.SelectMany(e => TypingAnalysis.Heuristics.SelectMany(h => h.Analyze(e))).ToList();
    }

    [Fact]
    public void Analysis_detects_a_short_English_word_between_layout_switches()
    {
        var signals = AnalyzeKeys(k => k.ShiftTap().Wait(500).ShiftTap().Type("http ").Wait(500).ShiftTap());
        Assert.Contains(signals, s => s.Heuristic == "temporary-layout" && s.Candidate == "http");
    }

    [Fact]
    public void Analysis_detects_deletion_and_retyping_after_a_layout_switch()
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Type("http");
            for (var i = 0; i < 4; i++) k.Tap(Vk.Backspace);
            k.ShiftTap().Type("http ");
        });
        Assert.Contains(signals, s => s.Heuristic == "delete-retype" && s.Candidate == "http");
        Assert.Contains(signals, s => s.Heuristic == "switch-retype" && s.Candidate == "http");
        Assert.Contains(signals, s => s.Heuristic == "edit-pattern");
    }

    [Fact]
    public void Recorded_command_prefix_is_found_after_paste_and_two_forward_deletes()
    {
        var signals = AnalyzeKeys(k =>
        {
            k.Platform.SwitchToNextLayout();
            k.Down('C');
            k.Wait(45).Down('D');
            k.Wait(49).Up('C');
            k.Wait(77).Up('D');
            k.Wait(54).Tap(Vk.Space, 90, 211).Down(Vk.LCtrl);
            k.Wait(111).Tap('V', 75, 52).Up(Vk.LCtrl);
            k.Wait(421).Down(Vk.LShift);
            k.Wait(105).Tap(Vk.Home, 75, 44).Up(Vk.LShift);
            k.Wait(256).Tap(Vk.Home, 75, 225).Tap(Vk.Delete, 52, 75).Tap(Vk.Delete, 68, 225);
            k.ShiftTap(105, 187).Down('C');
            k.Wait(75).Down('D');
            k.Wait(45).Up('C');
            k.Wait(60).Up('D');
            k.Wait(173).Tap(Vk.Enter);
        });
        var edit = Assert.Single(signals, s => s.Heuristic == "line-start-retype");
        Assert.Equal(("cd", "св", false), (edit.Candidate, edit.Original, edit.Partial));
    }

    [Theory]
    [InlineData('C', true)]
    [InlineData('X', false)]
    [InlineData(0, false)]
    public void Partial_command_requires_a_release_for_the_missing_first_press(int release, bool expected)
    {
        var signals = AnalyzeKeys(k =>
        {
            k.Platform.SwitchToNextLayout();
            k.Down('D');
            k.Wait(98);
            if (release != 0) k.Up(release);
            k.Wait(32).Up('D');
            k.Wait(80).Tap(Vk.Space, 99, 367).Down(Vk.LCtrl);
            k.Wait(599).Tap('V', 61, 157).Up(Vk.LCtrl);
            k.Wait(660).Tap(Vk.Home, 83, 195).Tap(Vk.Delete, 45, 75).Tap(Vk.Delete, 60, 247);
            k.ShiftTap(105, 518).Tap('C', 75, 97).Tap('D', 68, 382).Tap(Vk.Enter);
        });
        var edits = signals.Where(s => s.Heuristic == "line-start-retype").ToList();
        if (expected)
        {
            var edit = Assert.Single(edits);
            Assert.Equal(("cd", "в", true), (edit.Candidate, edit.Original, edit.Partial));
        }
        else Assert.Empty(edits);
    }

    [Theory]
    [InlineData("dir", 3, Vk.Home, true)]
    [InlineData("dir", 2, Vk.Home, false)]
    [InlineData("dir", 4, Vk.Home, false)]
    [InlineData("dir", 3, 0x25, false)]
    [InlineData("div", 3, Vk.Home, false)]
    [InlineData("dirs", 3, Vk.Home, false)]
    public void Line_start_retyping_matches_only_the_whole_deleted_prefix(string replacement, int deletes, int move, bool expected)
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Type("dir name").Tap(move);
            for (var i = 0; i < deletes; i++) k.Tap(Vk.Delete);
            k.ShiftTap().Type(replacement).Tap(Vk.Enter);
        });
        Assert.Equal(expected, signals.Any(s => s.Heuristic == "line-start-retype"));
    }

    [Fact]
    public void Analysis_records_an_edit_after_dictionary_correction_as_a_possible_false_positive()
    {
        var k = Recording();
        k.Engine.SetApprovedTypingWords(["llm"]);
        var collector = new TypingEpisodeCollector();
        var signals = new List<TypingSignal>();
        k.Engine.TypingObserved += collector.Observe;
        collector.Completed += episode => signals.AddRange(TypingAnalysis.Heuristics.SelectMany(h => h.Analyze(episode)));
        k.ShiftTap().Type("llm ").Tap(Vk.Backspace).Tap(Vk.Backspace);
        collector.Advance(k.Now + 3000);
        Assert.Contains(signals, s => s.Heuristic == "correction-followup" && s.Candidate == "llm");
    }

    [Fact]
    public void Russian_replacements_do_not_propose_English_dictionary_words()
    {
        var signals = AnalyzeKeys(k => k.Type("http").ShiftTap().ShiftTap());
        Assert.DoesNotContain(signals, s => s.Heuristic == "shift-shift" && s.Candidate.Length > 0);
    }

    [Theory]
    [InlineData("jhjifz")]
    [InlineData("gjnjv")]
    [InlineData("'nj")]
    [InlineData("bcgjk")]
    public void A_visit_ending_in_Shift_shift_does_not_propose_the_discarded_input(string keys)
    {
        var signals = AnalyzeKeys(k => k.ShiftTap().Wait(500).ShiftTap().Type(keys).ShiftTap().ShiftTap());
        Assert.DoesNotContain(signals, s => s.Heuristic == "temporary-layout" && s.Candidate.Length > 0);
    }

    [Fact]
    public void Recorded_Shift_shift_sequence_does_not_treat_the_next_Russian_word_as_an_English_visit()
    {
        var signals = AnalyzeKeys(k =>
        {
            k.Platform.SwitchToNextLayout();
            k.Type("llm");
            (long Time, int Vk, bool Up)[] sequence =
            [
                (382635647, Vk.LShift, false), (382635722, Vk.LShift, true), (382635729, Vk.LCtrl, true),
                (382635797, Vk.LShift, false), (382635865, Vk.LShift, true),
                (382636307, Vk.Space, false), (382636382, Vk.Space, true),
                (382637117, 'G', false), (382637184, 'G', true),
                (382637192, 'J', false), (382637252, 'J', true),
                (382637327, 'N', false), (382637379, 'N', true),
                (382637447, 'J', false), (382637522, 'J', true),
                (382637552, 'V', false), (382637627, 'V', true),
                (382637912, Vk.LShift, false), (382637979, Vk.LShift, true),
                (382638054, Vk.LShift, false), (382638122, Vk.LShift, true)
            ];
            foreach (var (time, vk, up) in sequence)
            {
                k.Wait(time - k.Now);
                if (up) k.Up(vk); else k.Down(vk);
            }
        });
        Assert.Contains(signals, s => s.Heuristic == "shift-shift" && s.Candidate == "llm");
        Assert.DoesNotContain(signals, s => s.Heuristic == "temporary-layout" && s.Candidate == "gjnjv");
    }

    [Theory]
    [InlineData(Vk.Backspace, false)]
    [InlineData(Vk.Backspace, true)]
    [InlineData(0x2E, false)]
    public void An_edited_or_deleted_visit_does_not_propose_a_dictionary_word(int edit, bool ctrl)
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Wait(500).ShiftTap().Type("http ");
            if (ctrl) k.Down(Vk.LCtrl);
            k.Tap(edit);
            if (ctrl) k.Up(Vk.LCtrl);
            k.ShiftTap();
        });
        Assert.DoesNotContain(signals, s => s.Heuristic == "temporary-layout" && s.Candidate == "http");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Editing_a_later_word_is_not_evidence_that_the_correction_was_wrong(bool ctrlBackspace)
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Type("http").ShiftTap().ShiftTap().Type(" next");
            if (ctrlBackspace) k.Down(Vk.LCtrl);
            k.Tap(Vk.Backspace);
            if (ctrlBackspace) k.Up(Vk.LCtrl);
        });
        Assert.DoesNotContain(signals, s => s.Heuristic == "correction-followup");
    }

    [Fact]
    public void Recorded_selection_and_Delete_after_HTTP_does_not_claim_a_false_positive()
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Type("http").ShiftTap(30, 0);
            k.Wait(82).ShiftTap(30, 0);
            var correctedAt = k.Now;
            k.Wait(278).Down(Vk.Space);
            k.Wait(67).Up(Vk.Space);
            k.Wait(correctedAt + 1786 - k.Now).Down(Vk.LShift);
            k.Wait(142).Down(0x24);
            k.Wait(75).Up(0x24);
            k.Wait(75).Up(Vk.LShift);
            k.Wait(105).Down(0x2E);
            k.Wait(67).Up(0x2E);
        });
        Assert.Contains(signals, s => s.Heuristic == "shift-shift" && s.Candidate == "http");
        Assert.DoesNotContain(signals, s => s.Heuristic == "correction-followup" && s.Candidate == "http");
    }

    [Fact]
    public void Undo_immediately_after_a_correction_is_still_evidence_of_a_possible_false_positive()
    {
        var signals = AnalyzeKeys(k =>
        {
            k.ShiftTap().Type("http").ShiftTap().ShiftTap();
            k.Down(Vk.LCtrl);
            k.Tap('Z');
            k.Up(Vk.LCtrl);
        });
        Assert.Contains(signals, s => s.Heuristic == "correction-followup" && s.Candidate == "http");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void The_dictionary_starts_empty_and_replacing_it_does_not_keep_hidden_words(string? config)
    {
        var k = Recording(config);
        k.ShiftTap().Type("llm ast ai http html markdown md grillme ");
        Assert.Empty(k.Platform.Sent);
        k.Engine.SetApprovedTypingWords(["ast"]);
        k.Type("ast ");
        Assert.Equal("ast", k.Platform.SentText);
        k.Platform.Sent.Clear();
        k.Engine.SetApprovedTypingWords([]);
        k.Type("ast ");
        Assert.Empty(k.Platform.Sent);
    }
}
