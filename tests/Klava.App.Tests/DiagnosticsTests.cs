using Klava.App;

namespace Klava.App.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Klava-diagnostics-tests-" + Guid.NewGuid());
    private string Primary => Path.Combine(_folder, "primary");
    private string Fallback => Path.Combine(_folder, "fallback");
    private ErrorJournal Journal => new(Primary, Fallback);

    [Fact]
    public void Writes_the_full_error_before_returning()
    {
        var journal = Journal;
        Assert.True(journal.Record("FATAL: " + new InvalidOperationException("test failure")));
        var contents = File.ReadAllText(journal.FilePath);
        Assert.Contains("InvalidOperationException: test failure", contents);
        Assert.Contains($"pid={Environment.ProcessId}", contents);
    }

    [Fact]
    public void Uses_a_separate_report_when_the_error_log_is_locked()
    {
        Directory.CreateDirectory(Primary);
        using var locked = new FileStream(Journal.FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        Assert.True(Journal.Record("locked log failure"));
        Assert.Contains("locked log failure", File.ReadAllText(Assert.Single(Directory.GetFiles(Primary, "error-*.log"))));
    }

    [Fact]
    public void Uses_the_fallback_when_the_main_folder_cannot_be_created()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Primary, "This file blocks the folder.");
        Assert.True(Journal.Record("fallback failure"));
        Assert.Contains("fallback failure", File.ReadAllText(Assert.Single(Directory.GetFiles(Fallback))));
    }

    [Fact]
    public void Does_not_throw_when_both_log_locations_are_unavailable()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Primary, "blocked");
        File.WriteAllText(Fallback, "blocked");
        Assert.False(Journal.Record("unwritable log"));
    }

    [Fact]
    public void Reports_a_session_without_a_clean_exit_on_the_next_start()
    {
        new RunSession(Primary, Journal).Start();
        var next = new RunSession(Primary, Journal);
        next.Start();
        Assert.Contains("Previous session did not exit cleanly", File.ReadAllText(Journal.FilePath));
        next.Complete();
    }

    [Fact]
    public void Does_not_report_a_clean_exit_as_a_crash()
    {
        var first = new RunSession(Primary, Journal);
        first.Start();
        first.Complete();
        var next = new RunSession(Primary, Journal);
        next.Start();
        Assert.Equal(string.Empty, File.ReadAllText(Journal.FilePath));
        next.Complete();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
