using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Handles the item-choice confirmation sent after a selective item skill opens its picker.
/// Target x2game.dll f99d013b... reads this body in 0x399E1130:
/// slot type u8 (0x399E1159), slot u8 (0x399E1173), try count u32
/// (0x399E1193), selected count u32 (0x399E11AF), then at most 32 u32
/// selection values (0x399E11C2, 0x399E11E8-0x399E11FA).
/// </summary>
public class CSBagHandleSelectiveItemsPacket : GamePacket
{
    private const uint MaxSelectionValues = 32;

    public CSBagHandleSelectiveItemsPacket()
        : base(CSOffsets.CSBagHandleSelectiveItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var tryCount = stream.ReadUInt32();
        var selectedCount = stream.ReadUInt32();

        var valuesToRead = Math.Min(selectedCount, MaxSelectionValues);
        var selectedValues = new List<uint>((int)valuesToRead);
        for (var i = 0u; i < valuesToRead; i++)
            selectedValues.Add(stream.ReadUInt32());

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        lock (character.InventoryTransactionLock)
            HandleSelection(character, slotType, slot, tryCount, selectedCount, selectedValues);
    }

    private static void HandleSelection(
        Character character,
        SlotType slotType,
        byte slot,
        uint tryCount,
        uint selectedCount,
        IReadOnlyList<uint> selectedValues)
    {
        if (selectedCount == 0 || selectedCount > MaxSelectionValues || tryCount == 0)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "invalid_counts");
            return;
        }

        if (slotType != SlotType.Inventory)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "source_not_in_inventory");
            return;
        }

        var sourceItem = character.Inventory.GetItem(slotType, slot);
        if (sourceItem?.Template == null || sourceItem.Template.UseSkillId == 0)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "source_item_missing");
            return;
        }

        // The picker is the client's own window: it opens it from item data and sends only this
        // confirmation, so requiring the box to have been "armed" by a preceding skill cast
        // refused every real attempt. What matters is that this box is a selective one and that
        // the exchange below is atomic. A pending arm for a different box still loses, since that
        // is a confirmation answering the wrong window.
        if (character.PendingSelectiveItemId != 0
            && character.PendingSelectiveItemId != sourceItem.Id
            && character.PendingSelectiveItemExpiresAt >= DateTime.UtcNow)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "selection_armed_for_another_item");
            return;
        }

        var selective = SkillManager.Instance.GetSelectiveItems(sourceItem.Template.UseSkillId);
        if (selective == null || selective.ItemSelections.Count == 0)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "selective_effect_missing");
            return;
        }

        if ((!selective.IsMulti && tryCount != 1)
            || selectedCount != (uint)selective.SelectCount)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "selection_shape_mismatch");
            return;
        }

        int consumeCount;
        try
        {
            consumeCount = checked(selective.ConsumeItemCount * (int)tryCount);
        }
        catch (OverflowException)
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "consume_count_overflow");
            return;
        }

        if (consumeCount <= 0 || sourceItem.Count < consumeCount)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            Reject(character, slotType, slot, tryCount, selectedCount, "not_enough_source_items", false);
            return;
        }

        var selectedOrdinals = new HashSet<uint>();
        var rewards = new Dictionary<(uint itemId, int gradeId), int>();
        foreach (var selectedValue in selectedValues)
        {
            // The captured first-option body is count=1,value=1. Loading rows in id
            // order preserves the one-based option list displayed by the client.
            if (selectedValue == 0 || selectedValue > (uint)selective.ItemSelections.Count
                || !selectedOrdinals.Add(selectedValue))
            {
                Reject(character, slotType, slot, tryCount, selectedCount, "invalid_selection_value");
                return;
            }

            var selection = selective.ItemSelections[(int)selectedValue - 1];
            try
            {
                var rewardCount = checked(selection.Count * (int)tryCount);
                var key = (selection.ItemId, selection.GradeId);
                rewards[key] = checked(rewards.GetValueOrDefault(key) + rewardCount);
            }
            catch (OverflowException)
            {
                Reject(character, slotType, slot, tryCount, selectedCount, "reward_count_overflow");
                return;
            }
        }

        if (rewards.Count == 0 || rewards.Any(x => x.Key.itemId == 0 || x.Value <= 0))
        {
            Reject(character, slotType, slot, tryCount, selectedCount, "invalid_reward");
            return;
        }

        if (GrantSelection(character, sourceItem, consumeCount, rewards, tryCount, selectedValues))
        {
            character.PendingSelectiveItemId = 0;
            character.PendingSelectiveSkillId = 0;
            character.PendingSelectiveItemExpiresAt = default;
        }
    }

    private static bool GrantSelection(
        Character character,
        Item sourceItem,
        int consumeCount,
        IReadOnlyDictionary<(uint itemId, int gradeId), int> rewards,
        uint tryCount,
        IReadOnlyCollection<uint> selectedValues)
    {
        var bag = character.Inventory.Bag;
        var snapshot = bag.Items.ToDictionary(item => item.Id, item => item.Count);
        var tasks = new List<ItemTask>();
        var deferredRemovals = new List<Item>();
        var consumptionEvents = new List<(Item item, int count)>();
        var acquisitionEvents = new List<(Item item, int count, bool isNew)>();
        var deferredSyncPackets = new List<GamePacket>();
        var acquiredItems = new List<Item>();

        if (!bag.TryConsumeForTransaction(
                sourceItem.TemplateId,
                consumeCount,
                tasks,
                deferredRemovals,
                consumptionEvents,
                preferredItem: sourceItem))
        {
            RollBackBag(character, snapshot, deferredRemovals);
            character.SendErrorMessage(ErrorMessageType.ItemUpdateFail);
            return false;
        }

        foreach (var reward in rewards)
        {
            if (!bag.AcquireDefaultItemEx(
                    ItemTaskType.Invalid,
                    reward.Key.itemId,
                    reward.Value,
                    reward.Key.gradeId,
                    out var newItems,
                    out var updatedItems,
                    0,
                    -1,
                    tasks,
                    acquisitionEvents,
                    deferredSyncPackets))
            {
                RollBackBag(character, snapshot, deferredRemovals);
                character.SendErrorMessage(ErrorMessageType.BagFull);
                return false;
            }

            acquiredItems.AddRange(newItems);
            acquiredItems.AddRange(updatedItems);
        }

        SendTaskBatches(character, ItemTaskType.SkillEffectGainItem, tasks);
        bag.CommitDeferredTransactionRemovals(deferredRemovals);

        foreach (var (item, count) in consumptionEvents)
            character.Inventory.OnConsumedItem(item, count);
        foreach (var (item, count, isNew) in acquisitionEvents)
            character.Inventory.OnAcquiredItem(item, count, isNew);
        foreach (var syncPacket in deferredSyncPackets)
            character.SendPacket(syncPacket);

        SendAcquisitionBatches(character, acquiredItems.DistinctBy(item => item.Id).ToList());
        Logger.Info(
            "Selective item success: char={0}, sourceTemplate={1}, skill={2}, tries={3}, selected=[{4}]",
            character.Id,
            sourceItem.TemplateId,
            sourceItem.Template.UseSkillId,
            tryCount,
            string.Join(",", selectedValues));
        return true;
    }

    private static void SendTaskBatches(Character character, ItemTaskType taskType, IReadOnlyList<ItemTask> tasks)
    {
        const int maxTasks = 30;
        for (var offset = 0; offset < tasks.Count; offset += maxTasks)
            character.SendPacket(new SCItemTaskSuccessPacket(taskType, tasks.Skip(offset).Take(maxTasks).ToList(), []));
    }

    private static void SendAcquisitionBatches(Character character, IReadOnlyList<Item> items)
    {
        const int maxItems = 15;
        for (var offset = 0; offset < items.Count; offset += maxItems)
            character.SendPacket(new SCItemAcquisitionPacket(character.Name, items.Skip(offset).Take(maxItems).ToList()));
    }

    private static void RollBackBag(
        Character character,
        IReadOnlyDictionary<ulong, int> snapshot,
        List<Item> deferredRemovals)
    {
        var bag = character.Inventory.Bag;
        bag.RollBackDeferredTransactionRemovals(deferredRemovals);

        foreach (var item in bag.Items.ToArray())
        {
            if (snapshot.TryGetValue(item.Id, out var count))
            {
                item.Count = count;
                continue;
            }

            bag.RemoveItem(ItemTaskType.Invalid, item, true, true);
        }
        bag.UpdateFreeSlotCount();
    }

    private static void Reject(
        Character character,
        SlotType slotType,
        byte slot,
        uint tryCount,
        uint selectedCount,
        string reason,
        bool sendError = true)
    {
        Logger.Warn(
            "Selective item rejected: char={0}, slotType={1}, slot={2}, tries={3}, selectedCount={4}, reason={5}",
            character.Id, slotType, slot, tryCount, selectedCount, reason);
        if (sendError)
            character.SendErrorMessage(ErrorMessageType.ItemUpdateFail);
    }
}
