//

using System;
using System.Collections.Generic;
using System.Linq;
using TarnishedTool.Enums;
using TarnishedTool.GameIds;
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
public class EquipService(
    IMemoryService memoryService, IItemService itemService,
    IParamService paramService, IParamRepository paramRepository) : IEquipService
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
    // Note: custom ashes of war aren't captured, so weapons re-apply with default ash.
    public void ApplyEquipment(EquipmentSnapshot snapshot)
    {
        if (!IsAvailable || snapshot == null) return;

        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return;

        var current = ReadEquippedArray(playerGameData);

        // Target ids by slot for quick lookup. Skip Unarmed/empty ids so a slot the
        // snapshot leaves empty (incl. legacy snapshots that stored Unarmed=110000)
        // is unequipped, never spawned/equipped.
        var target = new Dictionary<int, uint>();
        foreach (var item in snapshot.Items)
            if (!IsEmptySlotId(item.ItemId))
                target[item.Slot] = item.ItemId;

        // Raise the talisman pouch count to cover both current and snapshot while we
        // work, so equip/unequip on any talisman slot is valid; it's set to the
        // snapshot's exact value at the end (re-locking any extra slots).
        var currentPouches = memoryService.Read<byte>(playerGameData + GameDataMan.TalismanPouchCount);
        var workingPouches = System.Math.Max(currentPouches, snapshot.TalismanPouchCount);
        memoryService.Write(playerGameData + GameDataMan.TalismanPouchCount, workingPouches);

        for (int slot = 0; slot < current.Length; slot++)
        {
            if (slot == HairSlot) continue;

            uint cur = current[slot];
            bool curFilled = !IsEmptySlotId(cur);

            if (target.TryGetValue(slot, out uint want))
            {
                if (curFilled && cur == want) continue; // already correct — leave it

                uint fullId = want + CategoryPrefix(slot);

                // Only spawn what the player does not already own. Spawning creates a
                // FRESH copy, so equipping the one already in the inventory keeps its
                // ash of war and stops gear being duplicated on every restore.
                if (!IsInInventory(playerGameData, fullId))
                {
                    // Spawning with aowId -1 gives the weapon CLASS default skill, not
                    // the weapon's own: a spawned Rogier's Rapier came back with the
                    // generic Repeating Thrust instead of its Glintblade Phalanx.
                    // Resolve the weapon's native ash and spawn with it. -1 for
                    // armour/talismans, and as a fallback if the lookup fails.
                    int aowId = IsWeaponSlot(slot) ? ResolveNativeGemId(want) : -1;
                    itemService.SpawnItem((int)fullId, 1, aowId, false, 1);
                }

                Equip(fullId, slot);
            }
            else if (curFilled)
            {
                // Snapshot leaves this slot empty → unequip by toggling the current item.
                Equip(cur + CategoryPrefix(slot), slot);
            }
        }

        // Lock the pouch count to exactly what the snapshot had.
        memoryService.Write(playerGameData + GameDataMan.TalismanPouchCount, snapshot.TalismanPouchCount);

        memoryService.Write(playerGameData + GameDataMan.ChrAsmArmStyle, snapshot.ArmStyle);
        for (int i = 0; i < GameDataMan.WepSlotSelCount && i < snapshot.WeaponSlotSelections.Length; i++)
            memoryService.Write(playerGameData + GameDataMan.ChrAsmWepSlotSel + i * 4, snapshot.WeaponSlotSelections[i]);
    }

    // The ChrAsm array stores "Unarmed" (an empty weapon slot) as this weapon id.
    // It is NOT a real inventory item, so it must be treated as an empty slot and
    // never spawned/equipped: doing so makes find_inventoryid miss and the equip
    // function grab a garbage inventory index, equipping a random weapon (a stray
    // longbow/rapier). Empty slots otherwise read as 0 or 0xFFFFFFFF.
    private const uint UnarmedWeaponId = 110000;

    private static bool IsEmptySlotId(uint id) =>
        id == 0 || id == 0xFFFFFFFF || id == UnarmedWeaponId;

    // ChrAsm stores bare param ids; the spawn/equip functions want the category-
    // prefixed id, keyed by slot type.
    private const int HairSlot = 16;
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
        var snapshot = new EquipmentSnapshot();
        var playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return snapshot;

        var current = ReadEquippedArray(playerGameData);
        for (int slot = 0; slot < current.Length; slot++)
        {
            uint id = current[slot];
            if (!IsEmptySlotId(id))
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

    // Slots 0-11 are weapons and ammo; only those carry an ash of war.
    private static bool IsWeaponSlot(int slot) => slot >= 0 && slot < 12;

    // Weapon ids are base + affinity*100 + upgrade, with the base a multiple of
    // 10000 — so the param row for the weapon itself is the id with that stripped.
    private const int WeaponBaseIdModulus = 10000;

    // The weapon's own ash of war, as an EquipParamGem row id, or -1 if it cannot
    // be resolved (caller then falls back to the previous -1 behaviour).
    //
    // EquipParamWeapon.swordArtsParamId names the weapon's native skill, and the
    // gem that grants that skill is the EquipParamGem row with the same
    // swordArtsParamId — so the mapping is a reverse lookup over the gem table,
    // built once and cached.
    private Dictionary<int, int> _gemBySwordArt;

    private int ResolveNativeGemId(uint weaponItemId)
    {
        try
        {
            uint baseId = weaponItemId - (weaponItemId % WeaponBaseIdModulus);

            int swordArtsOffset = GetFieldOffset(Param.EquipParamWeapon, "swordArtsParamId");
            if (swordArtsOffset < 0) return -1;

            var (wepTable, wepSlot) = ParamIndices.All["EquipParamWeapon"];
            var weaponRow = paramService.GetParamRow(wepTable, wepSlot, baseId);
            if (weaponRow == IntPtr.Zero) return -1;

            int swordArtsId = memoryService.Read<int>(weaponRow + swordArtsOffset);
            if (swordArtsId <= 0) return -1;

            return GemBySwordArt().TryGetValue(swordArtsId, out int gemId) ? gemId : -1;
        }
        catch { return -1; }
    }

    private Dictionary<int, int> GemBySwordArt()
    {
        if (_gemBySwordArt != null) return _gemBySwordArt;

        var map = new Dictionary<int, int>();
        try
        {
            int offset = GetFieldOffset(Param.EquipParamGem, "swordArtsParamId");
            var (gemTable, gemSlot) = ParamIndices.All["EquipParamGem"];
            var gems = paramRepository.GetParam(Param.EquipParamGem);

            if (offset >= 0 && gems?.Entries != null)
            {
                foreach (var entry in gems.Entries)
                {
                    var row = paramService.GetParamRow(gemTable, gemSlot, entry.Id);
                    if (row == IntPtr.Zero) continue;

                    int swordArtsId = memoryService.Read<int>(row + offset);
                    // Several gem rows share a skill; keep the lowest id, which is
                    // the ordinary obtainable version rather than a variant.
                    if (swordArtsId > 0 &&
                        (!map.TryGetValue(swordArtsId, out int existing) || entry.Id < existing))
                        map[swordArtsId] = (int)entry.Id;
                }
            }
        }
        catch { /* leave the map empty — callers fall back to -1 */ }

        _gemBySwordArt = map;
        return map;
    }

    // Param field offsets are computed by walking the row layout (ParamRepository),
    // so they are resolved by name once and cached rather than hardcoded.
    private readonly Dictionary<(Param, string), int> _fieldOffsets = new();

    private int GetFieldOffset(Param param, string fieldName)
    {
        if (_fieldOffsets.TryGetValue((param, fieldName), out int cached)) return cached;

        int resolved = -1;
        try
        {
            var loaded = paramRepository.GetParam(param);
            var field = loaded?.Fields?.FirstOrDefault(f => f.InternalName == fieldName);
            if (field != null) resolved = field.Offset;
        }
        catch { resolved = -1; }

        _fieldOffsets[(param, fieldName)] = resolved;
        return resolved;
    }

    // Guard so a bad count can never send the walk into unmapped memory.
    private const int MaxInventoryEntries = 4096;

    // True if the player already holds this exact (category-prefixed) item id.
    // Walks the same EquipInventoryData entry array the consumables capture uses;
    // entries are 20 bytes with the item id at +0x04 (see Offsets.cs).
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
