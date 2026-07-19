//

using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Services;

// Composes the per-domain services into a single character capture/apply. Kept
// separate from the line-comparison feature so it can be reused wherever a
// character state needs saving or restoring.
public class CharacterSnapshotService(
    IEquipService equipService, IPlayerService playerService, IFlaskService flaskService,
    IInventoryService inventoryService)
    : ICharacterSnapshotService
{
    public CharacterSnapshot Capture() => new()
    {
        Equipment = equipService.CaptureEquipment(),
        Stats = playerService.GetStats(),
        RuneLevel = playerService.GetRuneLevel(),
        Flasks = flaskService?.CaptureFlasks(),
        Physick = inventoryService?.CapturePhysick(),
        Consumables = inventoryService?.CaptureConsumables(),
    };

    public string Apply(CharacterSnapshot snapshot)
    {
        if (snapshot == null) return string.Empty;

        var errors = new System.Text.StringBuilder();

        // Each step is isolated: a failure in one (e.g. flasks) must not silently
        // skip the ones after it, which is exactly how a mid-apply exception used
        // to swallow consumables and physick.
        void Step(string name, System.Action action)
        {
            try { action(); }
            catch (System.Exception ex) { errors.AppendLine($"{name}: {ex.Message}"); }
        }

        if (snapshot.Stats != null)
            Step("Stats", () => ApplyStats(snapshot.Stats));

        if (snapshot.Equipment != null)
            Step("Equipment", () => equipService.ApplyEquipment(snapshot.Equipment));

        // Flasks after stats: stat changes move max HP/FP, so set the charge
        // allocation once the character is otherwise final. A grace-rest (run last
        // by the reset flow) then tops the charges off.
        if (snapshot.Flasks != null)
            Step("Flasks", () => flaskService?.ApplyFlasks(snapshot.Flasks));

        if (snapshot.Consumables != null)
            Step("Consumables", () =>
            {
                var report = inventoryService?.ApplyConsumables(snapshot.Consumables);
                if (!string.IsNullOrWhiteSpace(report)) errors.AppendLine("[consumables]\n" + report);
            });

        if (snapshot.Physick != null)
            Step("Physick", () => inventoryService?.ApplyPhysick(snapshot.Physick));

        return errors.ToString();
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
