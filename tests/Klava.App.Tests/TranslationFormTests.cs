using Klava.App;
using Klava.Core;

namespace Klava.App.Tests;

public class TranslationFormTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Fits_actions_and_a_long_setup_hint_at_larger_scales(float scale)
    {
        FormTestKit.Run(() =>
        {
            using var form = new TranslationForm("word", _ => throw new InvalidOperationException(),
                "Add translation.deeplKey to C:\\Users\\Example.User\\AppData\\Roaming\\Klava\\config.json, then Reload config. Translation requires a DeepL API key.");
            form.Scale(new SizeF(scale, scale));
            form.Font = new Font(form.Font.FontFamily, 10 * scale);
            form.ClientSize = new Size((int)(460 * scale), (int)(450 * scale));
            FormTestKit.Preview(form, $"translation-setup-{scale:0.0}");
            foreach (var button in FormTestKit.Descendants(form).OfType<Button>())
                Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds), $"A translation action is clipped: {button.AccessibleName ?? button.Text}, bounds={button.Bounds}, parent={button.Parent.ClientRectangle}.");
            var status = FormTestKit.Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "Translation status");
            if (status.Visible)
                Assert.True(status.Height >= status.GetPreferredSize(new Size(status.Width, 0)).Height, "The setup hint is clipped.");
            var output = FormTestKit.Descendants(form).OfType<RichTextBox>().Single();
            Assert.True(output.Height > 50 * scale, "The translation body is too small.");
        });
    }

    [Fact]
    public void Opening_the_window_calls_only_DeepL_and_AI_requires_a_separate_request()
    {
        FormTestKit.Run(() =>
        {
            var deepL = new PendingTranslationClient();
            var ai = new PendingTranslationClient();
            using var form = new TranslationForm("bank", details => details ? ai : deepL);
            FormTestKit.Preview(form, "translation-loading");
            var request = Assert.Single(deepL.Requests);
            Assert.Empty(ai.Requests);
            request.Completion.SetResult(TranslationTestKit.Result("банк"));
            TranslationTestKit.PumpUntil(() => FormTestKit.Descendants(form).OfType<RichTextBox>().Single().Text.Contains("банк"));
            FormTestKit.Preview(form, "translation-quick");
            var task = form.TranslateAsync(true);
            var detailRequest = Assert.Single(ai.Requests);
            Assert.Equal("EN", detailRequest.Source);
            Assert.Equal("RU", detailRequest.Target);
            detailRequest.Completion.SetResult(new("EN", "RU", "bank", [
                new("банк", "noun", "financial institution", "She went to the bank.", "Она пошла в банк."),
                new("берег", "noun", "edge of a river", "They sat on the bank.", "Они сидели на берегу.")], ""));
            TranslationTestKit.PumpUntil(() => task.IsCompleted);
            var output = FormTestKit.Descendants(form).OfType<RichTextBox>().Single().Text;
            Assert.Contains("берег", output);
            Assert.Contains("NOUN", output);
            FormTestKit.Preview(form, "translation-dictionary");
        });
    }

    [Fact]
    public void A_late_result_does_not_replace_a_newer_result_and_closing_cancels_the_request()
    {
        FormTestKit.Run(() =>
        {
            var client = new PendingTranslationClient();
            using var form = new TranslationForm("some text", _ => client);
            FormTestKit.Preview(form, "translation-before-retry");
            var first = client.Requests[0];
            var task = form.TranslateAsync(false);
            Assert.True(first.Cancellation.IsCancellationRequested);
            client.Requests[1].Completion.SetResult(TranslationTestKit.Result("new result"));
            TranslationTestKit.PumpUntil(() => task.IsCompleted);
            first.Completion.SetResult(TranslationTestKit.Result("old result"));
            Application.DoEvents();
            Assert.Contains("new result", FormTestKit.Descendants(form).OfType<RichTextBox>().Single().Text);
            var pending = form.TranslateAsync(true);
            var last = client.Requests[^1];
            form.Close();
            Assert.True(last.Cancellation.IsCancellationRequested);
            last.Completion.SetResult(TranslationTestKit.Result("closed result"));
            TranslationTestKit.PumpUntil(() => pending.IsCompleted);
            Assert.False(pending.IsFaulted);
        });
    }

    [Fact]
    public void Language_controls_request_the_selected_pair_and_long_results_scroll()
    {
        FormTestKit.Run(() =>
        {
            var client = new PendingTranslationClient();
            using var form = new TranslationForm("A longer selected phrase", _ => client);
            FormTestKit.Preview(form, "translation-phrase-loading");
            var source = FormTestKit.Descendants(form).OfType<TranslationLanguageButton>().Single(button => button.AccessibleName == "Source language");
            var target = FormTestKit.Descendants(form).OfType<TranslationLanguageButton>().Single(button => button.AccessibleName == "Target language");
            source.Value = "DE";
            target.Value = "EN";
            Assert.True(client.Requests[0].Cancellation.IsCancellationRequested);
            Assert.Equal("DE", client.Requests[^1].Source);
            Assert.Equal("EN", client.Requests[^1].Target);
            var paragraph = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"Paragraph {i}: this is a long translation."));
            client.Requests[^1].Completion.SetResult(TranslationTestKit.Result(paragraph, "DE", "EN"));
            var output = FormTestKit.Descendants(form).OfType<RichTextBox>().Single();
            TranslationTestKit.PumpUntil(() => output.Text.Contains("Paragraph 60"));
            output.Select(output.TextLength, 0);
            output.ScrollToCaret();
            Application.DoEvents();
            Assert.True(output.GetCharIndexFromPosition(Point.Empty) > 0, "The translation did not scroll.");
            Assert.Equal(paragraph, output.Text);
            FormTestKit.Preview(form, "translation-long-scrolled");
            foreach (var request in client.Requests) request.Completion.TrySetCanceled();
        });
    }

    [Fact]
    public void AI_setup_errors_keep_the_quick_translation_available()
    {
        FormTestKit.Run(() =>
        {
            var client = new PendingTranslationClient();
            using var form = new TranslationForm("bank", details => details
                ? throw new TranslationException("Add translation.openaiKey to the config, then Reload config.") : client);
            FormTestKit.Preview(form, "translation-ai-setup-loading");
            client.Requests[0].Completion.SetResult(TranslationTestKit.Result("банк"));
            var output = FormTestKit.Descendants(form).OfType<RichTextBox>().Single();
            TranslationTestKit.PumpUntil(() => output.Text == "банк");
            var task = form.TranslateAsync(true);
            TranslationTestKit.PumpUntil(() => task.IsCompleted);
            Assert.Equal("банк", output.Text);
            var copy = FormTestKit.Descendants(form).OfType<Button>().Single(button => button.AccessibleName == "Copy result");
            Assert.True(copy.Enabled);
            var status = FormTestKit.Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "Translation status");
            Assert.True(status.Visible);
            Assert.Contains("OpenAI", status.Text);
            FormTestKit.Preview(form, "translation-ai-setup");
        });
    }
}
