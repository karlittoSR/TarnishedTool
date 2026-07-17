//

using System;

namespace TarnishedTool.Utilities;

public static class TimeFormatter
{
    // mm:ss.mmm
    public static string Mmssmmm(uint ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    // Signed delta, e.g. "+1.234" or "+1:02.345".
    public static string SignedDelta(long deltaMs)
    {
        var sign = deltaMs >= 0 ? "+" : "-";
        var ts = TimeSpan.FromMilliseconds(Math.Abs(deltaMs));
        return (int)ts.TotalMinutes >= 1
            ? $"{sign}{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}"
            : $"{sign}{ts.Seconds}.{ts.Milliseconds:D3}";
    }
}
