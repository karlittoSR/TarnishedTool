//

using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface ICharacterSnapshotService
{
    // Captures the current character (equipment + stats + rune level).
    CharacterSnapshot Capture();

    // Applies a snapshot: stats, equipment, flasks, consumables, physick. Each
    // step is isolated, so one failure cannot silently skip the rest. Returns an
    // empty string on success, otherwise a per-step failure report.
    string Apply(CharacterSnapshot snapshot);
}
