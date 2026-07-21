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
    public CharacterSnapshot Capture()
    {
        bool dlcLayout = GameDataMan.UsesDlcCharacterLayout;
        return new CharacterSnapshot
        {
            Equipment = equipService.CaptureEquipment(),
            Stats = playerService.GetStats(),
            RuneLevel = playerService.GetRuneLevel(),
            // These bytes do not represent blessings before the DLC layout.
            ScadutreeBlessingLevel = dlcLayout ? playerService.GetScadu() : null,
            ReveredSpiritAshBlessingLevel = dlcLayout ? playerService.GetSpiritAsh() : null,
            Flasks = flaskService?.CaptureFlasks(),
            Physick = inventoryService?.CapturePhysick(),
            Consumables = inventoryService?.CaptureConsumables(),
        };
    }

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

        if (GameDataMan.UsesDlcCharacterLayout && snapshot.ScadutreeBlessingLevel.HasValue)
            Step("Scadutree Blessing", () => playerService.SetScadu(
                Clamp(snapshot.ScadutreeBlessingLevel.Value, 0, 20)));

        if (GameDataMan.UsesDlcCharacterLayout && snapshot.ReveredSpiritAshBlessingLevel.HasValue)
            Step("Revered Spirit Ash Blessing", () => playerService.SetSpiritAsh(
                Clamp(snapshot.ReveredSpiritAshBlessingLevel.Value, 0, 10)));

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

        // The same one-dword-late legacy capture that shifted equipment also read
        // Physick from the wrong region. Its tear values cannot be reconstructed,
        // so preserve the current mix until that segment is updated/re-captured.
        bool invalidPreDlcV1Physick = snapshot.Equipment?.HasEarlyPreDlcV1Capture() == true;
        bool invalidLegacyDlcPhysick = snapshot.Equipment?.HasShiftedLegacyCapture() == true;
        if (snapshot.Physick != null && !invalidPreDlcV1Physick && !invalidLegacyDlcPhysick)
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

    private static int Clamp(int value, int minimum, int maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
