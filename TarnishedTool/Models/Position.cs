// 

using System.Numerics;

namespace TarnishedTool.Models;

public class Position(uint blockId, Vector3 coords, float angle, float physicsAngle1 = 0f, float physicsAngle2 = 0f)
{
    public uint BlockId { get; set; } = blockId;
    public Vector3 Coords { get; set; } = coords;
    public float Angle { get; set; } = angle;
    public float PhysicsAngle1 { get; set; } = physicsAngle1;
    public float PhysicsAngle2 { get; set; } = physicsAngle2;
}