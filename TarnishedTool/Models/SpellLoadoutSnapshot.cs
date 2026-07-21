using System;

namespace TarnishedTool.Models;

// Memorized spells use full database item ids (0x40000000 category prefix),
// while 0xFFFFFFFF preserves an empty entry. Keeping all fourteen entries makes
// capture lossless and avoids inferring anything from a spell's slot cost.
public class SpellLoadoutSnapshot
{
    public int FormatVersion { get; set; } = 1;
    public uint[] Slots { get; set; } = Array.Empty<uint>();
    public int SelectedSlot { get; set; } = -1;

    // Nullable so the short-lived capture-only format remains readable. A
    // snapshot without this guard is preserved but will not be applied.
    public int? MemorySlotCapacity { get; set; }
}
