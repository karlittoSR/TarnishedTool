//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarnishedTool.Models;

namespace TarnishedTool.Utilities;

// Persists the saved-segment library as JSON (lines.json) next to settings.txt.
// The current object envelope carries folders separately from SavedLine data.
// Older top-level arrays are backed up and migrated automatically on first load.
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

    public static SavedSegmentLibrary LoadLibrary()
    {
        // Prefer the JSON store once it exists.
        try
        {
            if (File.Exists(JsonPath))
            {
                var json = File.ReadAllText(JsonPath);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var lines = JsonSerializer.Deserialize<List<SavedLine>>(json)
                                ?? new List<SavedLine>();
                    var migratedLibrary = CreateRootLibrary(lines);

                    // Preserve the exact old file before replacing it with the
                    // folder-aware envelope. A failed write leaves the original
                    // lines.json untouched as well.
                    TryBackupPreFolderJson();
                    Save(migratedLibrary);
                    return migratedLibrary;
                }

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Invalid saved-segment library JSON.");

                var library = JsonSerializer.Deserialize<SavedSegmentLibrary>(json);
                if (library == null
                    || !string.Equals(library.Format, SavedSegmentLibrary.FormatId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unknown saved-segment library format.");

                if (library.Version < 1 || library.Version > SavedSegmentLibrary.CurrentVersion)
                    throw new InvalidDataException("Unsupported saved-segment library version.");

                Normalize(library);
                return library;
            }
        }
        catch { }

        // First run after upgrade: migrate the legacy tab-separated file.
        var migrated = LoadLegacyTxt();
        if (migrated.Count > 0)
        {
            var library = CreateRootLibrary(migrated);
            Save(library);
            TryBackupLegacyTxt();
            return library;
        }
        return new SavedSegmentLibrary();
    }

    // Kept for callers which only have a flat set. Every segment is placed at
    // root; the window uses the full-library overload so folder metadata survives.
    public static void Save(IEnumerable<SavedLine> lines) => Save(CreateRootLibrary(lines));

    public static void Save(SavedSegmentLibrary library)
    {
        string temporaryPath = null;
        try
        {
            Directory.CreateDirectory(Dir);
            Normalize(library);
            var json = JsonSerializer.Serialize(library, WriteOptions);

            // Write fully before replacing lines.json, so a crash or serialization
            // failure cannot leave a truncated library behind.
            temporaryPath = JsonPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(JsonPath))
                File.Replace(temporaryPath, JsonPath, null);
            else
                File.Move(temporaryPath, JsonPath);
        }
        catch
        {
            try
            {
                if (temporaryPath != null && File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch { }
        }
    }

    private static SavedSegmentLibrary CreateRootLibrary(IEnumerable<SavedLine> lines)
    {
        var library = new SavedSegmentLibrary();
        int order = 0;
        foreach (var line in lines ?? Enumerable.Empty<SavedLine>())
        {
            if (line == null) continue;
            library.Segments.Add(new SavedSegmentLibraryEntry
            {
                FolderId = null,
                Order = order++,
                Segment = line,
            });
        }
        return library;
    }

    private static void Normalize(SavedSegmentLibrary library)
    {
        library.Format = SavedSegmentLibrary.FormatId;
        library.Version = SavedSegmentLibrary.CurrentVersion;
        library.Folders ??= new List<SavedSegmentFolder>();
        library.Segments ??= new List<SavedSegmentLibraryEntry>();

        // Invalid references are safely returned to root. Do not silently delete
        // any folder or segment data while repairing a hand-edited/imported file.
        var folderIds = new HashSet<string>(
            library.Folders.Where(folder => !string.IsNullOrWhiteSpace(folder?.Id))
                .Select(folder => folder.Id),
            StringComparer.OrdinalIgnoreCase);

        library.Segments.RemoveAll(entry => entry?.Segment == null);
        foreach (var entry in library.Segments)
            if (!string.IsNullOrWhiteSpace(entry.FolderId) && !folderIds.Contains(entry.FolderId))
                entry.FolderId = null;
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

    private static void TryBackupPreFolderJson()
    {
        try
        {
            var backup = Path.Combine(Dir, "lines.pre-folders-v1.json");
            if (File.Exists(JsonPath) && !File.Exists(backup))
                File.Copy(JsonPath, backup);
        }
        catch { }
    }

    // Folder deletion can remove a complete subtree. Keep a recoverable copy of
    // the last library immediately before each such destructive operation.
    public static void BackupBeforeFolderDelete()
    {
        try
        {
            if (!File.Exists(JsonPath)) return;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var backup = Path.Combine(Dir, $"lines.before-folder-delete-{stamp}.json");
            File.Copy(JsonPath, backup, overwrite: false);
        }
        catch { }
    }
}
