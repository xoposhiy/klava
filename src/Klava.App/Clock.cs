using System.Diagnostics;

namespace Klava.App;

/// <summary>The high-resolution time base of <see cref="Klava.Core.KeyEvent.TimeMs"/>.</summary>
internal static class Clock
{
    public static long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>Wall-clock time of an event time, for the log.</summary>
    public static DateTime ToLocal(long timeMs) => DateTime.Now.AddMilliseconds(timeMs - NowMs());
}
