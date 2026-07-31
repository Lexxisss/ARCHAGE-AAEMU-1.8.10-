using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellItemsPacket : GamePacket
{
    private const int MaxCartEntries = 30;
    private const float MaxVendorDistance = 5f;

    private sealed class SaleEntry
    {
        public Item Item { get; init; }
        public int OriginalSlot { get; init; }
        public int Count { get; init; }
        public long Refund { get; init; }
        public bool FullStack { get; init; }
        public DateTime RemoveReservationTime { get; init; }
        public uint DbSlaveId { get; init; }
        public uint Type2 { get; init; }
        public Item BuyBackItem { get; set; }
    }

    public CSSellItemsPacket() : base(CSOffsets.CSSellItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        var npcObjId = stream.ReadBc();
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        var interactionObjId = stream.ReadBc();
        var count = stream.ReadByte();

        if (npc == null || npc.Template?.Merchant != true)
        {
            Fail(character, npcObjId, npc?.Template?.Id ?? 0, 0, 0, "invalid_vendor_npc", ErrorMessageType.StoreInvalidItem);
            return;
        }
        if (MathUtil.CalculateDistance(character.Transform.World.Position, npc.Transform.World.Position) > MaxVendorDistance)
        {
            Fail(character, npcObjId, npc.Template.Id, 0, 0, "vendor_too_far", ErrorMessageType.TooFarAway);
            return;
        }
        if (count == 0 || count > MaxCartEntries)
        {
            Fail(character, npcObjId, npc.Template.Id, 0, 0, "invalid_cart_size", ErrorMessageType.SellCartEmpty);
            return;
        }

        var sales = new List<SaleEntry>(count);
        var seenItems = new HashSet<ulong>();
        long totalRefund = 0;

        for (var i = 0; i < count; i++)
        {
            var slotType = (SlotType)stream.ReadByte();
            var slot = stream.ReadByte();
            var iid = stream.ReadUInt64();
            var requestedStack = stream.ReadUInt32();
            var removeReservationTime = stream.ReadDateTime();
            var clientTemplateId = stream.ReadUInt32();
            var dbSlaveId = stream.ReadUInt32();
            var clientType2 = stream.ReadUInt32();

            if (slotType != SlotType.Inventory || requestedStack == 0 || requestedStack > int.MaxValue)
            {
                Fail(character, npcObjId, npc.Template.Id, iid, unchecked((int)requestedStack), "invalid_sale_slot_or_count", ErrorMessageType.StoreInvalidItem);
                return;
            }

            var item = character.Inventory.Bag.GetItemBySlot(slot);
            if (item == null || item.Id != iid || item._holdingContainer != character.Inventory.Bag || item.OwnerId != character.Id || !seenItems.Add(iid))
            {
                Fail(character, npcObjId, npc.Template.Id, iid, unchecked((int)requestedStack), "item_identity_or_owner_mismatch", ErrorMessageType.StoreInvalidItem);
                return;
            }

            var saleCount = (int)requestedStack;
            if (saleCount > item.Count)
            {
                Fail(character, npcObjId, npc.Template.Id, iid, saleCount, "sale_count_exceeds_stack", ErrorMessageType.StoreInvalidItem);
                return;
            }
            if (item.Template?.Sellable != true)
            {
                Fail(character, npcObjId, npc.Template.Id, iid, saleCount, "item_not_sellable", ErrorMessageType.StoreNotSellableItem);
                return;
            }
            if (clientTemplateId != 0 && clientTemplateId != item.TemplateId)
            {
                Fail(character, npcObjId, npc.Template.Id, iid, saleCount, "client_template_mismatch", ErrorMessageType.StoreInvalidItem);
                return;
            }

            if (!VendorPriceResolver.TryResolveMoneyRefund(item.TemplateId, item.Grade, saleCount, out var refund, out var refundFailure))
            {
                Fail(character, npcObjId, npc.Template.Id, iid, saleCount, refundFailure, ErrorMessageType.StoreNotSellableItem);
                return;
            }

            try
            {
                totalRefund = checked(totalRefund + refund);
            }
            catch (OverflowException)
            {
                Fail(character, npcObjId, npc.Template.Id, iid, saleCount, "refund_total_overflow", ErrorMessageType.StoreInvalidItem);
                return;
            }

            sales.Add(new SaleEntry
            {
                Item = item,
                OriginalSlot = slot,
                Count = saleCount,
                Refund = refund,
                FullStack = saleCount == item.Count,
                RemoveReservationTime = removeReservationTime,
                DbSlaveId = dbSlaveId,
                Type2 = clientType2
            });

            Logger.Debug(
                "VendorSell request VendorNpcId={0} VendorTemplateId={1} InteractionObjId={2} ItemIid={3} ItemId={4} Count={5} RemoveReservationTime={6:o} DbSlaveId={7} Type2={8}",
                npcObjId, npc.Template.Id, interactionObjId, iid, item.TemplateId, saleCount,
                removeReservationTime, dbSlaveId, clientType2);
        }

        lock (character.InventoryTransactionLock)
        {
            // Revalidate every mutable value under the transaction lock.
            foreach (var sale in sales)
            {
                if (sale.Item._holdingContainer != character.Inventory.Bag || sale.Item.Count < sale.Count || sale.Item.Slot != sale.OriginalSlot)
                {
                    Fail(character, npcObjId, npc.Template.Id, sale.Item.Id, sale.Count, "sale_state_changed", ErrorMessageType.StoreUpdateInventory);
                    return;
                }
            }

            var moneyBefore = character.Money;
            try
            {
                _ = checked(character.Money + totalRefund);
            }
            catch (OverflowException)
            {
                Fail(character, npcObjId, npc.Template.Id, 0, 0, "money_balance_overflow", ErrorMessageType.StoreUpdateInventory);
                return;
            }

            var tasks = new List<ItemTask>();
            var applied = new List<SaleEntry>();

            foreach (var sale in sales)
            {
                if (sale.FullStack)
                {
                    var removeAction = new ItemStoreRemove(
                        sale.Item,
                        SlotType.Inventory,
                        (byte)sale.OriginalSlot,
                        sale.Count,
                        sale.RemoveReservationTime,
                        sale.DbSlaveId,
                        sale.Type2);
                    if (!character.BuyBackItems.AddOrMoveExistingItem(
                            ItemTaskType.Invalid, sale.Item, -1, suppressInventoryEvents: true))
                    {
                        RollBack(character, applied);
                        Fail(character, npcObjId, npc.Template.Id, sale.Item.Id, sale.Count, "buyback_move_failed", ErrorMessageType.StoreUpdateInventory);
                        return;
                    }

                    sale.BuyBackItem = sale.Item;
                    tasks.Add(removeAction);
                }
                else
                {
                    sale.Item.Count -= sale.Count;

                    var split = ItemManager.Instance.Create(sale.Item.TemplateId, sale.Count, sale.Item.Grade);
                    CopyStackMetadata(sale.Item, split);
                    if (split == null || !character.BuyBackItems.AddOrMoveExistingItem(
                            ItemTaskType.Invalid, split, -1, suppressInventoryEvents: true))
                    {
                        sale.Item.Count += sale.Count;
                        if (split != null)
                            ItemManager.Instance.ReleaseId(split.Id);
                        RollBack(character, applied);
                        Fail(character, npcObjId, npc.Template.Id, sale.Item.Id, sale.Count, "partial_sale_split_failed", ErrorMessageType.StoreUpdateInventory);
                        return;
                    }

                    sale.BuyBackItem = split;
                    tasks.Add(new ItemStoreRemove(
                        sale.Item,
                        SlotType.Inventory,
                        (byte)sale.OriginalSlot,
                        sale.Count,
                        sale.RemoveReservationTime,
                        sale.DbSlaveId,
                        sale.Type2));
                }

                applied.Add(sale);
            }

            character.Money += totalRefund;
            if (totalRefund != 0)
                tasks.Add(new MoneyChange(totalRefund));

            SendTaskBatches(character, tasks);

            foreach (var sale in sales)
                character.Inventory.OnConsumedItem(sale.Item, sale.Count);

            foreach (var sale in sales)
            {
                Logger.Info(
                    "VendorSell success VendorNpcId={0} VendorTemplateId={1} RequestedItemId={2} ItemIid={3} RequestedCount={4} ResolvedPrice={5} Currency=Money PlayerBalanceBefore={6} PlayerBalanceAfter={7} InventoryContainer={8} InventorySlot={9} ItemAction={10} TransactionResult=success",
                    npcObjId, npc.Template.Id, sale.Item.TemplateId, sale.Item.Id, sale.Count, sale.Refund,
                    moneyBefore, character.Money, SlotType.Inventory, sale.OriginalSlot,
                    "StoreRemove");
            }
        }
    }

    private static void CopyStackMetadata(Item source, Item destination)
    {
        if (source == null || destination == null)
            return;

        destination.ItemFlags = source.ItemFlags;
        destination.LifespanMins = source.LifespanMins;
        destination.MadeUnitId = source.MadeUnitId;
        destination.WorldId = source.WorldId;
        destination.CreateTime = source.CreateTime;
        destination.UnsecureTime = source.UnsecureTime;
        destination.UnpackTime = source.UnpackTime;
        destination.ImageItemTemplateId = source.ImageItemTemplateId;
        destination.ExpirationTime = source.ExpirationTime;
        destination.ExpirationOnlineMinutesLeft = source.ExpirationOnlineMinutesLeft;
        destination.ChargeUseSkillTime = source.ChargeUseSkillTime;
        destination.Flags = source.Flags;
        destination.Durability = source.Durability;
        destination.ChargeCount = source.ChargeCount;
        destination.ChargeStartTime = source.ChargeStartTime;
        destination.ChargeTime = source.ChargeTime;
        destination.ChargeProcTime = source.ChargeProcTime;
        destination.MappingFailBonus = source.MappingFailBonus;
        destination.ElementLevel = source.ElementLevel;
        destination.TemperPhysical = source.TemperPhysical;
        destination.TemperMagical = source.TemperMagical;
        destination.RuneId = source.RuneId;
        destination.UccId = source.UccId;
        destination.DetailType = source.DetailType;
        destination.GemIds = source.GemIds?.ToArray() ?? [];
        destination.Detail = source.Detail?.ToArray();
    }

    private static void SendTaskBatches(Character character, IReadOnlyList<ItemTask> tasks)
    {
        const int maxTasks = 30;
        for (var offset = 0; offset < tasks.Count; offset += maxTasks)
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreSell, tasks.Skip(offset).Take(maxTasks).ToList(), []));
    }

    private static void RollBack(Character character, IEnumerable<SaleEntry> applied)
    {
        foreach (var sale in applied.Reverse())
        {
            if (sale.FullStack)
            {
                character.Inventory.Bag.AddOrMoveExistingItem(
                    ItemTaskType.Invalid, sale.Item, sale.OriginalSlot, suppressInventoryEvents: true);
            }
            else
            {
                sale.Item.Count += sale.Count;
                if (sale.BuyBackItem != null)
                    character.BuyBackItems.RemoveItem(ItemTaskType.Invalid, sale.BuyBackItem, true, true);
            }
        }
    }

    private static void Fail(Character character, uint vendorNpcId, uint vendorTemplateId, ulong iid, int count, string reason, ErrorMessageType error)
    {
        Logger.Warn(
            "VendorSell failed VendorNpcId={0} VendorTemplateId={1} ItemIid={2} RequestedCount={3} TransactionResult=failed FailureReason={4}",
            vendorNpcId, vendorTemplateId, iid, count, reason);
        character.SendErrorMessage(error);
    }
}
