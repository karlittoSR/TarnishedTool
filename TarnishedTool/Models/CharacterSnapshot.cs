//

namespace TarnishedTool.Models;

// A full character state that can be attached to a saved line: equipment (with
// its ChrAsm control block and talisman pouch count), stats, rune level, and
// flasks (level + HP/FP split). Physick / consumables will follow.
public class CharacterSnapshot
{
    public EquipmentSnapshot Equipment { get; set; } = new();
    public Stats Stats { get; set; } = new();
    public int RuneLevel { get; set; }

    // Null on legacy snapshots captured before flasks were tracked.
    public FlaskSnapshot Flasks { get; set; }
}
