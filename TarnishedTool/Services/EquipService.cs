//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;
using TarnishedTool.Models;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Services;

// Proof-of-concept equipment writer. Mirrors the auto-equip path from
// yuiamoroll/elden_ring_item_randomiser: resolve the player's EquipInventoryData,
// ask the game's find_inventoryid function for the inventory slot holding a given
// item id, then call the game's equip function with an EquipItemStruct.
//
// The item must already be in the inventory (spawn it first). This only performs
// the equip half; capture/clear come later.
public class EquipService(IMemoryService memoryService, IItemService itemService) : IEquipService
{
    // EquipItemStruct field offsets (92-byte struct, rest is padding/zeroed).
    private const int SlotFieldOffset = 0x08;      // equipment_slot (uint32)
    private const int InventorySlotFieldOffset = 0x58; // inventory_slot (uint32), filled by shellcode
    private const int StructSize = 0x60;

    public bool IsAvailable => Functions.EquipItem != 0 && Functions.GetInventoryId != 0;

    public string ResolutionInfo
    {
        get
        {
            long baseAddr = (long)memoryService.BaseAddress;
            string Fmt(long a) => a == 0 ? "MISSING" : $"base+0x{a - baseAddr:X}";
            return $"EquipItem: {Fmt(Functions.EquipItem)}\nGetInventoryId: {Fmt(Functions.GetInventoryId)}";
        }
    }

    public void Equip(uint itemId, int equipSlot)
    {
        if (!IsAvailable) return;

        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return;

        var equipInventoryData = playerGameData + GameDataMan.EquipInventoryData;

        var structAddr = CodeCaveOffsets.Base + CodeCaveOffsets.EquipStruct;
        var itemIdAddr = CodeCaveOffsets.Base + CodeCaveOffsets.EquipItemId;
        var codeAddr = CodeCaveOffsets.Base + CodeCaveOffsets.EquipCode;

        // Zero the struct, then set the target equipment slot. inventory_slot is
        // filled in by the shellcode from find_inventoryid's return value.
        memoryService.WriteBytes(structAddr, new byte[StructSize]);
        memoryService.Write(structAddr + SlotFieldOffset, equipSlot);

        // The id find_inventoryid searches for.
        memoryService.Write(itemIdAddr, itemId);

        var code = BuildShellcode(
            (long)equipInventoryData,
            (long)itemIdAddr,
            (long)structAddr,
            Functions.GetInventoryId,
            Functions.EquipItem);

        memoryService.WriteBytes(codeAddr, code);
        memoryService.RunThread(codeAddr);
    }

    // True clear-and-replace: the result equals the snapshot exactly. Slots the
    // snapshot fills are equipped (spawning the item, re-adding the category prefix
    // stripped in ChrAsm); slots it leaves empty are unequipped. The equip function
    // toggles, so re-equipping the item already in a slot is how we both skip
    // no-op slots (by leaving them) and unequip (by toggling the current item off).
    // Finally restores the ChrAsm control block (grip + active armament).
    // Note: a weapon that must be spawned gets the ash of war authored on the
    // snapshot (EquippedItem.AshOfWarId); the mounted ash cannot be read back.
    public void ApplyEquipment(EquipmentSnapshot snapshot)
    {
        if (!IsAvailable || snapshot == null) return;

        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return;

        var current = ReadEquippedArray(playerGameData);

        // Two historical layouts need opposite one-dword migrations. Version 0
        // DLC captures used the old pre-DLC addresses and started one item late.
        // Version 1 pre-DLC captures used the new DLC addresses and started one
        // item early. Neither migration mutates the saved JSON.
        bool shiftedLegacy = snapshot.HasShiftedLegacyCapture();
        bool earlyPreDlcV1 = snapshot.HasEarlyPreDlcV1Capture();

        // Target ids by slot for quick lookup. Skip Unarmed/empty ids so a slot the
        // snapshot leaves empty (incl. legacy snapshots that stored Unarmed=110000)
        // is unequipped, never spawned/equipped.
        var target = new Dictionary<int, EquippedItem>();
        if (snapshot.Items != null)
        {
            foreach (var item in snapshot.Items)
            {
                int slot = shiftedLegacy
                    ? item.Slot + 1
                    : earlyPreDlcV1 ? item.Slot - 1 : item.Slot;
                if (IsRestorableSlot(slot) && !IsEmptySlotId(item.ItemId, slot))
                    target[slot] = item;
            }
        }

        // Raise the talisman pouch count to cover both current and snapshot while we
        // work, so equip/unequip on any talisman slot is valid; it's set to the
        // snapshot's exact value at the end (re-locking any extra slots).
        byte targetPouches = System.Math.Min(snapshot.TalismanPouchCount, (byte)3);
        var currentPouches = memoryService.Read<byte>(playerGameData + GameDataMan.TalismanPouchCount);
        var workingPouches = System.Math.Max(System.Math.Min(currentPouches, (byte)3), targetPouches);
        memoryService.Write(playerGameData + GameDataMan.TalismanPouchCount, workingPouches);

        // First clear every mismatching slot. Re-equipping while walking a stale
        // pre-change array can move an item out of a later slot and then toggle it
        // back when that later slot is processed, producing the alternating/swapped
        // armor and talisman state seen on repeated loads.
        for (int slot = 0; slot < current.Length; slot++)
        {
            if (!IsRestorableSlot(slot)) continue;
            // Talisman changes are settled serially below. Their equip state can
            // update a frame after EquipItem returns; mixing their clear/fill
            // requests into this fast pass lets an older clear land after the new
            // talisman and leave the slot empty.
            if (IsTalismanSlot(slot)) continue;
            // Those two real slots were outside the malformed capture: actual
            // slot 0 preceded its read window, while actual talisman slot 17 was
            // mistaken for Hair (captured index 16) and skipped. Preserve both.
            if (shiftedLegacy && slot == 0) continue;

            uint cur = current[slot];
            bool curFilled = !IsEmptySlotId(cur, slot);
            bool alreadyWanted = target.TryGetValue(slot, out var wanted) && cur == wanted.ItemId;
            if (curFilled && !alreadyWanted)
                Equip(cur + CategoryPrefix(slot), slot);
        }

        // Read the post-clear state before filling targets; never make decisions
        // using the array captured before the game moved/unequipped items.
        current = ReadEquippedArray(playerGameData);

        var pending = new List<(int Slot, uint FullId)>();
        var spawnedIds = new HashSet<uint>();
        foreach (var pair in target)
        {
            int slot = pair.Key;
            var wanted = pair.Value;
            uint want = wanted.ItemId;
            if (!IsEmptySlotId(current[slot], slot) && current[slot] == want) continue;

            uint fullId = want + CategoryPrefix(slot);
            pending.Add((slot, fullId));

            // Only spawn what the player does not already own. Spawning creates a
            // FRESH copy, so equipping the one already in the inventory keeps its
            // ash of war and stops gear being duplicated on every restore.
            if (!IsInInventory(playerGameData, fullId))
            {
                // A spawned weapon otherwise gets its CLASS default skill, not the
                // one it was carrying. A hand-authored gem id is used when present.
                itemService.SpawnItem((int)fullId, 1, wanted.AshOfWarId, false, 1);
                spawnedIds.Add(fullId);
            }
        }

        // ItemSpawn returns before the inventory entry is always visible to
        // find_inventoryid. Poll the real inventory (all spawned items together)
        // for at most one second; this normally completes on the next game frame.
        // Never call Equip for an id that still is not visible, because the game's
        // failed lookup otherwise supplies a garbage inventory index.
        WaitForInventoryItems(playerGameData, spawnedIds);

        foreach (var entry in pending)
        {
            if (IsTalismanSlot(entry.Slot)) continue;
            if (!IsInInventory(playerGameData, entry.FullId)) continue;
            Equip(entry.FullId, entry.Slot);
        }

        // The opposite malformed windows omitted opposite ends of the talisman
        // range: DLC legacy omitted slot 17; pre-DLC v1 omitted slot 20.
        int preservedTalismanSlot = shiftedLegacy ? 17 : earlyPreDlcV1 ? 20 : -1;
        RestoreTalismanSlots(playerGameData, target, preservedTalismanSlot);

        // Lock the pouch count to exactly what the snapshot had.
        memoryService.Write(playerGameData + GameDataMan.TalismanPouchCount, targetPouches);

        var selections = snapshot.WeaponSlotSelections;
        if (earlyPreDlcV1)
        {
            // Its first stored selection is the real pre-DLC ArmStyle dword.
            if (selections != null && selections.Length > 0
                && selections[0] >= 0 && selections[0] <= 2)
                memoryService.Write(
                    playerGameData + GameDataMan.ChrAsmArmStyle, (byte)selections[0]);
        }
        else if (!shiftedLegacy && snapshot.ArmStyle <= 2)
        {
            memoryService.Write(playerGameData + GameDataMan.ChrAsmArmStyle, snapshot.ArmStyle);
        }

        if (selections != null)
        {
            int sourceCount = System.Math.Min(selections.Length, GameDataMan.WepSlotSelCount);
            for (int source = 0; source < sourceCount; source++)
            {
                if (earlyPreDlcV1 && source == 0) continue;

                int destination = shiftedLegacy
                    ? source + 1
                    : earlyPreDlcV1 ? source - 1 : source;
                if (destination >= GameDataMan.WepSlotSelCount) break;

                int selection = selections[source];
                if (selection < 0 || selection > 2) continue;
                memoryService.Write(playerGameData + GameDataMan.ChrAsmWepSlotSel + destination * 4, selection);
            }
        }
    }

    // The ChrAsm array stores "Unarmed" (an empty weapon slot) as this weapon id.
    // It is NOT a real inventory item, so it must be treated as an empty slot and
    // never spawned/equipped: doing so makes find_inventoryid miss and the equip
    // function grab a garbage inventory index, equipping a random weapon (a stray
    // longbow/rapier). Empty slots otherwise read as 0 or 0xFFFFFFFF.
    private const uint UnarmedWeaponId = 110000;

    private static bool IsEmptySlotId(uint id, int slot) =>
        id == 0 || id == 0xFFFFFFFF || (slot >= 0 && slot <= 5 && id == UnarmedWeaponId);

    private static bool IsRestorableSlot(int slot) =>
        (slot >= 0 && slot <= 9) || (slot >= 12 && slot <= 15) || (slot >= 17 && slot <= 20);

    private static bool IsTalismanSlot(int slot) => slot >= 17 && slot <= 20;

    // ChrAsm stores bare param ids; the spawn/equip functions want the category-
    // prefixed id, keyed by slot type.
    private static uint CategoryPrefix(int slot) => slot switch
    {
        >= 12 and <= 15 => 0x10000000, // armor (Protector)
        >= 17 and <= 21 => 0x20000000, // talismans (Accessory)
        _ => 0u,                        // weapons + ammo (0-11)
    };

    // Reads the ChrAsm equipped-item array (bare ids per filled slot) plus the
    // control block (grip + active-armament selections).
    public EquipmentSnapshot CaptureEquipment()
    {
        var snapshot = new EquipmentSnapshot
        {
            LayoutVersion = EquipmentSnapshot.CurrentLayoutVersion,
            SourceGameLayout = GameDataMan.UsesDlcCharacterLayout
                ? CharacterDataLayout.Dlc
                : CharacterDataLayout.PreDlc,
        };
        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return snapshot;

        var current = ReadEquippedArray(playerGameData);
        for (int slot = 0; slot < current.Length; slot++)
        {
            if (!IsRestorableSlot(slot)) continue;
            uint id = current[slot];
            if (!IsEmptySlotId(id, slot))
                snapshot.Items.Add(new EquippedItem { Slot = slot, ItemId = id });
        }

        snapshot.ArmStyle = memoryService.Read<byte>(playerGameData + GameDataMan.ChrAsmArmStyle);
        var sel = memoryService.ReadBytes(
            playerGameData + GameDataMan.ChrAsmWepSlotSel, GameDataMan.WepSlotSelCount * 4);
        for (int i = 0; i < GameDataMan.WepSlotSelCount; i++)
            snapshot.WeaponSlotSelections[i] = BitConverter.ToInt32(sel, i * 4);

        snapshot.TalismanPouchCount = memoryService.Read<byte>(playerGameData + GameDataMan.TalismanPouchCount);

        return snapshot;
    }

    // not in PlayerGameData, so it must live on the item instance. Each inventory
    // entry carries a ga_item handle at +0x00; the matching GaItem record should sit
    // in one of the buffers hanging off the EquipInventoryData header (its +0x08
    // reads 0xC00 = 3072, which looks like a GaItem capacity). Search those buffers
    // for the weapon id and dump the surrounding bytes so the record layout — and
    // the gem field inside it — can be read off directly.
    // weapon param (Rogier's Rapier's param default is Repeating Thrust with
    // gemMountType=2, i.e. changeable). Before assuming it needs the GaItem struct,
    // check the cheap possibility: a gem id array sitting inside PlayerGameData
    // near the equipped-item list. Reads PGD once and reports every dword that
    // matches a known EquipParamGem row id.
    // row that grants it, so the correct gem to pass as SpawnItem's aowId can be
    // identified from real data instead of guessed. Reads nothing destructively.
    // Guard so a bad count can never send the walk into unmapped memory.
    private const int MaxInventoryEntries = 4096;

    // True if the player already holds this exact (category-prefixed) item id.
    // Walks the same EquipInventoryData entry array the consumables capture uses;
    // the runtime-specific stride and item-id field are documented in Offsets.cs.
    private bool IsInInventory(nint playerGameData, uint fullItemId)
    {
        var inv = playerGameData + GameDataMan.EquipInventoryData;
        var entries = memoryService.Read<nint>(inv + GameDataMan.InventoryEntriesPtr);
        int count = memoryService.Read<int>(inv + GameDataMan.InventoryCount);
        if (entries == 0 || count <= 0) return false;

        count = Math.Min(count, MaxInventoryEntries);

        byte[] buffer;
        try { buffer = memoryService.ReadBytes(entries, count * GameDataMan.InventoryEntrySize); }
        catch { return false; }

        for (int i = 0; i < count; i++)
        {
            int at = i * GameDataMan.InventoryEntrySize;
            if (BitConverter.ToUInt32(buffer, at + GameDataMan.InventoryEntryItemId) == fullItemId)
                return true;
        }
        return false;
    }

    private void WaitForInventoryItems(nint playerGameData, HashSet<uint> itemIds)
    {
        if (itemIds == null || itemIds.Count == 0) return;

        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 1000)
        {
            bool allVisible = true;
            foreach (uint itemId in itemIds)
            {
                if (IsInInventory(playerGameData, itemId)) continue;
                allVisible = false;
                break;
            }

            if (allVisible) return;
            Thread.Sleep(10);
        }
    }

    // Talisman equip/unequip requests settle asynchronously. Handle those four
    // slots separately: clear a mismatching slot and observe it become empty before
    // filling it, then observe the requested id before moving on. Correct slots are
    // only read, never toggled. A second pass catches inventory entries which become
    // visible just after an unequip without spawning another copy.
    private void RestoreTalismanSlots(
        nint playerGameData,
        Dictionary<int, EquippedItem> target,
        int preservedSlot)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int slot = 17; slot <= 20; slot++)
            {
                // The malformed old capture omitted actual talisman slot 17, so
                // preserving it is the only non-destructive legacy behaviour.
                if (slot == preservedSlot) continue;

                bool hasTarget = target.TryGetValue(slot, out var wanted);
                uint expected = hasTarget ? wanted.ItemId : 0;
                uint current = ReadEquippedSlot(playerGameData, slot);
                if ((hasTarget && current == expected) ||
                    (!hasTarget && IsEmptySlotId(current, slot)))
                    continue;

                if (!IsEmptySlotId(current, slot))
                {
                    Equip(current + CategoryPrefix(slot), slot);
                    WaitForTalismanSlot(playerGameData, slot, 0, expectEmpty: true);
                }
            }

            for (int slot = 17; slot <= 20; slot++)
            {
                if (slot == preservedSlot) continue;
                if (!target.TryGetValue(slot, out var wanted)) continue;

                uint current = ReadEquippedSlot(playerGameData, slot);
                if (current == wanted.ItemId) continue;
                if (!IsEmptySlotId(current, slot)) continue;

                uint fullItemId = wanted.ItemId + CategoryPrefix(slot);
                if (!IsInInventory(playerGameData, fullItemId)) continue;

                Equip(fullItemId, slot);
                WaitForTalismanSlot(
                    playerGameData, slot, wanted.ItemId, expectEmpty: false);
            }

            Thread.Sleep(25);
            if (TalismanSlotsMatch(playerGameData, target, preservedSlot)) return;
        }
    }

    private bool TalismanSlotsMatch(
        nint playerGameData,
        Dictionary<int, EquippedItem> target,
        int preservedSlot)
    {
        for (int slot = 17; slot <= 20; slot++)
        {
            if (slot == preservedSlot) continue;

            uint current = ReadEquippedSlot(playerGameData, slot);
            if (target.TryGetValue(slot, out var wanted))
            {
                if (current != wanted.ItemId) return false;
            }
            else if (!IsEmptySlotId(current, slot))
            {
                return false;
            }
        }
        return true;
    }

    private bool WaitForTalismanSlot(
        nint playerGameData, int slot, uint expectedItemId, bool expectEmpty)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            uint current = ReadEquippedSlot(playerGameData, slot);
            bool matches = expectEmpty
                ? IsEmptySlotId(current, slot)
                : current == expectedItemId;
            if (matches) return true;

            Thread.Sleep(10);
        }
        while (timer.ElapsedMilliseconds < 250);

        return false;
    }

    private uint ReadEquippedSlot(nint playerGameData, int slot) =>
        memoryService.Read<uint>(
            playerGameData + GameDataMan.ChrAsmEquippedList + slot * sizeof(uint));

    // Reads all EquipSlotCount ChrAsm equipped ids (0xFFFFFFFF/0 = empty).
    private uint[] ReadEquippedArray(nint playerGameData)
    {
        var arr = new uint[GameDataMan.EquipSlotCount];
        var bytes = memoryService.ReadBytes(
            playerGameData + GameDataMan.ChrAsmEquippedList, GameDataMan.EquipSlotCount * 4);
        for (int i = 0; i < arr.Length; i++)
            arr[i] = BitConverter.ToUInt32(bytes, i * 4);
        return arr;
    }

    // player_inventory_manager = *(*(GameDataMan) + PlayerGameData)  (== GetGameDataPtr)
    private nint ResolvePlayerGameData()
    {
        var gameDataInstance = memoryService.Read<nint>(GameDataMan.Base);
        if (gameDataInstance == 0) return 0;
        return memoryService.Read<nint>(gameDataInstance + GameDataMan.PlayerGameData);
    }

    // Discovery helper for locating the ChrAsm equipped-item array: scans the
    // first rangeBytes of PlayerGameData (4-byte aligned) and returns every
    // offset whose dword equals value. Equip a known item, scan for its id, then
    // re-equip it into a different slot and scan again — the offset that shifts
    // by the array stride is the equipped-slot array.
    public IReadOnlyList<int> FindValueOffsets(uint value, int rangeBytes)
    {
        var result = new List<int>();
        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return result;

        var bytes = memoryService.ReadBytes(playerGameData, rangeBytes);
        for (int off = 0; off + 4 <= bytes.Length; off += 4)
        {
            if (BitConverter.ToUInt32(bytes, off) == value)
                result.Add(off);
        }
        return result;
    }

    // Builds:
    //   sub rsp,0x28
    //   mov rcx, equipInventoryData
    //   mov rdx, &itemId
    //   mov rax, find_inventoryid ; call rax        ; eax = inventory slot
    //   mov rcx, &equipStruct
    //   mov [rcx+0x58], eax                          ; equipStruct.inventory_slot = eax
    //   mov rax, equip ; call rax                    ; equip(&equipStruct)
    //   add rsp,0x28 ; ret
    // Absolute (mov imm64 + call rax) so it works regardless of cave distance.
    private static byte[] BuildShellcode(long equipInventoryData, long itemIdAddr, long structAddr,
        long findInventoryId, long equip)
    {
        var code = new List<byte>();

        code.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });                 // sub rsp,0x28

        code.AddRange(new byte[] { 0x48, 0xB9 });                             // mov rcx, imm64
        code.AddRange(BitConverter.GetBytes(equipInventoryData));

        code.AddRange(new byte[] { 0x48, 0xBA });                             // mov rdx, imm64
        code.AddRange(BitConverter.GetBytes(itemIdAddr));

        code.AddRange(new byte[] { 0x48, 0xB8 });                             // mov rax, imm64
        code.AddRange(BitConverter.GetBytes(findInventoryId));
        code.AddRange(new byte[] { 0xFF, 0xD0 });                            // call rax

        code.AddRange(new byte[] { 0x48, 0xB9 });                             // mov rcx, imm64 (&struct)
        code.AddRange(BitConverter.GetBytes(structAddr));

        code.AddRange(new byte[] { 0x89, 0x81, 0x58, 0x00, 0x00, 0x00 });     // mov [rcx+0x58], eax

        code.AddRange(new byte[] { 0x48, 0xB8 });                             // mov rax, imm64
        code.AddRange(BitConverter.GetBytes(equip));
        code.AddRange(new byte[] { 0xFF, 0xD0 });                            // call rax

        code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });                 // add rsp,0x28
        code.Add(0xC3);                                                       // ret

        return code.ToArray();
    }
}
