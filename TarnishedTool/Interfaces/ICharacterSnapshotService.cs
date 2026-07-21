//

using TarnishedTool.Models;

namespace TarnishedTool.Interfaces;

public interface ICharacterSnapshotService
{
    // Captures the current character, including DLC blessing levels and the
    // memorized spell list.
    CharacterSnapshot Capture();

    // Applies every captured domain, including spells. Each step is isolated, so
    // one failure cannot silently skip the rest. Returns an empty string on
    // success, otherwise a per-step failure report.
    string Apply(CharacterSnapshot snapshot);
}
