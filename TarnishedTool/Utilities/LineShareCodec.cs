//

using System;
using System.Globalization;
using System.Numerics;
using TarnishedTool.Models;

namespace TarnishedTool.Utilities;

// Serializes a Line Comparison start/end pair to a compact, strict text token
// so runners can share a line. Full float precision; start carries orientation
// (angle + physics angles) and both sides carry their trigger radius, so an
// imported line reproduces the exact restore point and trigger zones.
//
// Format (single line):
//   TTLINE1;S,<blockIdHex>,<x>,<y>,<z>,<angle>,<pa1>,<pa2>,<radius>;E,<blockIdHex>,<x>,<y>,<z>,<radius>
public static class LineShareCodec
{
    private const string Magic = "TTLINE1";
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static string Encode(Position start, float startRadius, Position end, float endRadius) =>
        string.Join(";", Magic,
            EncodeStart(start, startRadius),
            EncodeEnd(end, endRadius));

    private static string EncodeStart(Position p, float radius) =>
        string.Join(",", "S", p.BlockId.ToString("X", Ci),
            F(p.Coords.X), F(p.Coords.Y), F(p.Coords.Z),
            F(p.Angle), F(p.PhysicsAngle1), F(p.PhysicsAngle2), F(radius));

    private static string EncodeEnd(Position p, float radius) =>
        string.Join(",", "E", p.BlockId.ToString("X", Ci),
            F(p.Coords.X), F(p.Coords.Y), F(p.Coords.Z), F(radius));

    private static string F(float f) => f.ToString("G9", Ci);

    public static bool TryDecode(string text, out Position start, out float startRadius,
        out Position end, out float endRadius)
    {
        start = null;
        end = null;
        startRadius = 0f;
        endRadius = 0f;

        if (string.IsNullOrWhiteSpace(text)) return false;

        // Pull the token out even if it's embedded in a larger blob (e.g. the
        // Copy output, which prefixes it with "Code: " among other lines).
        var idx = text.IndexOf(Magic, StringComparison.Ordinal);
        if (idx < 0) return false;
        var token = text.Substring(idx);
        var newline = token.IndexOfAny(['\r', '\n']);
        if (newline >= 0) token = token.Substring(0, newline);

        var parts = token.Trim().Split(';');
        if (parts.Length != 3 || parts[0] != Magic) return false;

        return TryDecodePart(parts[1], "S", true, out start, out startRadius)
            && TryDecodePart(parts[2], "E", false, out end, out endRadius);
    }

    private static bool TryDecodePart(string segment, string tag, bool withAngles,
        out Position pos, out float radius)
    {
        pos = null;
        radius = 0f;

        var f = segment.Split(',');
        var expected = withAngles ? 9 : 6;
        if (f.Length != expected || f[0] != tag) return false;

        if (!uint.TryParse(f[1], NumberStyles.HexNumber, Ci, out var blockId)) return false;
        if (!TryF(f[2], out var x) || !TryF(f[3], out var y) || !TryF(f[4], out var z)) return false;

        if (withAngles)
        {
            if (!TryF(f[5], out var angle) || !TryF(f[6], out var pa1) || !TryF(f[7], out var pa2))
                return false;
            if (!TryF(f[8], out radius)) return false;
            pos = new Position(blockId, new Vector3(x, y, z), angle, pa1, pa2);
        }
        else
        {
            if (!TryF(f[5], out radius)) return false;
            pos = new Position(blockId, new Vector3(x, y, z), 0f);
        }

        return true;
    }

    private static bool TryF(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, Ci, out value);
}
