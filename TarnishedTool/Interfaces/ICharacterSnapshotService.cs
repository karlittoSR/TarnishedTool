//

using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface ICharacterSnapshotService
{
    // Captures the current character (equipment + stats + rune level).
    CharacterSnapshot Capture();

    // Applies a snapshot: sets stats then re-applies equipment.
    void Apply(CharacterSnapshot snapshot);
}
