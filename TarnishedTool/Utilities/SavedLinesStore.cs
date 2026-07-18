//

using System;
using System.Collections.Generic;
using System.IO;
using TarnishedTool.Models;

namespace TarnishedTool.Utilities;

// Persists the saved-lines library to a plain text file next to settings.txt.
// One entry per line: "<name>\t<TTLINE1 code>\t<bestMs>" (bestMs optional for
// backward compatibility). Reuses the share codec so a saved line is exactly a
// named export code — portable and dependency-free.
public static class SavedLinesStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TarnishedTool",
        "lines.txt");

    public static List<SavedLine> Load()
    {
        var result = new List<SavedLine>();
        try
        {
            if (!File.Exists(FilePath)) return result;

            foreach (var line in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 2) continue;

                var name = parts[0];
                var code = parts[1];
                if (string.IsNullOrWhiteSpace(code)) continue;

                uint bestMs = 0;
                if (parts.Length >= 3) uint.TryParse(parts[2], out bestMs);

                result.Add(new SavedLine(name, code, bestMs));
            }
        }
        catch { }
        return result;
    }

    public static void Save(IEnumerable<SavedLine> lines)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var output = new List<string>();
            foreach (var l in lines)
                output.Add($"{Sanitize(l.Name)}\t{l.Code}\t{l.BestMs}");

            File.WriteAllLines(FilePath, output);
        }
        catch { }
    }

    private static string Sanitize(string s) =>
        (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
