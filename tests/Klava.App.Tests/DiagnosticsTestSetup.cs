using System.Runtime.CompilerServices;

namespace Klava.App.Tests;

internal static class DiagnosticsTestSetup
{
    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable("KLAVA_LOG_DIR",
        Path.Combine(Path.GetTempPath(), "Klava-test-logs-" + Guid.NewGuid()));
}
