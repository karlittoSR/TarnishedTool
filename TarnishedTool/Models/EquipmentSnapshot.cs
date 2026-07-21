//

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TarnishedTool.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterDataLayout
{
    Unknown,
    PreDlc,
    Dlc,
}

// One equipped item: the equip-function slot id and the bare param id (no
// category prefix, as stored in ChrAsm).
public class EquippedItem
{
    public int Slot { get; set; }
    public uint ItemId { get; set; }

    // Ash of war to give this weapon when it has to be spawned, as an
    // EquipParamGem row id. -1 leaves the weapon with its class default.
    //
    // This is authored by hand in lines.json rather than captured: the mounted
    // ash lives on the item instance (in the GaItem record reached through the
    // inventory entry's handle), which is not reachable from PlayerGameData, the
    // ChrAsm array or the inventory buffers. The weapon param cannot supply it
    // either — Rogier's Rapier's own swordArtsParamId is 109 (Repeating Thrust)
    // with gemMountType 2, i.e. its Glintblade Phalanx comes from a mounted gem.
    public int AshOfWarId { get; set; } = -1;
}

// A capture of the player's equipment: the filled slots plus the ChrAsm control
// block (grip + which armament is active per hand), needed to faithfully restore
// two-handing and the active-weapon selection.
public class EquipmentSnapshot
{
    // Version 2 marks captures made with version-aware pre-DLC/DLC offsets.
    // During the version 1 transition, the same marker could be written by a
    // correct DLC capture or by a pre-DLC capture using DLC offsets; the explicit
    // source layout now distinguishes them. Version 0 snapshots remain supported.
    public const int CurrentLayoutVersion = 2;
    public int LayoutVersion { get; set; }

    // The game layout that produced this capture. Equipment is stored as semantic
    // slot ids and is portable once normalized, but retaining the source layout
    // makes historical one-dword capture migrations unambiguous across game
    // versions. Serialized as "PreDlc" / "Dlc" for readable exports.
    public CharacterDataLayout SourceGameLayout { get; set; }

    public List<EquippedItem> Items { get; set; } = new();

    // ChrAsm ArmStyle byte (one-hand / two-hand grip).
    public byte ArmStyle { get; set; }

    // The 6 CurrentWepSlotOffset selections: Left, Right, LeftArrow, RightArrow,
    // LeftBolt, RightBolt — which of the 3 slots per hand is currently active.
    public int[] WeaponSlotSelections { get; set; } = new int[6];

    // Talisman Pouches owned (0-3) at capture time. The player always has one
    // base slot, for a total of one to four usable talisman slots. Restored before equipping
    // talismans so all captured talisman slots are valid on the target character.
    public byte TalismanPouchCount { get; set; }

    // The broken capture began one dword late. Reserved slot 11 or an invalid
    // sixth active-slot value identifies that layout without changing older,
    // correctly-authored snapshots that also predate LayoutVersion.
    public bool HasShiftedLegacyCapture()
    {
        if (SourceGameLayout == CharacterDataLayout.PreDlc) return false;
        if (LayoutVersion != 0) return false;
        if (SourceGameLayout == CharacterDataLayout.Dlc) return true;
        if (Items != null && Items.Exists(item => item.Slot == 11)) return true;

        return WeaponSlotSelections != null
            && WeaponSlotSelections.Length >= 6
            && (WeaponSlotSelections[5] < 0 || WeaponSlotSelections[5] > 2);
    }

    // Layout version 1 was produced while DLC addresses were temporarily used on
    // pre-DLC builds. The explicit source marker is authoritative. ArmStyle is a
    // fallback signature for old imports that predate the marker.
    public bool HasEarlyPreDlcV1Capture() =>
        LayoutVersion == 1
        && (SourceGameLayout == CharacterDataLayout.PreDlc
            || (SourceGameLayout == CharacterDataLayout.Unknown && ArmStyle > 2));

    // Carries hand-authored ash-of-war ids over from a previous snapshot, so
    // re-capturing (Update) does not wipe them. Matched on the BASE weapon id, so
    // an ash set for Rogier's Rapier keeps applying after the weapon is upgraded —
    // the item id changes with every upgrade level, but the base does not.
    public void PreserveAshFrom(EquipmentSnapshot previous)
    {
        if (previous?.Items == null || Items == null) return;

        var ashByWeapon = new Dictionary<uint, int>();
        foreach (var old in previous.Items)
            if (old.AshOfWarId >= 0) ashByWeapon[BaseWeaponId(old.ItemId)] = old.AshOfWarId;

        foreach (var item in Items)
            if (item.AshOfWarId < 0 && ashByWeapon.TryGetValue(BaseWeaponId(item.ItemId), out int ash))
                item.AshOfWarId = ash;
    }

    // Weapon ids are base + affinity*100 + upgrade, with the base a multiple of
    // 10000, so stripping the remainder identifies the weapon itself.
    private static uint BaseWeaponId(uint itemId) => itemId - (itemId % 10000);
}
