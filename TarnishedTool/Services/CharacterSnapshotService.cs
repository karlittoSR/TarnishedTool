//

using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Services;

// Composes the per-domain services into a single character capture/apply. Kept
// separate from the line-comparison feature so it can be reused wherever a
// character state needs saving or restoring.
public class CharacterSnapshotService(
    IEquipService equipService, IPlayerService playerService, IFlaskService flaskService)
    : ICharacterSnapshotService
{
    public CharacterSnapshot Capture() => new()
    {
        Equipment = equipService.CaptureEquipment(),
        Stats = playerService.GetStats(),
        RuneLevel = playerService.GetRuneLevel(),
        Flasks = flaskService?.CaptureFlasks(),
    };

    public void Apply(CharacterSnapshot snapshot)
    {
        if (snapshot == null) return;

        if (snapshot.Stats != null)
            ApplyStats(snapshot.Stats);

        if (snapshot.Equipment != null)
            equipService.ApplyEquipment(snapshot.Equipment);

        // Flasks after stats: stat changes move max HP/FP, so set the charge
        // allocation once the character is otherwise final. A grace-rest (run last
        // by the reset flow) then tops the charges off.
        if (snapshot.Flasks != null)
            flaskService?.ApplyFlasks(snapshot.Flasks);
    }

    // SetStat adjusts rune level + rune memory per stat, keeping the character
    // consistent (same path the Player tab uses).
    private void ApplyStats(Stats stats)
    {
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Vigor, stats.Vigor);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Mind, stats.Mind);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Endurance, stats.Endurance);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Strength, stats.Strength);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Dexterity, stats.Dexterity);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Intelligence, stats.Intelligence);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Faith, stats.Faith);
        playerService.SetStat((int)GameDataMan.PlayerGameDataOffsets.Arcane, stats.Arcane);
    }
}
