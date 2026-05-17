using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TarnishedTool.Utilities;

public static class DiagnosticsLogger
{
    private static readonly object LockObject = new();
    private static readonly Dictionary<string, DateTime> LastLogTimes = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TarnishedTool",
        "diagnostics.log");

    public static void Log(string message)
    {
        try
        {
            lock (LockObject)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} UTC] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void LogThrottled(string key, string message, TimeSpan? minimumInterval = null)
    {
        try
        {
            lock (LockObject)
            {
                var interval = minimumInterval ?? TimeSpan.FromSeconds(5);
                var now = DateTime.UtcNow;

                if (LastLogTimes.TryGetValue(key, out var lastLogTime) && now - lastLogTime < interval)
                    return;

                LastLogTimes[key] = now;
            }

            Log(message);
        }
        catch
        {
        }
    }
}