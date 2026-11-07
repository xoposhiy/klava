using System.Text.Json;
using Klava.App;
using Klava.Core;

namespace Klava.UiProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--analyze-typing", var folder]) return AnalyzeTyping(folder);
        Application.SetHighDpiMode(args.Contains("--unaware") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new TranslationForm(
            "Ctrl+C copies the result. Ctrl+Enter requests AI details. Esc closes the window.", _ => new PreviewClient());
        form.StartPosition = FormStartPosition.CenterScreen;
        var verify = args.Contains("--verify");
        if (verify)
        {
            form.Opacity = 0;
            form.ShowInTaskbar = false;
        }
        var failure = false;
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            object Check(string name)
            {
                form.PerformLayout();
                var metrics = Descendants(form).OfType<TranslationButton>().Select(button => new
                {
                    button.Text, button.DeviceDpi, button.Width, button.Height,
                    PreferredWidth = button.GetPreferredSize(Size.Empty).Width,
                    Fits = button.Width >= button.GetPreferredSize(Size.Empty).Width && button.Parent!.ClientRectangle.Contains(button.Bounds),
                }).ToArray();
                failure |= metrics.Any(button => !button.Fits);
                var footerActions = Descendants(form).OfType<TranslationButton>()
                    .Where(button => button.Text is "AI details" or "Settings" or "Copy").ToArray();
                failure |= footerActions.Select(button => button.PointToScreen(Point.Empty).Y).Distinct().Count() != 1;
                var folder = Environment.GetEnvironmentVariable("KLAVA_HISTORY_PREVIEW_DIR");
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(folder, $"translation-native-{form.DeviceDpi}-{name}.png"));
                }
                return new { name, form.ClientSize, form.Opacity, form.ShowInTaskbar, Buttons = metrics };
            }
            // Windows may limit the initial size to the monitor work area.
            failure = Math.Abs(form.Padding.Left - 18 * form.DeviceDpi / 96) > 1
                || form.FormBorderStyle != FormBorderStyle.Sizable;
            failure |= Descendants(form).OfType<TranslationButton>().Where(button => button.Text == "AI details")
                .Any(button => Math.Abs(button.Padding.Left - 12 * button.DeviceDpi / 96) > 1);
            var first = Check("initial");
            var scenarios = new List<object> { first };
            if (verify)
            {
                form.ClientSize = new Size(440 * form.DeviceDpi / 96, 380 * form.DeviceDpi / 96);
                scenarios.Add(Check("small"));
                form.ClientSize = new Size(640 * form.DeviceDpi / 96, 500 * form.DeviceDpi / 96);
                form.Hide(); form.Show();
                scenarios.Add(Check("resized"));
            }
            Console.WriteLine(JsonSerializer.Serialize(new { form.DeviceDpi, Scenarios = scenarios, Passed = !failure }));
            if (verify) form.Close();
        });
        Application.Run(form);
        return failure ? 1 : 0;
    }

    private static int AnalyzeTyping(string folder)
    {
        if (!Directory.Exists(folder) || System.Diagnostics.Process.GetProcessesByName("Klava").Length > 0)
        {
            Console.Error.WriteLine("Exit Klava and pass an existing typing folder before offline analysis.");
            return 1;
        }
        using var store = new TypingEpisodeStore(folder);
        if (!SpinWait.SpinUntil(() => !store.Snapshot.Status.StartsWith("Loading", StringComparison.Ordinal), TimeSpan.FromSeconds(5))
            || !store.Snapshot.Status.StartsWith("Ready", StringComparison.Ordinal)) return 1;
        store.AnalyzeNow();
        if (!SpinWait.SpinUntil(() => store.Snapshot.Status.StartsWith("Analysis complete", StringComparison.Ordinal), TimeSpan.FromSeconds(30))) return 1;
        Console.WriteLine(JsonSerializer.Serialize(store.Snapshot));
        return 0;
    }

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private sealed class PreviewClient : ITranslationClient
    {
        public Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken cancellation) =>
            Task.FromResult(new TranslationResult("EN", "RU", "", [new TranslationEntry(
                "Ctrl+C копирует результат. Ctrl+Enter запрашивает ИИ-подробности. Esc закрывает окно.", "", "", "", "")], ""));
    }
}
