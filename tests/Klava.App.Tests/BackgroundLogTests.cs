using Klava.App;

namespace Klava.App.Tests;

public sealed class BackgroundLogTests
{
    [Fact]
    public async Task Flush_waits_for_the_write_to_finish_after_the_entry_leaves_the_queue()
    {
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var log = new BackgroundLog((_, _) => { writing.Set(); release.Wait(); }, _ => { });
        log.Add("error", true);
        try
        {
            Assert.True(writing.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(log.Flush(TimeSpan.FromMilliseconds(50)));
            var flushed = Task.Run(() => log.Flush(TimeSpan.FromSeconds(5)));
            release.Set();
            Assert.True(await flushed);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void A_writer_failure_does_not_stop_later_log_entries()
    {
        var reports = new List<Exception>();
        var lines = new List<string>();
        using var log = new BackgroundLog((line, _) =>
        {
            if (line == "broken") throw new UnauthorizedAccessException("test failure");
            lines.Add(line);
        }, reports.Add);
        log.Add("broken", true);
        log.Add("next", false);
        Assert.True(log.Flush(TimeSpan.FromSeconds(5)));
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(reports));
        Assert.Equal("next", Assert.Single(lines));
    }
}
