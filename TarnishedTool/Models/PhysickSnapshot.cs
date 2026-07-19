//

namespace TarnishedTool.Models;

// The two crystal tears mixed into the Flask of Wondrous Physick. Stored as bare
// goods param ids (the category prefix is re-added on apply). NoTear = empty slot.
public class PhysickSnapshot
{
    public const uint NoTear = 0xFFFFFFFF;

    public uint Tear1 { get; set; } = NoTear;
    public uint Tear2 { get; set; } = NoTear;
}
