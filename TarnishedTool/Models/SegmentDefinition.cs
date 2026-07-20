//

namespace TarnishedTool.Models;

public enum SegmentFinishType
{
    Position,
    EventFlag
}

// The complete, decoded definition of a Segment Timer route. A start is always
// positional; the finish is exactly one of a position trigger or an event flag.
public sealed class SegmentDefinition
{
    private SegmentDefinition(Position start, float startRadius, SegmentFinishType finishType,
        Position endPosition, float endRadius, uint endFlagId, bool endFlagValue)
    {
        Start = start;
        StartRadius = startRadius;
        FinishType = finishType;
        EndPosition = endPosition;
        EndRadius = endRadius;
        EndFlagId = endFlagId;
        EndFlagValue = endFlagValue;
    }

    public Position Start { get; }
    public float StartRadius { get; }
    public SegmentFinishType FinishType { get; }
    public Position EndPosition { get; }
    public float EndRadius { get; }
    public uint EndFlagId { get; }
    public bool EndFlagValue { get; }

    public static SegmentDefinition PositionFinish(Position start, float startRadius,
        Position endPosition, float endRadius) =>
        new(start, startRadius, SegmentFinishType.Position, endPosition, endRadius, 0, false);

    public static SegmentDefinition EventFlagFinish(Position start, float startRadius,
        uint endFlagId, bool endFlagValue) =>
        new(start, startRadius, SegmentFinishType.EventFlag, null, 0f, endFlagId, endFlagValue);
}
