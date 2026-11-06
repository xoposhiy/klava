using System.Diagnostics;
using System.Text.Json;

namespace Klava.App.Tests;

public class TranslationDpiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_startup_and_resize_keep_button_captions_inside_the_window(bool unaware)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../Klava.UiProbe/bin", configuration, "net10.0-windows/Klava.UiProbe.exe"));
        var start = new ProcessStartInfo(probe)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        start.ArgumentList.Add("--verify");
        if (unaware) start.ArgumentList.Add("--unaware");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var report = await output;
        Assert.True(process.ExitCode == 0, report + await error);
        using var json = JsonDocument.Parse(report);
        Assert.True(json.RootElement.GetProperty("Passed").GetBoolean());
        if (unaware) Assert.Equal(96, json.RootElement.GetProperty("DeviceDpi").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("Scenarios").GetArrayLength());
    }
}
