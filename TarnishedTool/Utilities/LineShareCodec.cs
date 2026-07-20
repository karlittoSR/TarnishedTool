//

using System;
using System.Globalization;
using System.Numerics;
using TarnishedTool.Models;

namespace TarnishedTool.Utilities;

// Serializes a Segment Timer definition to the compact token stored in a
// SavedLine's JSON Code property. Full float precision; start carries orientation
// (angle + physics angles). A finish is either a position/radius or an event flag.
//
// Current formats (single line):
//   TTSEGMENT2;S,<blockIdHex>,<x>,<y>,<z>,<angle>,<pa1>,<pa2>,<radius>;P,<blockIdHex>,<x>,<y>,<z>,<radius>
//   TTSEGMENT2;S,<blockIdHex>,<x>,<y>,<z>,<angle>,<pa1>,<pa2>,<radius>;F,<eventId>,<0|1>
//
// Legacy position definitions remain readable:
//   TTLINE1;S,<blockIdHex>,<x>,<y>,<z>,<angle>,<pa1>,<pa2>,<radius>;E,<blockIdHex>,<x>,<y>,<z>,<radius>
public static class LineShareCodec
{
    private const string LegacyMagic = "TTLINE1";
    private const string Magic = "TTSEGMENT2";
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static string Encode(Position start, float startRadius, Position end, float endRadius) =>
        string.Join(";", Magic,
            EncodeStart(start, startRadius),
            EncodeEndPosition(end, endRadius));

    public static string EncodeEventFlag(Position start, float startRadius, uint eventId, bool expectedValue) =>
        string.Join(";", Magic,
            EncodeStart(start, startRadius),
            string.Join(",", "F", eventId.ToString(Ci), expectedValue ? "1" : "0"));

    public static string Encode(SegmentDefinition definition) =>
        definition.FinishType == SegmentFinishType.EventFlag
            ? EncodeEventFlag(definition.Start, definition.StartRadius,
                definition.EndFlagId, definition.EndFlagValue)
            : Encode(definition.Start, definition.StartRadius,
                definition.EndPosition, definition.EndRadius);

    private static string EncodeStart(Position p, float radius) =>
        string.Join(",", "S", p.BlockId.ToString("X", Ci),
            F(p.Coords.X), F(p.Coords.Y), F(p.Coords.Z),
            F(p.Angle), F(p.PhysicsAngle1), F(p.PhysicsAngle2), F(radius));

    private static string EncodeEndPosition(Position p, float radius) =>
        string.Join(",", "P", p.BlockId.ToString("X", Ci),
            F(p.Coords.X), F(p.Coords.Y), F(p.Coords.Z), F(radius));

    private static string F(float f) => f.ToString("G9", Ci);

    public static bool TryDecode(string text, out SegmentDefinition definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(text)) return false;

        // Pull the token out even if it is embedded in a larger blob. Prefer the
        // current format, then fall back to the legacy position-only format.
        var idx = text.IndexOf(Magic, StringComparison.Ordinal);
        var magic = Magic;
        if (idx < 0)
        {
            idx = text.IndexOf(LegacyMagic, StringComparison.Ordinal);
            magic = LegacyMagic;
        }
        if (idx < 0) return false;

        var token = text.Substring(idx);
        var newline = token.IndexOfAny(['\r', '\n']);
        if (newline >= 0) token = token.Substring(0, newline);

        var parts = token.Trim().Split(';');
        if (parts.Length != 3 || parts[0] != magic) return false;

        if (!TryDecodePosition(parts[1], "S", true, out var start, out var startRadius))
            return false;

        if (magic == LegacyMagic)
        {
            if (!TryDecodePosition(parts[2], "E", false, out var legacyEnd, out var legacyEndRadius))
                return false;
            definition = SegmentDefinition.PositionFinish(start, startRadius, legacyEnd, legacyEndRadius);
            return true;
        }

        if (TryDecodePosition(parts[2], "P", false, out var end, out var endRadius))
        {
            definition = SegmentDefinition.PositionFinish(start, startRadius, end, endRadius);
            return true;
        }

        var finish = parts[2].Split(',');
        if (finish.Length != 3 || finish[0] != "F"
            || !uint.TryParse(finish[1], NumberStyles.None, Ci, out var eventId) || eventId == 0
            || (finish[2] != "0" && finish[2] != "1"))
            return false;

        definition = SegmentDefinition.EventFlagFinish(start, startRadius, eventId, finish[2] == "1");
        return true;
    }

    // Compatibility overload for callers that specifically require a positional
    // finish. Event-flag definitions deliberately return false here.
    public static bool TryDecode(string text, out Position start, out float startRadius,
        out Position end, out float endRadius)
    {
        start = null;
        end = null;
        startRadius = 0f;
        endRadius = 0f;

        if (!TryDecode(text, out var definition)
            || definition.FinishType != SegmentFinishType.Position)
            return false;

        start = definition.Start;
        startRadius = definition.StartRadius;
        end = definition.EndPosition;
        endRadius = definition.EndRadius;
        return true;
    }

    private static bool TryDecodePosition(string segment, string tag, bool withAngles,
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
        float.TryParse(s, NumberStyles.Float, Ci, out value)
        && !float.IsNaN(value) && !float.IsInfinity(value);
}
