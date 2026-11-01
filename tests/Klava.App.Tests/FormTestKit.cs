using System.Runtime.ExceptionServices;

namespace Klava.App.Tests;

internal static class FormTestKit
{
    public static void SaveHistory(string path, Klava.Core.ChatterHistory history) =>
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Version = 3, Days = history.Snapshot() }));

    public static void Run(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                test();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The form test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));

    public static void Preview(Form form, string name)
    {
        if (!form.Visible)
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
        }
        form.PerformLayout();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        var directory = Environment.GetEnvironmentVariable("KLAVA_HISTORY_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"));
    }
}
