//

using System;
using System.Collections.Generic;
using System.Linq;
using TarnishedTool.Enums;
using TarnishedTool.Enums.ParamEnums.EquipParamGoods;
using TarnishedTool.GameIds;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using static TarnishedTool.GameIds.EzState;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Services;

// Reads and restores the two character pieces that live in the goods system:
// the Wondrous Physick mix (inline in PlayerGameData) and consumable counts
// (walked from the EquipInventoryData entry array). See the verified live-layout
// offset comments in Offsets.cs.
public class InventoryService(
    IMemoryService memoryService,
    IParamService paramService,
    IParamRepository paramRepository,
    IEzStateService ezStateService,
    IItemService itemService) : IInventoryService
{
    // Item type used by the inventory talk-commands for goods.
    private const int GoodsItemType = 3;

    // Guard so a bad count can never send the walk into unmapped memory.
    private const int MaxInventoryEntries = 4096;

    #region Physick

    public PhysickSnapshot CapturePhysick()
    {
        var snapshot = new PhysickSnapshot();
        var pgd = ResolvePlayerGameData();
        if (pgd == 0) return snapshot;

        snapshot.Tear1 = ToBareGoodId(memoryService.Read<uint>(pgd + GameDataMan.PhysickTear1));
        snapshot.Tear2 = ToBareGoodId(memoryService.Read<uint>(pgd + GameDataMan.PhysickTear2));
        return snapshot;
    }

    public void ApplyPhysick(PhysickSnapshot snapshot)
    {
        if (snapshot == null) return;
        var pgd = ResolvePlayerGameData();
        if (pgd == 0) return;

        memoryService.Write(pgd + GameDataMan.PhysickTear1, ToStoredGoodId(snapshot.Tear1));
        memoryService.Write(pgd + GameDataMan.PhysickTear2, ToStoredGoodId(snapshot.Tear2));
    }

    // Stored ids carry the goods category prefix; empty slots read as 0xFFFFFFFF
    // (and 0 defensively).
    private static uint ToBareGoodId(uint stored) =>
        stored == 0xFFFFFFFF || stored == 0
            ? PhysickSnapshot.NoTear
            : stored & GameDataMan.ItemIdMask;

    private static uint ToStoredGoodId(uint bare) =>
        bare == PhysickSnapshot.NoTear ? 0xFFFFFFFF : bare | GameDataMan.GoodsCategoryPrefix;

    #endregion

    #region Consumables

    public ConsumablesSnapshot CaptureConsumables()
    {
        var snapshot = new ConsumablesSnapshot
        {
            LayoutVersion = ConsumablesSnapshot.CurrentLayoutVersion
        };
        foreach (var (goodId, quantity) in ReadHeldGoods(GameDataMan.InventoryEntrySize))
        {
            if (!IsRestorableConsumable(goodId)) continue;
            snapshot.Items.Add(new ConsumableItem { GoodId = goodId, Quantity = quantity });
        }

        var pgd = ResolvePlayerGameData();
        if (pgd != 0) snapshot.QuickSlots = CaptureQuickSlots(pgd);

        return snapshot;
    }

    public string ApplyConsumables(ConsumablesSnapshot snapshot)
    {
        if (snapshot == null) return "consumables: snapshot null";

        var log = new System.Text.StringBuilder();

        var target = new Dictionary<uint, int>();
        foreach (var item in snapshot.Items)
            target[item.GoodId] = item.Quantity;

        int entrySize = snapshot.LayoutVersion >= ConsumablesSnapshot.CurrentLayoutVersion
            ? GameDataMan.InventoryEntrySize
            : GameDataMan.LegacyInventoryEntrySize;
        var seen = ReadHeldGoods(entrySize).ToList();

        // Trim or remove what's held now, then top up the rest.
        var held = new HashSet<uint>();
        foreach (var (goodId, quantity) in seen)
        {
            if (!IsRestorableConsumable(goodId)) continue;
            held.Add(goodId);

            int want = target.TryGetValue(goodId, out int q) ? q : 0;
            if (want != quantity) ChangeQuantity(goodId, want - quantity);
        }

        foreach (var kvp in target)
        {
            if (held.Contains(kvp.Key) || kvp.Value <= 0) continue;

            // Same filter as the capture side. Without it this path could grant an
            // item the filter rejects — including anything recorded by an older
            // snapshot taken under looser rules, which is how permanent tools and
            // tutorial notes kept being re-granted.
            if (!IsRestorableConsumable(kvp.Key)) continue;

            ChangeQuantity(kvp.Key, kvp.Value); // not held at all — grant the stack
        }

        // Re-read and report only what failed to land, so a silent partial restore
        // cannot go unnoticed. Entries the filter rejects are skipped — legacy
        // snapshots hold non-consumables we deliberately no longer touch.
        var after = ReadHeldGoods(entrySize).ToDictionary(g => g.GoodId, g => g.Quantity);
        foreach (var kvp in target)
        {
            if (!IsRestorableConsumable(kvp.Key)) continue;
            after.TryGetValue(kvp.Key, out int now);
            if (now != kvp.Value) log.AppendLine($"  {kvp.Key}: wanted {kvp.Value}, got {now}");
        }

        // NOTE: the quick-item bar is deliberately NOT restored. Writing the slot
        // words works (the icons appear), but an item id alone does not bind the
        // slot to the actual inventory stack, so every restored slot renders "0"
        // even when the item is held. The binding appears to need the entry's
        // ga_item handle (entry+0x00, e.g. 0xB00006C2), which is not stored
        // adjacent to the slot — the physick tears bound the region, so
        // the bar cannot simply be a wider stride. QuickSlots is still captured so
        // the data is there if the binding is ever worked out.
        return log.ToString();
    }

    private void ChangeQuantity(uint goodId, int delta)
    {
        if (delta == 0) return;

        if (delta > 0)
        {
            // PlayerInventoryChange only adjusts a stack that already exists, so it
            // cannot restore an item the player ran out of entirely. Spawning is the
            // proven "give" path (same one equipment restore uses) and works whether
            // or not the stack is currently held.
            int fullId = (int)(goodId | GameDataMan.GoodsCategoryPrefix);
            itemService.SpawnItem(fullId, delta, -1, false, delta);
            return;
        }

        ezStateService.ExecuteTalkCommand(
            TalkCommands.PlayerInventoryChange(GoodsItemType, (int)goodId, delta));
    }

    // The quick-item bar holds goods-prefixed ids; empty slots read as 0xFFFFFFFF.
    private uint[] CaptureQuickSlots(nint pgd)
    {
        var slots = new uint[GameDataMan.QuickItemSlotCount];
        var bytes = memoryService.ReadBytes(
            pgd + GameDataMan.QuickItemSlots, GameDataMan.QuickItemSlotCount * 4);

        for (int i = 0; i < slots.Length; i++)
        {
            uint stored = BitConverter.ToUInt32(bytes, i * 4);
            slots[i] = stored == 0xFFFFFFFF || stored == 0
                ? ConsumablesSnapshot.NoItem
                : stored & GameDataMan.ItemIdMask;
        }
        return slots;
    }


    // Walks the EquipInventoryData entry array and yields every goods stack.
    private IEnumerable<(uint GoodId, int Quantity)> ReadHeldGoods(int entrySize)
    {
        var pgd = ResolvePlayerGameData();
        if (pgd == 0) yield break;

        var inv = pgd + GameDataMan.EquipInventoryData;
        var entries = memoryService.Read<nint>(inv + GameDataMan.InventoryEntriesPtr);
        int count = memoryService.Read<int>(inv + GameDataMan.InventoryCount);
        if (entries == 0 || count <= 0) yield break;

        count = Math.Min(count, MaxInventoryEntries);

        byte[] buffer;
        try { buffer = memoryService.ReadBytes(entries, count * entrySize); }
        catch { yield break; }

        for (int i = 0; i < count; i++)
        {
            int at = i * entrySize;
            uint itemId = BitConverter.ToUInt32(buffer, at + GameDataMan.InventoryEntryItemId);
            if ((itemId & GameDataMan.ItemCategoryMask) != GameDataMan.GoodsCategoryPrefix) continue;

            int quantity = BitConverter.ToInt32(buffer, at + GameDataMan.InventoryEntryQuantity);
            if (quantity <= 0) continue;

            yield return (itemId & GameDataMan.ItemIdMask, quantity);
        }
    }

    // Goods that must NEVER be granted or removed. Restore is exact-match, so a
    // missing entry means "delete it" — and deleting quest progression can
    // soft-lock a save. Learned abilities and great runes are not consumables at
    // all, so they are protected too. Everything else (plain consumables,
    // remembrances, tools, materials, the physick and its tears) is restorable.
    private static readonly HashSet<GoodsType> ProtectedGoodsTypes = new()
    {
        GoodsType.KeyItem,
        GoodsType.GreatRune,
        GoodsType.Sorcery,
        GoodsType.Incantation,
        GoodsType.SelfBuffSorcery,
        GoodsType.SelfBuffIncantation,
        GoodsType.SpiritSummonLesser,
        GoodsType.SpiritSummonGreater,
    };

    // Goods already owned by another part of the snapshot. The flask charges are
    // restored by FlaskSnapshot and the physick mix by PhysickSnapshot, so the
    // consumables pass must not also add or remove them — otherwise the two fight
    // each other (this is what made the cerulean flask vanish when an older line
    // was applied). Crystal tears are unique pickups consumed by the mix, so they
    // are left alone too.
    private static bool IsOwnedByAnotherSystem(uint goodId) =>
        (goodId >= 1000 && goodId < 1100) ||   // crimson / cerulean flask charges
        goodId == 250 || goodId == 251 ||      // Flask of Wondrous Physick
        (goodId >= 11000 && goodId < 11100);   // crystal tears

    // Restorable = actually a consumable. The decisive test is the param's own
    // "isConsume" flag: items consumed on use (pots, greases, kukri, exalted
    // flesh) are in scope, while permanently-owned tools (Spectral Steed Whistle,
    // Tarnished's Wizened Finger, Memory of Grace) and tutorial notes are not.
    // Without this the snapshot captured those permanent items and re-granted them
    // on every restore, producing a wall of "item acquired" popups.
    //
    // Unknown rows (param lookup failed, e.g. ids that resolve to no real item)
    // are treated as protected — never granted or removed.
    private bool IsRestorableConsumable(uint goodId)
    {
        if (IsOwnedByAnotherSystem(goodId)) return false;

        if (GetGoodsField(goodId, "isConsume") is not int isConsume || isConsume == 0)
            return false;

        var type = GetGoodsType(goodId);
        return type.HasValue && !ProtectedGoodsTypes.Contains(type.Value);
    }

    private GoodsType? GetGoodsType(uint goodId) =>
        GetGoodsField(goodId, "goodsType") is int raw ? (GoodsType)(byte)raw : null;

    // Reads an EquipParamGoods field by name, decoded through the param service so
    // BITFIELDS are handled. Many u8 fields here (isConsume, isEquip, isDiscard…)
    // are single bits packed into a shared byte — reading the raw byte returns the
    // whole packed set (e.g. 223) rather than the flag, which is what made the
    // consumable filter pass everything. Returns null if it cannot be resolved.
    private int? GetGoodsField(uint goodId, string fieldName)
    {
        try
        {
            var loaded = paramRepository.GetParam(Param.EquipParamGoods);
            var field = loaded?.Fields?.FirstOrDefault(f => f.InternalName == fieldName);
            if (field == null || loaded.RowSize <= 0) return null;

            var row = paramService.GetParamRow(EquipParamGoodsTable, EquipParamGoodsSlot, goodId);
            if (row == IntPtr.Zero) return null;

            var bytes = paramService.ReadRow(row, loaded.RowSize);
            var value = paramService.ReadFieldFromBytes(bytes, field);
            return value == null ? null : Convert.ToInt32(value);
        }
        catch { return null; }
    }

    private int EquipParamGoodsTable => ParamIndices.All["EquipParamGoods"].TableIndex;
    private int EquipParamGoodsSlot => ParamIndices.All["EquipParamGoods"].SlotIndex;

    #endregion

    private nint ResolvePlayerGameData()
    {
        var gameDataInstance = memoryService.Read<nint>(GameDataMan.Base);
        if (gameDataInstance == 0) return 0;
        return memoryService.Read<nint>(gameDataInstance + GameDataMan.PlayerGameData);
    }
}
