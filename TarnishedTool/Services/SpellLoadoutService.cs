using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Services;

// Capture, validation and restore for the live memorized-spell structure.
public class SpellLoadoutService : ISpellLoadoutService
{
    private const int CandidateBytes = GameDataMan.EquipMagicSelectedSlot + sizeof(int);
    private const uint EmptySlot = 0xFFFFFFFF;

    private readonly IMemoryService _memoryService;
    private readonly IDlcService _dlcService;
    private readonly Dictionary<uint, Item> _spellsByRawId;
    private readonly object _applyGate = new();

    public SpellLoadoutService(IMemoryService memoryService, IDlcService dlcService)
    {
        _memoryService = memoryService;
        _dlcService = dlcService;
        _spellsByRawId = DataLoader.GetItems("Sorceries", "Sorceries")
            .Concat(DataLoader.GetItems("Incantations", "Incantations"))
            .GroupBy(item => unchecked((uint)item.Id) & GameDataMan.ItemIdMask)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public SpellLoadoutSnapshot Capture()
    {
        if (!_memoryService.IsAttached) return null;

        nint playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0) return null;

        nint equipMagicData = _memoryService.Read<nint>(
            playerGameData + GameDataMan.EquipMagicData);
        if (!IsPlausibleUserPointer((long)equipMagicData)) return null;

        var candidate = ReadCandidate(GameDataMan.EquipMagicData, equipMagicData);
        if (candidate == null || candidate.InvalidSlotCount != 0 ||
            candidate.SelectedSlot < -1 || candidate.SelectedSlot >= GameDataMan.EquipMagicSlotCount ||
            !candidate.Slots.All(HasExpectedCompanion))
            return null;

        return new SpellLoadoutSnapshot
        {
            FormatVersion = 1,
            Slots = candidate.Slots
                .Select(slot => slot.Spell == null
                    ? EmptySlot
                    : slot.RawId | GameDataMan.GoodsCategoryPrefix)
                .ToArray(),
            SelectedSlot = candidate.SelectedSlot,
            MemorySlotCapacity = ReadMemorySlotCapacity(playerGameData),
        };
    }

    public void Apply(SpellLoadoutSnapshot snapshot)
    {
        if (snapshot == null) return;
        if (snapshot.FormatVersion != 1)
            throw new InvalidOperationException($"Unsupported spell snapshot format {snapshot.FormatVersion}.");
        if (snapshot.Slots == null || snapshot.Slots.Length != GameDataMan.EquipMagicSlotCount)
            throw new InvalidOperationException(
                $"Spell snapshot must contain exactly {GameDataMan.EquipMagicSlotCount} entries.");
        if (!snapshot.MemorySlotCapacity.HasValue)
            throw new InvalidOperationException(
                "This spell snapshot predates the memory-slot safety guard. Update the segment once before loading its spells.");
        if (snapshot.MemorySlotCapacity < 1 || snapshot.MemorySlotCapacity > GameDataMan.EquipMagicSlotCount)
            throw new InvalidOperationException(
                $"Invalid captured memory-slot capacity: {snapshot.MemorySlotCapacity}.");

        var targetRawIds = ValidateTarget(snapshot, out int occupiedCount);

        nint playerGameData = ResolvePlayerGameData();
        if (playerGameData == 0)
            throw new InvalidOperationException("PlayerGameData is unavailable.");

        int currentCapacity = ReadMemorySlotCapacity(playerGameData);
        if (currentCapacity < snapshot.MemorySlotCapacity.Value)
            throw new InvalidOperationException(
                $"The segment was captured with {snapshot.MemorySlotCapacity.Value} memory slots, " +
                $"but this character currently has {currentCapacity}. No spells were changed.");
        if (occupiedCount > currentCapacity)
            throw new InvalidOperationException(
                $"The spell list contains {occupiedCount} entries but only {currentCapacity} memory slots are available.");

        nint magicInventoryData = ResolveMagicInventoryData(playerGameData);
        if (!IsPlausibleUserPointer((long)magicInventoryData))
            throw new InvalidOperationException(
                "The spell inventory is unavailable; no spells were changed.");

        var targetInventoryHandles = new int[occupiedCount];
        var missing = new List<string>();
        for (int i = 0; i < occupiedCount; i++)
        {
            int physicalIndex = FindMagicInventoryIndex(magicInventoryData, snapshot.Slots[i]);
            if (physicalIndex < 0)
            {
                missing.Add(DescribeFullId(snapshot.Slots[i]));
                continue;
            }

            int tailDataIndex = _memoryService.Read<int>(
                magicInventoryData + GameDataMan.InventoryTailDataIndex);
            targetInventoryHandles[i] = checked(physicalIndex + tailDataIndex - 1);
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "The following memorized spells are not owned; no spells were changed:\n  " +
                string.Join("\n  ", missing));

        if (Functions.ChangeMagic == 0)
            throw new InvalidOperationException(
                "The game spell-menu function was not found for this version; no spells were changed.");
        if (!GameDataMan.UsesDlcCharacterLayout && Functions.ChangeMagicItemIdField == 0)
            throw new InvalidOperationException(
                "The pre-DLC spell item-data layout could not be resolved; no spells were changed.");
        if (CodeCaveOffsets.Base == IntPtr.Zero)
            throw new InvalidOperationException(
                "TarnishedTool's game-code workspace is unavailable; no spells were changed.");

        nint equipMagicData = _memoryService.Read<nint>(
            playerGameData + GameDataMan.EquipMagicData);
        if (!IsPlausibleUserPointer((long)equipMagicData))
            throw new InvalidOperationException("EquipMagicData is unavailable.");

        var current = ReadCandidate(GameDataMan.EquipMagicData, equipMagicData);
        if (current == null)
            throw new InvalidOperationException(
                "The current spell structure is unreadable; no spells were changed.");

        uint[] originalRawIds = current.Slots.Select(slot => slot.RawId).ToArray();
        int originalSelected = current.SelectedSlot;
        bool companionsCanonical = current.Slots.All(HasExpectedCompanion);

        // Idempotent reload: once the requested list and selection are already
        // active AND canonical, no game function should be called again.
        if (originalSelected == snapshot.SelectedSlot &&
            originalRawIds.SequenceEqual(targetRawIds) && companionsCanonical)
            return;

        // 1.07 first replaces a removed spell with (-1,-1); calling remove on
        // that already-empty slot once more normalizes it to (-1,0). Accept only
        // this exact intermediate state so ApplyExact can repair it.
        bool repairablePreDlcEmptyCompanions = !GameDataMan.UsesDlcCharacterLayout &&
            current.InvalidSlotCount == 0 &&
            current.Slots.All(slot => HasExpectedCompanion(slot) ||
                                      (slot.RawId == EmptySlot && slot.Companion == EmptySlot));

        if (current.InvalidSlotCount != 0 ||
            (!companionsCanonical && !repairablePreDlcEmptyCompanions))
            throw new InvalidOperationException(
                "The current spell structure failed validation; no spells were changed. " +
                $"recognized={current.ValidSpellCount}, empty={current.EmptySlotCount}, " +
                $"invalid={current.InvalidSlotCount}, slots={FormatRawIds(originalRawIds)}.");

        lock (_applyGate)
        {
            try
            {
                ApplyExact(targetRawIds, targetInventoryHandles, snapshot.SelectedSlot);
            }
            catch (Exception applyError)
            {
                try
                {
                    nint rollbackInventory = ResolveMagicInventoryData(playerGameData);
                    int originalCount = Array.FindIndex(originalRawIds, rawId => rawId == EmptySlot);
                    if (originalCount < 0) originalCount = originalRawIds.Length;
                    var rollbackHandles = ResolveInventoryHandles(
                        rollbackInventory, originalRawIds, originalCount);
                    ApplyExact(originalRawIds, rollbackHandles, originalSelected);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        $"Spell restore failed ({applyError.Message}) and its rollback also failed " +
                        $"({rollbackError.Message}).", applyError);
                }

                throw new InvalidOperationException(
                    $"The game rejected the spell replacement; the previous spell list was restored. " +
                    applyError.Message, applyError);
            }
        }
    }

    private SpellCandidate ReadCandidate(int fieldOffset, nint pointer)
    {
        byte[] bytes;
        try
        {
            bytes = _memoryService.ReadBytes(pointer, CandidateBytes);
        }
        catch
        {
            return null;
        }

        if (bytes == null || bytes.Length < CandidateBytes) return null;

        var slots = new List<SpellSlotDiagnostic>(GameDataMan.EquipMagicSlotCount);
        int valid = 0;
        int empty = 0;
        int invalid = 0;

        for (int slot = 0; slot < GameDataMan.EquipMagicSlotCount; slot++)
        {
            int offset = GameDataMan.EquipMagicSlotStart + slot * GameDataMan.EquipMagicSlotStride;
            uint rawId = BitConverter.ToUInt32(bytes, offset);
            uint companion = BitConverter.ToUInt32(bytes, offset + sizeof(uint));

            _spellsByRawId.TryGetValue(rawId, out var spell);
            if (spell != null) valid++;
            else if (rawId == EmptySlot) empty++;
            else invalid++;

            slots.Add(new SpellSlotDiagnostic(slot, rawId, companion, spell));
        }

        // Random pointed-to structures frequently contain one integer which also
        // happens to be a MagicParam id. Keep those as fallback candidates, but
        // reject noisy blocks that cannot plausibly be the 14-slot array.
        if (fieldOffset != GameDataMan.EquipMagicData && (valid == 0 || invalid > 4))
            return null;

        return new SpellCandidate(
            fieldOffset,
            pointer,
            BitConverter.ToInt32(bytes, GameDataMan.EquipMagicSelectedSlot),
            slots,
            valid,
            empty,
            invalid);
    }

    private static bool HasExpectedCompanion(SpellSlotDiagnostic slot) =>
        slot.RawId == EmptySlot
            ? slot.Companion == 0
            : slot.Spell != null && slot.Companion == EmptySlot;

    private uint[] ValidateTarget(SpellLoadoutSnapshot snapshot, out int occupiedCount)
    {
        var rawIds = new uint[GameDataMan.EquipMagicSlotCount];
        var seen = new HashSet<uint>();
        bool reachedEmpty = false;
        occupiedCount = 0;

        for (int i = 0; i < snapshot.Slots.Length; i++)
        {
            uint fullId = snapshot.Slots[i];
            if (fullId == EmptySlot)
            {
                reachedEmpty = true;
                rawIds[i] = EmptySlot;
                continue;
            }

            if (reachedEmpty)
                throw new InvalidOperationException(
                    "Spell snapshot contains an occupied entry after an empty entry.");
            if ((fullId & GameDataMan.ItemCategoryMask) != GameDataMan.GoodsCategoryPrefix)
                throw new InvalidOperationException($"Invalid spell item id 0x{fullId:X8} in slot {i}.");

            uint rawId = fullId & GameDataMan.ItemIdMask;
            if (!_spellsByRawId.TryGetValue(rawId, out var spell) ||
                unchecked((uint)spell.Id) != fullId)
                throw new InvalidOperationException($"Unknown spell item id 0x{fullId:X8} in slot {i}.");
            if (spell.IsDlc &&
                (!GameDataMan.UsesDlcCharacterLayout || _dlcService?.IsDlcAvailable != true))
                throw new InvalidOperationException(
                    $"{spell.Name} is a DLC spell unavailable in the current game. No spells were changed.");
            if (!seen.Add(rawId))
                throw new InvalidOperationException($"Duplicate spell {spell.Name} in the snapshot.");

            rawIds[i] = rawId;
            occupiedCount++;
        }

        if (occupiedCount == 0)
        {
            if (snapshot.SelectedSlot != -1)
                throw new InvalidOperationException(
                    "An empty spell list must use selected slot -1.");
        }
        else if (snapshot.SelectedSlot < 0 || snapshot.SelectedSlot >= occupiedCount)
        {
            throw new InvalidOperationException(
                $"Selected spell slot {snapshot.SelectedSlot} is outside the occupied spell list.");
        }

        return rawIds;
    }

    private void ApplyExact(IReadOnlyList<uint> rawIds, IReadOnlyList<int> inventoryHandles,
        int selectedSlot)
    {
        int occupiedCount = 0;
        while (occupiedCount < rawIds.Count && rawIds[occupiedCount] != EmptySlot)
            occupiedCount++;
        if (inventoryHandles.Count < occupiedCount)
            throw new InvalidOperationException("Missing spell inventory handles.");

        // Use the same game path as the Memorize Spell menu. Clearing first is
        // necessary because ChangeMagic rejects a spell already present in a
        // different slot. Calls are synchronous and serialized by _applyGate.
        nint playerGameDataBefore = ResolvePlayerGameData();
        if (playerGameDataBefore == 0)
            throw new InvalidOperationException("PlayerGameData disappeared during spell restore.");
        int callableSlotCount = Math.Min(
            ReadMemorySlotCapacity(playerGameDataBefore),
            GameDataMan.EquipMagicSlotCount);
        if (callableSlotCount <= 0)
            throw new InvalidOperationException("No callable memory slots are available.");

        // ChangeMagic itself does not clamp its slot argument. The menu helper
        // does, so never call it beyond the character's unlocked capacity.
        // Remove from the end. Older builds may compact later spells toward the
        // front when one is forgotten; ascending removal can then skip every
        // spell that moved into a slot already visited.
        for (int slot = callableSlotCount - 1; slot >= 0; slot--)
            CallChangeMagic(slot, EmptySlot, -1);

        if (!GameDataMan.UsesDlcCharacterLayout)
        {
            // The first pass turns occupied slots into (-1,-1). The second sees
            // raw -1 and takes the true remove path, producing canonical (-1,0).
            for (int slot = callableSlotCount - 1; slot >= 0; slot--)
                CallChangeMagic(slot, EmptySlot, -1);
        }

        for (int slot = 0; slot < occupiedCount; slot++)
            CallChangeMagic(slot, rawIds[slot], inventoryHandles[slot]);

        nint playerGameData = ResolvePlayerGameData();
        nint equipMagicData = playerGameData == 0
            ? 0
            : _memoryService.Read<nint>(playerGameData + GameDataMan.EquipMagicData);
        if (!IsPlausibleUserPointer((long)equipMagicData))
            throw new InvalidOperationException("EquipMagicData disappeared during spell restore.");

        // Selection is a simple index in the authoritative structure; the menu
        // function has already rebuilt the spell list and all dependent caches.
        _memoryService.Write(
            equipMagicData + GameDataMan.EquipMagicSelectedSlot, selectedSlot);

        Thread.Sleep(50);
        var after = ReadCandidate(GameDataMan.EquipMagicData, equipMagicData);
        uint[] actualRawIds = after?.Slots.Select(slot => slot.RawId).ToArray();
        if (after == null || after.SelectedSlot != selectedSlot ||
            !actualRawIds.SequenceEqual(rawIds) || !after.Slots.All(HasExpectedCompanion))
            throw new InvalidOperationException(
                "Read-back did not match the requested spell list. " +
                $"Expected selected={selectedSlot}, slots={FormatRawIds(rawIds)}; " +
                $"actual selected={(after == null ? "unreadable" : after.SelectedSlot.ToString())}, " +
                $"slots={(actualRawIds == null ? "unreadable" : FormatRawIds(actualRawIds))}.");
    }

    private void CallChangeMagic(int slot, uint rawId, int inventoryHandle)
    {
        const int structSize = 0x80;
        const int slotField = 0x08;
        const int inventoryHandleField = 0x48;
        int fullItemIdField = GameDataMan.UsesDlcCharacterLayout
            ? 0x4C
            : Functions.ChangeMagicItemIdField;
        const int inventoryItemIdField = 0x50;

        nint structAddress = CodeCaveOffsets.Base + CodeCaveOffsets.SpellStruct;
        nint codeAddress = CodeCaveOffsets.Base + CodeCaveOffsets.SpellCode;
        var data = new byte[structSize];

        WriteUInt32(data, slotField, unchecked((uint)slot));
        if (rawId == EmptySlot)
        {
            WriteUInt32(data, inventoryHandleField, EmptySlot);
            // The DLC function expects category-shaped invalid IDs. The older
            // one expects plain FFFFFFFF in all three fields.
            WriteUInt32(data, fullItemIdField,
                GameDataMan.UsesDlcCharacterLayout ? 0x4FFFFFFFu : EmptySlot);
            WriteUInt32(data, inventoryItemIdField,
                GameDataMan.UsesDlcCharacterLayout ? 0xBFFFFFFFu : EmptySlot);
        }
        else
        {
            WriteUInt32(data, inventoryHandleField, unchecked((uint)inventoryHandle));
            WriteUInt32(data, fullItemIdField, rawId | GameDataMan.GoodsCategoryPrefix);
            WriteUInt32(data, inventoryItemIdField, rawId | 0xB0000000u);
        }

        _memoryService.WriteBytes(structAddress, data);
        _memoryService.WriteBytes(codeAddress, BuildChangeMagicShellcode(
            slot, (long)structAddress, Functions.ChangeMagic,
            GameDataMan.UsesDlcCharacterLayout));
        _memoryService.RunThread(codeAddress);
    }

    private static byte[] BuildChangeMagicShellcode(int slot, long structAddress,
        long changeMagic, bool dlcCallingConvention)
    {
        var code = new List<byte>();
        code.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 }); // sub rsp,28

        if (dlcCallingConvention)
        {
            code.Add(0xB9);                                  // mov ecx,slot
            code.AddRange(BitConverter.GetBytes(slot));
            code.AddRange(new byte[] { 0x48, 0xBA });         // mov rdx,&struct
        }
        else
        {
            code.AddRange(new byte[] { 0x48, 0xB9 });         // mov rcx,&struct
        }
        code.AddRange(BitConverter.GetBytes(structAddress));

        code.AddRange(new byte[] { 0x48, 0xB8 });             // mov rax,function
        code.AddRange(BitConverter.GetBytes(changeMagic));
        code.AddRange(new byte[] { 0xFF, 0xD0 });             // call rax
        code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 }); // add rsp,28
        code.Add(0xC3);                                       // ret
        return code.ToArray();
    }

    private int[] ResolveInventoryHandles(nint magicInventoryData,
        IReadOnlyList<uint> rawIds, int occupiedCount)
    {
        if (!IsPlausibleUserPointer((long)magicInventoryData))
            throw new InvalidOperationException("The spell inventory is unavailable.");

        int tailDataIndex = _memoryService.Read<int>(
            magicInventoryData + GameDataMan.InventoryTailDataIndex);
        var handles = new int[occupiedCount];
        for (int i = 0; i < occupiedCount; i++)
        {
            uint fullId = rawIds[i] | GameDataMan.GoodsCategoryPrefix;
            int physicalIndex = FindMagicInventoryIndex(magicInventoryData, fullId);
            if (physicalIndex < 0)
                throw new InvalidOperationException(
                    $"{DescribeFullId(fullId)} is unavailable for rollback.");
            handles[i] = checked(physicalIndex + tailDataIndex - 1);
        }
        return handles;
    }

    private int FindMagicInventoryIndex(nint magicInventoryData, uint fullItemId)
    {
        nint entries = _memoryService.Read<nint>(
            magicInventoryData + GameDataMan.InventoryEntriesPtr);
        int usedCount = _memoryService.Read<int>(
            magicInventoryData + GameDataMan.InventoryCount);
        if (!IsPlausibleUserPointer((long)entries) || usedCount <= 0)
            return -1;

        byte[] buffer = _memoryService.ReadBytes(
            entries, GameDataMan.InventoryMaxEntries * GameDataMan.InventoryEntrySize);
        int encountered = 0;
        for (int i = 0; i < GameDataMan.InventoryMaxEntries; i++)
        {
            int offset = i * GameDataMan.InventoryEntrySize + GameDataMan.InventoryEntryItemId;
            uint itemId = BitConverter.ToUInt32(buffer, offset);
            if (itemId == fullItemId) return i;
            if (itemId != EmptySlot && ++encountered >= usedCount) break;
        }
        return -1;
    }

    private nint ResolveMagicInventoryData(nint playerGameData)
    {
        if (!GameDataMan.UsesDlcCharacterLayout)
            return playerGameData + GameDataMan.EquipInventoryData;

        return _memoryService.Read<nint>(
            playerGameData + GameDataMan.DlcMagicInventoryDataPointer);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, buffer, offset, sizeof(uint));
    }

    private static string FormatRawIds(IEnumerable<uint> rawIds) =>
        "[" + string.Join(",", rawIds.Select(id => id == EmptySlot ? "-" : $"{id:X}")) + "]";

    private string DescribeFullId(uint fullId)
    {
        uint rawId = fullId & GameDataMan.ItemIdMask;
        return _spellsByRawId.TryGetValue(rawId, out var spell)
            ? $"{spell.Name} (0x{fullId:X8})"
            : $"0x{fullId:X8}";
    }

    private int ReadMemorySlotCapacity(nint playerGameData) =>
        _memoryService.Read<int>(playerGameData + GameDataMan.MagicSlotCapacity);

    private nint ResolvePlayerGameData()
    {
        nint gameDataInstance = _memoryService.Read<nint>(GameDataMan.Base);
        return gameDataInstance == 0
            ? 0
            : _memoryService.Read<nint>(gameDataInstance + GameDataMan.PlayerGameData);
    }

    private static bool IsPlausibleUserPointer(long value) =>
        value >= 0x10000 && value <= 0x00007FFFFFFFFFFF;

    private sealed class SpellCandidate
    {
        public SpellCandidate(int playerGameDataFieldOffset, nint address, int selectedSlot,
            IReadOnlyList<SpellSlotDiagnostic> slots, int validSpellCount,
            int emptySlotCount, int invalidSlotCount)
        {
            PlayerGameDataFieldOffset = playerGameDataFieldOffset;
            Address = address;
            SelectedSlot = selectedSlot;
            Slots = slots;
            ValidSpellCount = validSpellCount;
            EmptySlotCount = emptySlotCount;
            InvalidSlotCount = invalidSlotCount;
        }

        public int PlayerGameDataFieldOffset { get; }
        public nint Address { get; }
        public int SelectedSlot { get; }
        public IReadOnlyList<SpellSlotDiagnostic> Slots { get; }
        public int ValidSpellCount { get; }
        public int EmptySlotCount { get; }
        public int InvalidSlotCount { get; }
    }

    private sealed class SpellSlotDiagnostic
    {
        public SpellSlotDiagnostic(int index, uint rawId, uint companion, Item spell)
        {
            Index = index;
            RawId = rawId;
            Companion = companion;
            Spell = spell;
        }

        public int Index { get; }
        public uint RawId { get; }
        public uint Companion { get; }
        public Item Spell { get; }
    }
}
