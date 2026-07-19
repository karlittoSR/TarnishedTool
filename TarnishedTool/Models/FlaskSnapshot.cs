//

namespace TarnishedTool.Models;

// The flask portion of a character snapshot: upgrade level and the HP/FP charge
// split. Captured/applied through the game's own flask talk-commands (see
// FlaskService) so the physical flask goods stay consistent with the values.
public class FlaskSnapshot
{
    // Flask upgrade level (0-12). -1 = no flask found / not captured.
    public int FlaskLevel { get; set; } = -1;

    // Number of crimson (HP) charges allocated.
    public int HpAllocation { get; set; } = -1;

    // Number of cerulean (FP) charges allocated.
    public int FpAllocation { get; set; } = -1;
}
