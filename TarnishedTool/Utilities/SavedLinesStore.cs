//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TarnishedTool.Models;

namespace TarnishedTool.Utilities;

// Persists the saved-lines library as JSON (lines.json) next to settings.txt.
// A saved line is stored as its full model, so new fields (character stats,
// equipment, ...) can be added to SavedLine and will round-trip automatically:
// unknown fields are ignored on read and missing fields fall back to defaults.
//
// Legacy: earlier versions stored a tab-separated lines.txt
// ("<name>\t<TTLINE1 code>\t<bestMs>"). Load() migrates that file on first run
// (writes lines.json, keeps the old file as a backup) so existing libraries
// survive the upgrade.
public static class SavedLinesStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TarnishedTool");

    private static string JsonPath => Path.Combine(Dir, "lines.json");
    private static string LegacyTxtPath => Path.Combine(Dir, "lines.txt");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static List<SavedLine> Load()
    {
        // Prefer the JSON store once it exists.
        try
        {
            if (File.Exists(JsonPath))
            {
                var json = File.ReadAllText(JsonPath);
                var lines = JsonSerializer.Deserialize<List<SavedLine>>(json);
                return lines ?? new List<SavedLine>();
            }
        }
        catch { }

        // First run after upgrade: migrate the legacy tab-separated file.
        var migrated = LoadLegacyTxt();
        if (migrated.Count > 0)
        {
            Save(migrated);
            TryBackupLegacyTxt();
        }
        return migrated;
    }

    public static void Save(IEnumerable<SavedLine> lines)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new List<SavedLine>(lines), WriteOptions);
            File.WriteAllText(JsonPath, json);
        }
        catch { }
    }

    private static List<SavedLine> LoadLegacyTxt()
    {
        var result = new List<SavedLine>();
        try
        {
            if (!File.Exists(LegacyTxtPath)) return result;

            foreach (var line in File.ReadAllLines(LegacyTxtPath))
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

    private static void TryBackupLegacyTxt()
    {
        try
        {
            var backup = LegacyTxtPath + ".bak";
            if (File.Exists(LegacyTxtPath) && !File.Exists(backup))
                File.Move(LegacyTxtPath, backup);
        }
        catch { }
    }
}
