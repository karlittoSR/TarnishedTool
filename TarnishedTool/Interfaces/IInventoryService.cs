//

using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface IInventoryService
{
    // The two tears currently mixed into the Flask of Wondrous Physick.
    PhysickSnapshot CapturePhysick();

    // Re-mixes the physick to the captured pair. The tears do not need to be in
    // the inventory — the mix is stored directly on the character.
    void ApplyPhysick(PhysickSnapshot snapshot);

    // Consumable goods (GoodsType.NormalItem only) currently held, with counts.
    ConsumablesSnapshot CaptureConsumables();

    // Restores consumable counts to match the snapshot exactly: quantities are
    // topped up or trimmed, and consumables absent from the snapshot are removed.
    // Protected goods types (key items, great runes, spells, ashes) are untouched.
    // Returns a diagnostic report of what it actually read and changed.
    string ApplyConsumables(ConsumablesSnapshot snapshot);
}
