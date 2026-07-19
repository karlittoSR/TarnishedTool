//

using System.Collections.Generic;

namespace TarnishedTool.Models;

// One equipped item: the equip-function slot id and the bare param id (no
// category prefix, as stored in ChrAsm).
public class EquippedItem
{
    public int Slot { get; set; }
    public uint ItemId { get; set; }
}

// A capture of the player's equipment: the filled slots plus the ChrAsm control
// block (grip + which armament is active per hand), needed to faithfully restore
// two-handing and the active-weapon selection.
public class EquipmentSnapshot
{
    public List<EquippedItem> Items { get; set; } = new();

    // ChrAsm ArmStyle byte (one-hand / two-hand grip).
    public byte ArmStyle { get; set; }

    // The 6 CurrentWepSlotOffset selections: Left, Right, LeftArrow, RightArrow,
    // LeftBolt, RightBolt — which of the 3 slots per hand is currently active.
    public int[] WeaponSlotSelections { get; set; } = new int[6];

    // Unlocked talisman slots (0-4) at capture time. Restored before equipping
    // talismans so all captured talisman slots are valid on the target character.
    public byte TalismanPouchCount { get; set; }
}
