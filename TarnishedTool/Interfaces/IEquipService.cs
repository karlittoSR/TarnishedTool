//

using System.Collections.Generic;
using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface IEquipService
{
    // True if the equip/find-inventory-id game functions were resolved by AOB.
    bool IsAvailable { get; }

    // Human-readable resolution state (base-relative addresses or MISSING) for
    // each function, so a failure can be diagnosed per version.
    string ResolutionInfo { get; }

    // Equips an item already present in the player's inventory into the given
    // ChrAsm equipment slot. itemId is the category-prefixed id (same form used
    // by SpawnItem). Slot ids: 0=L-hand1, 1=R-hand1, 6=arrow1, 7=bolt1,
    // 12=head, 13=chest, 14=arms, 15=legs, 17-20=talismans.
    void Equip(uint itemId, int equipSlot);

    // Discovery helper: offsets (from PlayerGameData) whose dword equals value.
    IReadOnlyList<int> FindValueOffsets(uint value, int rangeBytes);

    // Snapshot of currently-equipped items + ChrAsm control block.
    EquipmentSnapshot CaptureEquipment();

    // Re-applies a captured snapshot (spawn + equip each slot, then restore grip
    // and active-armament selection).
    void ApplyEquipment(EquipmentSnapshot snapshot);
}
