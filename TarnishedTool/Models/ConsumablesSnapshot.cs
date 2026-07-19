//

using System.Collections.Generic;

namespace TarnishedTool.Models;

// One consumable stack: the bare goods param id and how many were held.
public class ConsumableItem
{
    public uint GoodId { get; set; }
    public int Quantity { get; set; }
}

// The player's consumable goods at capture time. Deliberately scoped to
// GoodsType.NormalItem so restoring never touches key/quest items, crafting
// materials, spells, remembrances or spirit ashes.
public class ConsumablesSnapshot
{
    public List<ConsumableItem> Items { get; set; } = new();

    // Which consumable sits in each quick-item bar slot (bare goods ids, NoItem
    // for an empty slot). Null on snapshots captured before the bar was tracked,
    // in which case the bar is left alone.
    public const uint NoItem = 0xFFFFFFFF;
    public uint[] QuickSlots { get; set; }
}
