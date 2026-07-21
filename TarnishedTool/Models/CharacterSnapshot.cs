//

namespace TarnishedTool.Models;

// A full character state that can be attached to a saved line: equipment (with
// its ChrAsm control block and talisman pouch count), stats, rune level, DLC
// blessing levels, flasks, physick and consumables.
public class CharacterSnapshot
{
    public EquipmentSnapshot Equipment { get; set; } = new();
    public Stats Stats { get; set; } = new();
    public int RuneLevel { get; set; }

    // Nullable for snapshots created before DLC blessings were tracked. Applying
    // one of those legacy snapshots must leave the player's current levels alone.
    public int? ScadutreeBlessingLevel { get; set; }
    public int? ReveredSpiritAshBlessingLevel { get; set; }

    // Null on legacy snapshots captured before flasks were tracked.
    public FlaskSnapshot Flasks { get; set; }

    // Null on legacy snapshots captured before physick/consumables were tracked.
    public PhysickSnapshot Physick { get; set; }
    public ConsumablesSnapshot Consumables { get; set; }
}
