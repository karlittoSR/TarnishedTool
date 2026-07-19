//

namespace TarnishedTool.Models;

// A full character state that can be attached to a saved line: equipment (with
// its ChrAsm control block and talisman pouch count), stats, and rune level.
// Flasks / physick / consumables will be added as those pieces come online.
public class CharacterSnapshot
{
    public EquipmentSnapshot Equipment { get; set; } = new();
    public Stats Stats { get; set; } = new();
    public int RuneLevel { get; set; }
}
