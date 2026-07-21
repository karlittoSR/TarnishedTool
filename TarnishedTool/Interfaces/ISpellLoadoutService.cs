using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

// Captures and restores the game's memorized-spell list. Apply is guarded by the
// captured slot capacity, ownership, layout and a read-back verification.
public interface ISpellLoadoutService
{
    SpellLoadoutSnapshot Capture();
    void Apply(SpellLoadoutSnapshot snapshot);
}
