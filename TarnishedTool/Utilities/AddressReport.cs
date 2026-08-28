//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TarnishedTool.Memory;

namespace TarnishedTool.Utilities;

// Every game patch moves the static addresses, so supporting a new build starts
// with knowing what the scan actually found. This writes each resolved address as
// a module-relative offset -- the exact form the version tables in Offsets.cs
// store -- and names the ones that came back empty, which are the patterns that
// need updating for the new build.
public static class AddressReport
{
    // The pre-1.08 and 1.07 shapes of the loading-screen hook. Exactly one of the
    // three LoadScreenMsgLookup variants matches any given build, so the other two
    // are always absent -- reporting them as a loss would be pure noise.
    private static readonly string[] ExpectedAbsent =
    {
        "Hooks.LoadScreenMsgLookupEarlyPatches",
        "Hooks.LoadScreenMsgLookupMidPatches",
    };

    public static IReadOnlyList<string> Write(string gameVersion, IntPtr moduleBase, bool scanned)
    {
        var missing = new List<string>();

        try
        {
            var found = new List<string>();
            var expectedAbsent = new List<string>();
            long moduleBaseValue = moduleBase.ToInt64();

            foreach (var type in typeof(Offsets)
                         .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
                         .OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    // Consts are struct field offsets, not addresses.
                    if (field.IsLiteral) continue;

                    long value;
                    if (field.FieldType == typeof(IntPtr))
                        value = ((IntPtr)field.GetValue(null)).ToInt64();
                    else if (field.FieldType == typeof(long))
                        value = (long)field.GetValue(null);
                    else
                        continue;

                    var name = $"{type.Name}.{field.Name}";

                    // Zero, or the module base itself, means the lookup produced
                    // nothing: an unmatched pattern or a missing table entry.
                    if (value == 0 || value == moduleBaseValue)
                    {
                        if (Array.IndexOf(ExpectedAbsent, name) < 0) missing.Add(name);
                        else expectedAbsent.Add(name);
                        continue;
                    }

                    found.Add($"{name} = 0x{value - moduleBaseValue:X}   (absolute 0x{value:X})");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(
                $"=== Address report -- game {gameVersion ?? "unknown"}, module base 0x{moduleBaseValue:X}, " +
                $"source: {(scanned ? "pattern scan" : "version table")} ===");
            sb.AppendLine(
                $"resolved: {found.Count}, missing: {missing.Count}, " +
                $"absent by design: {expectedAbsent.Count} ({string.Join(", ", expectedAbsent)})");

            if (missing.Count > 0)
            {
                sb.AppendLine("--- MISSING ---");
                foreach (var name in missing) sb.AppendLine(name);
            }

            sb.AppendLine("--- RESOLVED (module-relative) ---");
            foreach (var line in found) sb.AppendLine(line);

            DiagnosticsLogger.Log(sb.ToString());
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Log($"Address report failed: {ex.Message}");
        }

        return missing;
    }
}
