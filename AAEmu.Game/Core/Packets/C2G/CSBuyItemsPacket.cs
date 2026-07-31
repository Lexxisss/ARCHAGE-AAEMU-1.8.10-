using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBuyItemsPacket : GamePacket
{
    private const int MaxCartEntries = 30;
    private const float MaxVendorDistance = 5f;

    private sealed class PurchaseEntry
    {
        public uint ItemId { get; init; }
        public int Count { get; init; }
        public Merchants Good { get; init; }
        public VendorPayment Payment { get; init; }
    }

    private sealed class BuyBackEntry
    {
        public Item Item { get; init; }
        public int OriginalSlot { get; init; }
        public long Cost { get; init; }
    }

    public CSBuyItemsPacket() : base(CSOffsets.CSBuyItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        var npcObjId = stream.ReadBc();
        var npc = WorldManager.Instance.GetNpc(npcObjId);

        var doodadObjId = stream.ReadBc();
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        var shopId = stream.ReadUInt32();
        var buyCount = stream.ReadByte();
        var buyBackCount = stream.ReadByte();

        if (buyCount > MaxCartEntries || buyBackCount > MaxCartEntries || buyCount + buyBackCount == 0)
        {
            Fail(character, npcObjId, npc?.Template?.Id ?? 0, shopId, 0, 0, "invalid_cart_size", ErrorMessageType.BuyCartEmpty);
            return;
        }

        if (npc == null || npc.Template?.Merchant != true)
        {
            Fail(character, npcObjId, npc?.Template?.Id ?? 0, shopId, 0, 0, "invalid_vendor_npc", ErrorMessageType.StoreInvalidItem);
            return;
        }

        if (MathUtil.CalculateDistance(character.Transform.World.Position, npc.Transform.World.Position) > MaxVendorDistance)
        {
            Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "vendor_too_far", ErrorMessageType.TooFarAway);
            return;
        }

        if (doodadObjId != 0)
        {
            if (doodad == null || MathUtil.CalculateDistance(character.Transform.World.Position, doodad.Transform.World.Position) > MaxVendorDistance)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "shop_doodad_invalid_or_far", ErrorMessageType.TooFarAway);
                return;
            }
        }

        var purchases = new List<PurchaseEntry>(buyCount);
        for (var i = 0; i < buyCount; i++)
        {
            var itemId = stream.ReadUInt32();
            var requestedGrade = stream.ReadByte();
            var count = stream.ReadInt32();
            var requestedCurrency = (ShopCurrencyType)stream.ReadByte();

            if (count <= 0)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, "invalid_purchase_count", ErrorMessageType.StoreInvalidItem);
                return;
            }

            if (!VendorGameData.Instance.TryGetMerchantGood(npc.Template.Id, itemId, requestedGrade, out var good))
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, "item_not_sold_by_vendor", ErrorMessageType.StoreNpcNotHandleThisItem);
                return;
            }

            if (good.PurchaseLimit > 0 && count > good.PurchaseLimit)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, "purchase_limit_exceeded", ErrorMessageType.StoreInvalidItem);
                return;
            }

            if (ItemManager.Instance.GetTemplate(itemId) == null)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, "item_template_missing", ErrorMessageType.StoreItemDoesNotExist);
                return;
            }

            if (!VendorPriceResolver.TryResolvePurchase(good, requestedCurrency, out var payment, out var priceFailure))
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, priceFailure, CurrencyError(requestedCurrency));
                return;
            }

            try
            {
                _ = payment.GetTotal(count);
            }
            catch (OverflowException)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, itemId, count, "purchase_cost_overflow", ErrorMessageType.StoreInvalidItem);
                return;
            }

            purchases.Add(new PurchaseEntry
            {
                ItemId = itemId,
                Count = count,
                Good = good,
                Payment = payment
            });
        }

        foreach (var group in purchases.GroupBy(x => x.Good.GoodsId))
        {
            var limit = group.First().Good.PurchaseLimit;
            if (limit <= 0)
                continue;

            var requestedTotal = group.Sum(x => (long)x.Count);
            if (requestedTotal > limit)
            {
                var first = group.First();
                Fail(character, npcObjId, npc.Template.Id, shopId, first.ItemId,
                    requestedTotal > int.MaxValue ? int.MaxValue : (int)requestedTotal,
                    "aggregate_purchase_limit_exceeded", ErrorMessageType.StoreInvalidItem);
                return;
            }
        }

        var buyBackEntries = new List<BuyBackEntry>(buyBackCount);
        var buyBackIds = new HashSet<ulong>();
        for (var i = 0; i < buyBackCount; i++)
        {
            var slot = stream.ReadInt32();
            var item = character.BuyBackItems.GetItemBySlot(slot);
            if (item == null || !buyBackIds.Add(item.Id))
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "invalid_buyback_slot", ErrorMessageType.StoreInvalidItem);
                return;
            }

            if (!VendorPriceResolver.TryResolveMoneyRefund(item.TemplateId, item.Grade, item.Count, out var cost, out var refundFailure))
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, item.TemplateId, item.Count, refundFailure, ErrorMessageType.StoreInvalidItem);
                return;
            }

            buyBackEntries.Add(new BuyBackEntry { Item = item, OriginalSlot = slot, Cost = cost });
        }

        var useAaPoint = stream.ReadBoolean();
        var openType = stream.ReadByte();
        if (useAaPoint)
        {
            Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "aa_point_payment_not_supported_by_vendor_schema", ErrorMessageType.StoreInvalidItem);
            return;
        }

        lock (character.InventoryTransactionLock)
        {
            // Buy-back entries were decoded before taking the inventory lock. Revalidate
            // their identity and price while protected so a concurrent inventory action
            // cannot swap the slot or alter the item between validation and mutation.
            foreach (var entry in buyBackEntries)
            {
                var current = character.BuyBackItems.GetItemBySlot(entry.OriginalSlot);
                if (current == null || current.Id != entry.Item.Id || current.Count != entry.Item.Count ||
                    current.TemplateId != entry.Item.TemplateId || current.Grade != entry.Item.Grade ||
                    !VendorPriceResolver.TryResolveMoneyRefund(
                        current.TemplateId, current.Grade, current.Count, out var currentCost, out _) ||
                    currentCost != entry.Cost)
                {
                    Fail(character, npcObjId, npc.Template.Id, shopId, entry.Item.TemplateId, entry.Item.Count,
                        "buyback_changed_during_request", ErrorMessageType.StoreInvalidItem);
                    return;
                }
            }

            if (!TryCalculateCosts(purchases, buyBackEntries, out var money, out var honor, out var vocation, out var itemCosts, out var costFailure))
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, costFailure, ErrorMessageType.StoreInvalidItem);
                return;
            }

            if (money > character.Money)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "not_enough_money", ErrorMessageType.NotEnoughMoney);
                return;
            }
            if (honor > character.HonorPoint)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "not_enough_honor", ErrorMessageType.NotEnoughHonorPoint);
                return;
            }
            if (vocation > character.VocationPoint)
            {
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "not_enough_vocation", ErrorMessageType.StoreCantBuyWithLivingPoint);
                return;
            }

            foreach (var itemCost in itemCosts)
            {
                character.Inventory.Bag.GetAllItemsByTemplate(itemCost.Key, -1, out _, out var owned);
                if (owned < itemCost.Value)
                {
                    Fail(character, npcObjId, npc.Template.Id, shopId, itemCost.Key, itemCost.Value, "not_enough_item_currency", ErrorMessageType.NotEnoughItem);
                    return;
                }
            }

            var moneyBefore = character.Money;
            var bagSnapshot = character.Inventory.Bag.Items.ToDictionary(x => x.Id, x => x.Count);
            var tasks = new List<ItemTask>();
            var acquiredItems = new List<Item>();
            var movedBuyBack = new List<BuyBackEntry>();
            var deferredRemovals = new List<Item>();
            var acquisitionEvents = new List<(Item item, int count, bool isNew)>();
            var consumptionEvents = new List<(Item item, int count)>();
            var deferredSyncPackets = new List<GamePacket>();

            // Consume item currencies first. This prevents a purchase of the same
            // template from accidentally consuming the newly-created item and also
            // lets fully-consumed currency stacks free inventory slots atomically.
            foreach (var itemCost in itemCosts)
            {
                if (!character.Inventory.Bag.TryConsumeForTransaction(
                        itemCost.Key, itemCost.Value, tasks, deferredRemovals, consumptionEvents))
                {
                    Logger.Error(
                        "Vendor transaction invariant failed: char={0}, npc={1}, currencyItem={2}, count={3}",
                        character.Id, npc.Template.Id, itemCost.Key, itemCost.Value);
                    RollBackBag(character, bagSnapshot, deferredRemovals);
                    Fail(character, npcObjId, npc.Template.Id, shopId, itemCost.Key, itemCost.Value, "item_currency_mutation_failed", ErrorMessageType.StoreUpdateInventory);
                    return;
                }
            }

            if (!HasInventoryCapacity(character, purchases, buyBackEntries.Count))
            {
                RollBackBag(character, bagSnapshot, deferredRemovals);
                Fail(character, npcObjId, npc.Template.Id, shopId, 0, 0, "inventory_full", ErrorMessageType.BagFull);
                return;
            }

            foreach (var purchase in purchases)
            {
                if (!character.Inventory.Bag.AcquireDefaultItemEx(
                        ItemTaskType.Invalid,
                        purchase.ItemId,
                        purchase.Count,
                        purchase.Good.GradeId,
                        out var newItems,
                        out var updatedItems,
                        0,
                        -1,
                        tasks,
                        acquisitionEvents,
                        deferredSyncPackets))
                {
                    RollBackBuyBack(character, movedBuyBack);
                    RollBackBag(character, bagSnapshot, deferredRemovals);
                    Fail(character, npcObjId, npc.Template.Id, shopId, purchase.ItemId, purchase.Count, "inventory_mutation_failed", ErrorMessageType.StoreUpdateInventory);
                    return;
                }

                acquiredItems.AddRange(newItems);
                acquiredItems.AddRange(updatedItems);
            }

            foreach (var entry in buyBackEntries)
            {
                if (!character.Inventory.Bag.AddOrMoveExistingItem(
                        ItemTaskType.Invalid, entry.Item, -1, suppressInventoryEvents: true))
                {
                    RollBackBuyBack(character, movedBuyBack);
                    RollBackBag(character, bagSnapshot, deferredRemovals);
                    Fail(character, npcObjId, npc.Template.Id, shopId, entry.Item.TemplateId, entry.Item.Count, "buyback_inventory_mutation_failed", ErrorMessageType.StoreUpdateInventory);
                    return;
                }

                movedBuyBack.Add(entry);
                tasks.Add(new ItemBuyback(entry.Item));
                acquiredItems.Add(entry.Item);
                acquisitionEvents.Add((entry.Item, entry.Item.Count, true));
            }

            character.Money -= money;
            character.HonorPoint -= honor;
            character.VocationPoint -= vocation;

            if (money != 0)
                tasks.Add(new MoneyChange(-money));
            if (honor != 0)
                tasks.Add(new ChangeGamePoint((byte)GamePointKind.Honor, -honor));
            if (vocation != 0)
                tasks.Add(new ChangeGamePoint((byte)GamePointKind.Vocation, -vocation));

            SendTaskBatches(character, ItemTaskType.StoreBuy, tasks);
            character.Inventory.Bag.CommitDeferredTransactionRemovals(deferredRemovals);
            foreach (var (item, count) in consumptionEvents)
                character.Inventory.OnConsumedItem(item, count);
            foreach (var (item, count, isNew) in acquisitionEvents)
                character.Inventory.OnAcquiredItem(item, count, isNew);
            foreach (var syncPacket in deferredSyncPackets)
                character.SendPacket(syncPacket);

            if (honor != 0 || vocation != 0)
                character.SendPacket(new SCCharacterGamePointsPacket(character));
            if (acquiredItems.Count > 0)
                SendAcquisitionBatches(character, acquiredItems.DistinctBy(x => x.Id).ToList());

            foreach (var purchase in purchases)
            {
                Logger.Info(
                    "VendorBuy success VendorNpcId={0} VendorTemplateId={1} ShopId={2} GoodsId={3} RequestedItemId={4} ResolvedItemId={5} RequestedCount={6} ResolvedGrade={7} ResolvedPrice={8} Currency={9} CurrencyItemId={10} PlayerBalanceBefore={11} PlayerBalanceAfter={12} ItemAction=Create/AddStack TransactionResult=success OpenType={13}",
                    npcObjId, npc.Template.Id, shopId, purchase.Good.GoodsId, purchase.ItemId, purchase.Good.ItemId,
                    purchase.Count, purchase.Good.GradeId, purchase.Payment.UnitPrice, purchase.Payment.Kind,
                    purchase.Payment.ItemCurrencyId, moneyBefore, character.Money, openType);
            }
        }
    }

    private static bool TryCalculateCosts(
        IReadOnlyCollection<PurchaseEntry> purchases,
        IReadOnlyCollection<BuyBackEntry> buyBackEntries,
        out long money,
        out int honor,
        out int vocation,
        out Dictionary<uint, int> itemCosts,
        out string failureReason)
    {
        long moneyTotal = 0;
        long honorTotal = 0;
        long vocationTotal = 0;
        var itemTotals = new Dictionary<uint, long>();
        failureReason = string.Empty;

        try
        {
            foreach (var purchase in purchases)
            {
                var total = purchase.Payment.GetTotal(purchase.Count);
                switch (purchase.Payment.Kind)
                {
                    case VendorPaymentKind.Money:
                        moneyTotal = checked(moneyTotal + total);
                        break;
                    case VendorPaymentKind.Honor:
                        honorTotal = checked(honorTotal + total);
                        break;
                    case VendorPaymentKind.Vocation:
                        vocationTotal = checked(vocationTotal + total);
                        break;
                    case VendorPaymentKind.Item:
                        itemTotals[purchase.Payment.ItemCurrencyId] = checked(itemTotals.GetValueOrDefault(purchase.Payment.ItemCurrencyId) + total);
                        break;
                }
            }

            foreach (var entry in buyBackEntries)
                moneyTotal = checked(moneyTotal + entry.Cost);

            if (honorTotal > int.MaxValue || vocationTotal > int.MaxValue || itemTotals.Values.Any(x => x > int.MaxValue))
                throw new OverflowException();
        }
        catch (OverflowException)
        {
            money = 0;
            honor = vocation = 0;
            itemCosts = new Dictionary<uint, int>();
            failureReason = "transaction_total_overflow";
            return false;
        }

        money = moneyTotal;
        honor = (int)honorTotal;
        vocation = (int)vocationTotal;
        itemCosts = itemTotals.ToDictionary(x => x.Key, x => (int)x.Value);
        return true;
    }

    private static bool HasInventoryCapacity(Character character, IReadOnlyCollection<PurchaseEntry> purchases, int buyBackCount)
    {
        long requiredSlots = buyBackCount;
        foreach (var group in purchases.GroupBy(x => (x.ItemId, x.Good.GradeId)))
        {
            var template = ItemManager.Instance.GetTemplate(group.Key.ItemId);
            if (template == null || template.MaxCount <= 0)
                return false;

            character.Inventory.Bag.GetAllItemsByTemplate(group.Key.ItemId, group.Key.GradeId, out var current, out _);
            var existingCapacity = current.Sum(x => Math.Max(0, template.MaxCount - x.Count));
            var requested = group.Sum(x => (long)x.Count);
            var remaining = Math.Max(0L, requested - existingCapacity);
            requiredSlots += (remaining + template.MaxCount - 1L) / template.MaxCount;
            if (requiredSlots > character.Inventory.Bag.FreeSlotCount)
                return false;
        }

        return requiredSlots <= character.Inventory.Bag.FreeSlotCount;
    }

    private static void SendTaskBatches(Character character, ItemTaskType taskType, IReadOnlyList<ItemTask> tasks)
    {
        const int maxTasks = 30;
        for (var offset = 0; offset < tasks.Count; offset += maxTasks)
        {
            var batch = tasks.Skip(offset).Take(maxTasks).ToList();
            character.SendPacket(new SCItemTaskSuccessPacket(taskType, batch, []));
        }
    }

    private static void SendAcquisitionBatches(Character character, IReadOnlyList<Item> items)
    {
        const int maxItems = byte.MaxValue;
        for (var offset = 0; offset < items.Count; offset += maxItems)
            character.SendPacket(new SCItemAcquisitionPacket(character.Name, items.Skip(offset).Take(maxItems).ToList()));
    }

    private static void RollBackBag(Character character, IReadOnlyDictionary<ulong, int> snapshot, List<Item> deferredRemovals)
    {
        character.Inventory.Bag.RollBackDeferredTransactionRemovals(deferredRemovals);

        foreach (var item in character.Inventory.Bag.Items.ToArray())
        {
            if (snapshot.TryGetValue(item.Id, out var count))
            {
                item.Count = count;
                continue;
            }

            character.Inventory.Bag.RemoveItem(ItemTaskType.Invalid, item, true, true);
        }
        character.Inventory.Bag.UpdateFreeSlotCount();
    }

    private static void RollBackBuyBack(Character character, IEnumerable<BuyBackEntry> moved)
    {
        foreach (var entry in moved.Reverse())
            character.BuyBackItems.AddOrMoveExistingItem(
                ItemTaskType.Invalid, entry.Item, entry.OriginalSlot, suppressInventoryEvents: true);
    }

    private static ErrorMessageType CurrencyError(ShopCurrencyType currency)
    {
        return currency switch
        {
            ShopCurrencyType.Honor => ErrorMessageType.StoreCantBuyWithHonor,
            ShopCurrencyType.VocationBadges => ErrorMessageType.StoreCantBuyWithLivingPoint,
            _ => ErrorMessageType.StoreCantBuyWithMoney
        };
    }

    private static void Fail(
        Character character,
        uint vendorNpcId,
        uint vendorTemplateId,
        uint shopId,
        uint itemId,
        int count,
        string reason,
        ErrorMessageType error)
    {
        Logger.Warn(
            "VendorBuy failed VendorNpcId={0} VendorTemplateId={1} ShopId={2} RequestedItemId={3} RequestedCount={4} TransactionResult=failed FailureReason={5}",
            vendorNpcId, vendorTemplateId, shopId, itemId, count, reason);
        character.SendErrorMessage(error);
    }
}
