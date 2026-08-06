using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SpecialtyManager : Singleton<SpecialtyManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Specialty> _specialties;
    private Dictionary<uint, SpecialtyBundleItem> _specialtyBundleItems;
    private Dictionary<uint, SpecialtyNpc> _specialtyNpc;
    private HashSet<(uint FromZoneGroupId, uint ToZoneGroupId)> _specialtyRoutes;
    private Dictionary<uint, List<FreshnessGroupItem>> _freshnessGroupItems;
    private Dictionary<uint, SpecialtyEventTrigger> _specialtyEventTriggers;
    private List<SpecialtyEvent> _specialtyEvents;
    private Dictionary<uint, HashSet<uint>> _itemSetItems;

    //                 itemId           bundleId
    private Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>> _specialtyBundleItemsMapped;
    //                 itemId           zoneGroupId
    private Dictionary<uint, Dictionary<uint, double>> _priceRatios;
    //                 itemId           zoneId
    private Dictionary<uint, Dictionary<uint, int>> _soldPackAmountInTick;

    public void Load()
    {
        _specialties = new Dictionary<uint, Specialty>();
        _specialtyBundleItems = new Dictionary<uint, SpecialtyBundleItem>();
        _specialtyNpc = new Dictionary<uint, SpecialtyNpc>();
        _specialtyRoutes = new HashSet<(uint FromZoneGroupId, uint ToZoneGroupId)>();
        _freshnessGroupItems = new Dictionary<uint, List<FreshnessGroupItem>>();
        _specialtyEventTriggers = new Dictionary<uint, SpecialtyEventTrigger>();
        _specialtyEvents = new List<SpecialtyEvent>();
        _itemSetItems = new Dictionary<uint, HashSet<uint>>();
        _soldPackAmountInTick = new Dictionary<uint, Dictionary<uint, int>>();

        _specialtyBundleItemsMapped = new Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>>();
        _priceRatios = new Dictionary<uint, Dictionary<uint, double>>();

        Logger.Info("SpecialtyManager is loading...");

        ItemManager.Instance.OnItemsLoaded += OnItemsLoaded;

        using (var connection = SQLite.CreateTargetClientConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialties";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Specialty();
                        template.Id = reader.GetUInt32("id");
                        template.RowZoneGroupId = reader.GetUInt32("row_zone_group_id");
                        template.ColZoneGroupId = reader.GetUInt32("col_zone_group_id");
                        _specialties.Add(template.Id, template);
                        // In the client matrix col is the source axis and row is the destination axis.
                        _specialtyRoutes.Add((template.ColZoneGroupId, template.RowZoneGroupId));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_bundle_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpecialtyBundleItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                        template.Profit = reader.GetUInt32("profit");
                        template.Ratio = reader.GetInt32("ratio");
                        _specialtyBundleItems.Add(template.Id, template);

                        if (!_specialtyBundleItemsMapped.ContainsKey(template.ItemId))
                            _specialtyBundleItemsMapped.Add(template.ItemId, new Dictionary<uint, SpecialtyBundleItem>());

                        _specialtyBundleItemsMapped[template.ItemId].Add(template.SpecialtyBundleId, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpecialtyNpc();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        //template.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                        template.ZoneGroupId = reader.GetUInt32("zone_group_id", 0);

                        // One current target row per NPC. Keep TryAdd so malformed duplicate rows cannot
                        // silently replace the acceptance bundle already loaded for that template.
                        _specialtyNpc.TryAdd(template.NpcId, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM freshness_group_items ORDER BY freshness_group_id, time, id";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var item = new FreshnessGroupItem
                        {
                            Id = reader.GetUInt32("id"),
                            FreshnessGroupId = reader.GetUInt32("freshness_group_id"),
                            RewardRate = reader.GetUInt32("reward_rate", 1000),
                            SellerShareRatio = reader.GetUInt32("seller_share_ratio", 0),
                            Time = reader.GetUInt32("time", 0),
                            Tooltip = reader.GetString("tooltip", string.Empty)
                        };

                        if (!_freshnessGroupItems.TryGetValue(item.FreshnessGroupId, out var items))
                        {
                            items = new List<FreshnessGroupItem>();
                            _freshnessGroupItems.Add(item.FreshnessGroupId, items);
                        }

                        items.Add(item);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_event_triggers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var trigger = new SpecialtyEventTrigger
                        {
                            Id = reader.GetUInt32("id"),
                            CheckTime = reader.GetUInt32("check_time", 0),
                            EventRate = reader.GetUInt32("event_rate", 0),
                            EventTime = reader.GetUInt32("event_time", 0),
                            TriggerType = reader.GetUInt32("trigger_type", 0),
                            TriggerSubjectType = reader.GetString("trigger_subject_type", string.Empty),
                            TriggerSubjectId = reader.GetUInt32("trigger_subject_id", 0),
                            TriggerValue1 = reader.GetUInt32("trigger_value_1", 0),
                            TriggerValue2 = reader.GetUInt32("trigger_value_2", 0),
                            ZoneGroupId = reader.GetUInt32("zone_group_id", 0)
                        };
                        _specialtyEventTriggers.TryAdd(trigger.Id, trigger);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_events";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        _specialtyEvents.Add(new SpecialtyEvent
                        {
                            Id = reader.GetUInt32("id"),
                            EventType = reader.GetUInt32("event_type", 0),
                            EventObjectType = reader.GetString("event_object_type", string.Empty),
                            EventObjectId = reader.GetUInt32("event_object_id", 0),
                            EventValue = reader.GetUInt32("event_value", 1000),
                            TriggerId = reader.GetUInt32("specialty_event_trigger_id", 0),
                            TooltipText = reader.GetString("tooltip_text", string.Empty)
                        });
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT item_set_id, item_id FROM item_set_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var itemSetId = reader.GetUInt32("item_set_id");
                        if (!_itemSetItems.TryGetValue(itemSetId, out var itemIds))
                        {
                            itemIds = new HashSet<uint>();
                            _itemSetItems.Add(itemSetId, itemIds);
                        }

                        itemIds.Add(reader.GetUInt32("item_id"));
                    }
                }
            }
        }

        Logger.Info("SpecialtyManager loaded");
    }

    public static void Initialize()
    {
        var ratioConsumeTask = new SpecialtyRatioConsumeTask();
        TaskManager.Instance.Schedule(ratioConsumeTask, TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioDecreaseTickMinutes), TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioDecreaseTickMinutes));

        var ratioRegenTask = new SpecialtyRatioRegenTask();
        TaskManager.Instance.Schedule(ratioRegenTask, TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioRegenTickMinutes), TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioRegenTickMinutes));
    }

    public void OnItemsLoaded(object sender, EventArgs e)
    {
        foreach (var specialtyBundleItem in _specialtyBundleItems.Values)
        {
            specialtyBundleItem.Item = ItemManager.Instance.GetTemplate(specialtyBundleItem.ItemId);
        }
    }

    /// <summary>
    /// Returns the current demand ratio for the equipped specialty pack at the destination.
    /// </summary>
    public int GetRatioForSpecialty(Character player, uint destinationZoneGroupId)
    {
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null || destinationZoneGroupId == 0)
            return 0;

        InitRatioInZoneForPack(backpack.TemplateId, destinationZoneGroupId);
        return (int)Math.Floor(_priceRatios[backpack.TemplateId][destinationZoneGroupId]);
    }

    public int GetRatioForSpecialty(Character player)
    {
        var destinationZoneGroupId = ZoneManager.Instance.GetZoneByKey(player.Transform.ZoneId)?.GroupId ?? 0;
        return GetRatioForSpecialty(player, destinationZoneGroupId);
    }

    /// <summary>
    /// Gets a list of items and their current trade-rate for given zones
    /// </summary>
    /// <param name="fromZoneGroupId">Zone where the item was made</param>
    /// <param name="toZoneGroupId">Zone where the item is traded in</param>
    /// <returns></returns>
    public List<(uint, uint)> GetRatiosForTargetRoute(uint fromZoneGroupId, uint toZoneGroupId)
    {
        var res = new List<(uint, uint)>();
        if (!_specialtyRoutes.Contains((fromZoneGroupId, toZoneGroupId)))
            return res;

        // Get list of possible source packs
        var sourcePacks = ItemManager.Instance.GetAllItems().Where(x => x.SpecialtyZoneId == fromZoneGroupId);
        foreach (var item in sourcePacks.Take(128))
        {
            InitRatioInZoneForPack(item.Id, toZoneGroupId);
            res.Add((item.Id, (uint)Math.Floor(_priceRatios[item.Id][toZoneGroupId])));
        }

        return res;
    }

    /// <summary>
    /// Builds the sale list shown by one specialty buyer.
    /// SC 0x0018 is limited by the target client to 20 goods per packet; chunking is done by the packet handler.
    /// </summary>
    public List<SpecialtyGoods> GetGoodsForNpc(Character player, uint npcObjId)
    {
        var result = new List<SpecialtyGoods>();
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || !_specialtyNpc.TryGetValue(npc.TemplateId, out var specialtyNpc))
            return result;

        if (MathUtil.CalculateDistance(player.Transform.World.Position, npc.Transform.World.Position) > 2.5)
            return result;

        var destinationZoneGroupId = specialtyNpc.ZoneGroupId != 0
            ? specialtyNpc.ZoneGroupId
            : ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        if (destinationZoneGroupId == 0)
            return result;

        var paysItemPoints = npc.Template.SpecialtyCoinId != 0;
        foreach (var bundleItem in _specialtyBundleItems.Values
                     .Where(x => x.SpecialtyBundleId == specialtyNpc.SpecialtyBundleId)
                     .OrderBy(x => x.ItemId))
        {
            var goods = BuildGoodsRecord(bundleItem, destinationZoneGroupId, paysItemPoints);
            if (goods != null)
                result.Add(goods);
        }

        return result;
    }

    /// <summary>
    /// Builds the item-specific response used by SC 0x0100. The dedicated handler keys this
    /// request by the character's verified current zone group and the requested item id.
    /// </summary>
    public List<SpecialtyGoods> GetRatioGoods(ushort destinationZoneGroupId, uint itemId)
    {
        var result = new List<SpecialtyGoods>();
        if (!_specialtyBundleItemsMapped.TryGetValue(itemId, out var bundleItems))
            return result;

        var acceptedBundleIds = _specialtyNpc.Values
            .Where(x => x.ZoneGroupId == destinationZoneGroupId)
            .Select(x => x.SpecialtyBundleId)
            .ToHashSet();
        if (acceptedBundleIds.Count == 0)
            return result;

        foreach (var bundleId in acceptedBundleIds.OrderBy(x => x))
        {
            if (!bundleItems.TryGetValue(bundleId, out var bundleItem))
                continue;

            var goods = BuildGoodsRecord(bundleItem, destinationZoneGroupId, false);
            if (goods != null)
            {
                result.Add(goods);
                break;
            }
        }

        return result;
    }

    private SpecialtyGoods BuildGoodsRecord(SpecialtyBundleItem bundleItem, uint destinationZoneGroupId,
        bool paysItemPoints)
    {
        var item = bundleItem?.Item;
        var sourceZoneGroupId = item?.SpecialtyZoneId ?? 0;
        if (item == null || sourceZoneGroupId == 0 || destinationZoneGroupId == 0)
            return null;

        var baseCopper = CalculateBaseAmount(bundleItem);
        if (baseCopper <= 0)
            return null;

        InitRatioInZoneForPack(bundleItem.ItemId, destinationZoneGroupId);
        var demandRatio = (uint)Math.Clamp(
            (int)Math.Floor(_priceRatios[bundleItem.ItemId][destinationZoneGroupId]), 0, int.MaxValue);

        var demandCopper = (long)Math.Floor(baseCopper * (demandRatio / 100d));
        var eventRate = GetActiveSaleEventRate(destinationZoneGroupId, bundleItem.ItemId);
        var currentCopper = ApplyPerMille(demandCopper, eventRate);

        return new SpecialtyGoods
        {
            ItemId = bundleItem.ItemId,
            CurrentAmount = paysItemPoints ? (long)Math.Round(currentCopper / 10000d) : currentCopper,
            BaseAmount = paysItemPoints ? (long)Math.Round(baseCopper / 10000d) : baseCopper,
            Ratio = demandRatio,
            Stock = GetSoldPackCount(bundleItem.ItemId, destinationZoneGroupId),
            CanProduce = true,
            // Dev client EnumCurrency registration: GOLD=0, ITEM_POINT=6
            // (x2game-dev.dll 0x399F06B4-0x399F089A).
            Currency = paysItemPoints ? (sbyte)6 : (sbyte)0,
            // Dedicated resolves the final byte as item grade through 0x39CA0970 before
            // constructing the shared specialty record at 0x39824340-0x39824429.
            Grade = ResolveSpecialtyGrade(item)
        };
    }

    private static byte ResolveSpecialtyGrade(ItemTemplate item)
    {
        if (item == null || item.FixedGrade < 0)
            return 0;

        return (byte)Math.Clamp(item.FixedGrade, byte.MinValue, byte.MaxValue);
    }

    private static long CalculateBaseAmount(SpecialtyBundleItem bundleItem)
    {
        if (bundleItem?.Item == null)
            return 0;

        // Route value is data-driven: each destination bundle has its own profit/ratio row.
        // No separate geometric-distance coefficient exists in specialties or specialty_bundle_items.
        return (long)Math.Floor(bundleItem.Profit * (bundleItem.Ratio / 1000d)) + bundleItem.Item.Refund;
    }

    private static long ApplyPerMille(long amount, uint rate)
    {
        return (long)Math.Floor(amount * (rate / 1000d));
    }

    /// <summary>
    /// Returns the active sale-price event multiplier. specialty_events.event_value is per-mille:
    /// 1300/1500 are +30%/+50%, and 900 is -10% in the target client data.
    /// Only trigger type 4 (zone war-state) is activated here because the server has authoritative
    /// state and timing for it. Stock and quest triggers remain disabled until their counters exist.
    /// </summary>
    private uint GetActiveSaleEventRate(uint destinationZoneGroupId, uint itemId)
    {
        foreach (var specialtyEvent in _specialtyEvents)
        {
            // event_type 3 is the sale-price event. event_type 2 is a purchase/production discount.
            if (specialtyEvent.EventType != 3 || specialtyEvent.EventValue == 0 ||
                !SpecialtyEventMatchesItem(specialtyEvent, itemId) ||
                !_specialtyEventTriggers.TryGetValue(specialtyEvent.TriggerId, out var trigger) ||
                !IsSpecialtyEventTriggerActive(trigger, destinationZoneGroupId))
                continue;

            return specialtyEvent.EventValue;
        }

        return 1000;
    }

    public IReadOnlyList<uint> GetActiveEventItemIds(uint destinationZoneGroupId, uint itemId)
    {
        if (itemId == 0)
            return Array.Empty<uint>();

        return GetActiveSaleEventRate(destinationZoneGroupId, itemId) == 1000
            ? Array.Empty<uint>()
            : new[] { itemId };
    }

    public IReadOnlyList<uint> GetActiveEventItemIds(uint destinationZoneGroupId, IEnumerable<uint> itemIds)
    {
        return (itemIds ?? Array.Empty<uint>())
            .Distinct()
            .Where(itemId => GetActiveSaleEventRate(destinationZoneGroupId, itemId) != 1000)
            .Take(50)
            .ToArray();
    }

    private bool SpecialtyEventMatchesItem(SpecialtyEvent specialtyEvent, uint itemId)
    {
        if (string.Equals(specialtyEvent.EventObjectType, "Item", StringComparison.OrdinalIgnoreCase))
            return specialtyEvent.EventObjectId == itemId;

        return string.Equals(specialtyEvent.EventObjectType, "ItemSet", StringComparison.OrdinalIgnoreCase) &&
               _itemSetItems.TryGetValue(specialtyEvent.EventObjectId, out var items) &&
               items.Contains(itemId);
    }

    private static bool IsSpecialtyEventTriggerActive(SpecialtyEventTrigger trigger, uint destinationZoneGroupId)
    {
        if (trigger == null || trigger.TriggerType != 4 || trigger.ZoneGroupId != destinationZoneGroupId ||
            !string.Equals(trigger.TriggerSubjectType, "EnumHonorPointWarState", StringComparison.OrdinalIgnoreCase))
            return false;

        if (trigger.TriggerSubjectId != (uint)ZoneConflictType.War &&
            trigger.TriggerSubjectId != (uint)ZoneConflictType.Peace)
            return false;

        var conflict = ZoneManager.Instance.GetZoneGroupById(destinationZoneGroupId)?.Conflict;
        if (conflict == null || (uint)conflict.CurrentZoneState != trigger.TriggerSubjectId)
            return false;

        if (trigger.EventTime == 0)
            return true;

        var stateDurationMinutes = conflict.CurrentZoneState == ZoneConflictType.War
            ? conflict.WarMin
            : conflict.PeaceMin;
        if (stateDurationMinutes <= 0 || conflict.NextStateTime == DateTime.MinValue)
            return false;

        var stateStartedAt = conflict.NextStateTime.AddMinutes(-stateDurationMinutes);
        var eventEndsAt = stateStartedAt.AddSeconds(trigger.EventTime);
        var now = DateTime.UtcNow;
        return now >= stateStartedAt && now < eventEndsAt;
    }

    /// <summary>
    /// Dedicated 0x39B33650 selects the first freshness row whose time threshold is not less
    /// than the elapsed item age. The selected row exposes reward_rate and seller_share_ratio.
    /// </summary>
    private FreshnessGroupItem GetFreshnessGroupItem(Item backpack)
    {
        if (backpack?.Template is not BackpackTemplate backpackTemplate ||
            backpackTemplate.FreshnessGroupId == 0 ||
            !_freshnessGroupItems.TryGetValue(backpackTemplate.FreshnessGroupId, out var items) ||
            items.Count == 0)
            return null;

        var createTime = backpack.CreateTime;
        if (createTime == DateTime.MinValue || createTime == DateTime.MaxValue)
            return null;

        if (createTime.Kind == DateTimeKind.Unspecified)
            createTime = DateTime.SpecifyKind(createTime, DateTimeKind.Utc);
        else
            createTime = createTime.ToUniversalTime();

        var elapsedSeconds = Math.Max(0d, (DateTime.UtcNow - createTime).TotalSeconds);
        var selected = items.FirstOrDefault(x => elapsedSeconds <= x.Time);
        return selected ?? items[^1];
    }

    private uint GetSoldPackCount(uint itemId, uint destinationZoneGroupId)
    {
        if (!_soldPackAmountInTick.TryGetValue(itemId, out var zones) ||
            !zones.TryGetValue(destinationZoneGroupId, out var count) || count <= 0)
            return 0;

        return (uint)count;
    }

    public int GetBasePriceForSpecialty(Character player, uint npcId)
    {
        // Sanity checks
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return 0;
        }

        var npc = WorldManager.Instance.GetNpc(npcId);
        if (npc == null)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return 0;
        }

        if (MathUtil.CalculateDistance(player.Transform.World.Position, npc.Transform.World.Position) > 2.5)
        {
            player.SendErrorMessage(ErrorMessageType.TooFarAway);
            return 0;
        }

        if (!_specialtyNpc.TryGetValue(npc.TemplateId, out var specialtyNpc))
        {
            player.SendErrorMessage(ErrorMessageType.StoreCantSellSameZone);
            return 0;
        }

        var destinationZoneGroupId = specialtyNpc.ZoneGroupId != 0
            ? specialtyNpc.ZoneGroupId
            : ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        var sourceZoneGroupId = backpack.Template?.SpecialtyZoneId ?? 0;
        if (sourceZoneGroupId == 0 || destinationZoneGroupId == 0 ||
            sourceZoneGroupId == destinationZoneGroupId)
        {
            player.SendErrorMessage(ErrorMessageType.StoreCantSellSameZone);
            return 0;
        }

        var bundleIdAtNPC = specialtyNpc.SpecialtyBundleId;

        if (!_specialtyBundleItemsMapped.ContainsKey(backpack.TemplateId))
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        if (!_specialtyBundleItemsMapped[backpack.TemplateId].TryGetValue(bundleIdAtNPC, out var value))
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        var bundleItem = value;
        if (bundleItem == null)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        if (bundleItem.Item == null)
            return 0;

        return checked((int)CalculateBaseAmount(bundleItem));
    }

    public int SellSpecialty(Character player, uint npcObjId)
    {
        if (player.LaborPower < 60)
        {
            player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return 0;
        }

        var basePrice = GetBasePriceForSpecialty(player, npcObjId);

        if (basePrice == 0) // We had an error, no need to keep going
            return basePrice;

        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return 0;
        }

        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || !_specialtyNpc.TryGetValue(npc.TemplateId, out var specialtyNpc))
            return 0;

        var destinationZoneGroupId = specialtyNpc.ZoneGroupId != 0
            ? specialtyNpc.ZoneGroupId
            : ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        var priceRatio = GetRatioForSpecialty(player, destinationZoneGroupId);
        if (priceRatio <= 0)
            return 0;

        // Our backpack isn't null, we have the NPC, time to calculate the profits.
        var demandPrice = (long)Math.Floor(basePrice * (priceRatio / 100d));
        var eventRate = GetActiveSaleEventRate(destinationZoneGroupId, backpack.TemplateId);
        var eventPrice = ApplyPerMille(demandPrice, eventRate);
        var freshness = GetFreshnessGroupItem(backpack);
        var freshnessRate = freshness?.RewardRate ?? 1000;
        var finalPrice = ApplyPerMille(eventPrice, freshnessRate);

        // Dedicated 0x39822E01-0x39822EAA reads reward_rate from +0x04 and, when non-zero,
        // seller_share_ratio from +0x08 multiplied by 10 (percent -> per-mille).
        // Profit sharing is still gated by the server feature flag below.
        uint crafterId = backpack.MadeUnitId != player.Id ? backpack.MadeUnitId : 0;
        var sellerShare = freshness?.SellerShareRatio > 0
            ? freshness.SellerShareRatio / 100f
            : 0.80f;

        // GetSpecialtyInterest in the dedicated server reads game-rule ids 206/207 for two
        // item kinds (0x399B8850-0x399B8902). Those authoritative server rule values are not
        // present in the supplied client SQLite, so no numeric negotiation bonus is invented.
        var interestRate = 0;
        var amountBonus = checked((int)(finalPrice - demandPrice));

        var itemTypeToDeliver = npc.Template.SpecialtyCoinId;
        var amountOfItemsTotalPayout = checked((int)finalPrice);
        var amountOfItemsSeller = amountOfItemsTotalPayout;
        var amountOfItemsCrafter = 0;
        var amountOfItemsBase = basePrice;

        if (npc.Template.SpecialtyCoinId != 0)
        {
            // Items are listed in the DB at the same rate as "amounts of gold" so the value needs to be divided by 10000
            amountOfItemsTotalPayout = (int)Math.Round(amountOfItemsTotalPayout / 10000f);
            amountOfItemsSeller = (int)Math.Round(amountOfItemsSeller / 10000f);
            amountOfItemsBase = (int)Math.Round(basePrice / 10000f);
        }
        else
        {
            itemTypeToDeliver = Item.Coins;
        }

        // TODO: implement a global fsets
        var fsets = new Models.Game.Features.FeatureSet();

        // Split up the profit if needed
        if ((crafterId != 0) && (crafterId != player.Id) && fsets.Check(Models.Game.Features.Feature.backpackProfitShare))
        {
            amountOfItemsSeller = (int)Math.Round(amountOfItemsTotalPayout * sellerShare);
            amountOfItemsCrafter = amountOfItemsTotalPayout - amountOfItemsSeller;
        }

        // Mail for seller
        if (amountOfItemsSeller > 0) // This check is here for if you'd create custom packs that give 100% to crafter and 0% for delivery
        {
            var sellerMail = new MailForSpeciality(player, crafterId, backpack.TemplateId, priceRatio, itemTypeToDeliver, amountOfItemsBase, amountBonus, amountOfItemsSeller, amountOfItemsCrafter, interestRate);
            sellerMail.FinalizeForSeller();
            if (!sellerMail.Send())
            {
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                return basePrice;
            }
        }

        // Mail for crafter. If seller is not crafter, send a crafter mail as well
        if ((amountOfItemsCrafter > 0) && (crafterId != 0))
        {
            var crafterMail = new MailForSpeciality(player, crafterId, backpack.TemplateId, priceRatio, itemTypeToDeliver, amountOfItemsBase, amountBonus, amountOfItemsSeller, amountOfItemsCrafter, interestRate);
            crafterMail.FinalizeForCrafter();
            if (!crafterMail.Send())
            {
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                // return; // don't cancel here if we fail to send mail to crafter
            }
        }

        // Delete the backpack
        player.Inventory.Equipment.ConsumeItem(ItemTaskType.SellBackpack, backpack.TemplateId, 1, backpack);
        player.Quests?.OnBackpackSold(backpack.TemplateId);
        // TODO: Calculate proper labor by skill level
        player.ChangeLabor(-60, (int)ActabilityType.Commerce);

        // Add one pack sold in this zone during this tick
        var zoneGroupId = destinationZoneGroupId;
        if (!_soldPackAmountInTick.ContainsKey(backpack.TemplateId))
            _soldPackAmountInTick.Add(backpack.TemplateId, new Dictionary<uint, int>());

        if (!_soldPackAmountInTick[backpack.TemplateId].ContainsKey(zoneGroupId))
            _soldPackAmountInTick[backpack.TemplateId].Add(zoneGroupId, 0);

        _soldPackAmountInTick[backpack.TemplateId][zoneGroupId] += 1;

        return basePrice;
    }

    public uint ResolveSpecialtyNpcObjId(uint firstObjId, uint secondObjId)
    {
        var firstNpc = WorldManager.Instance.GetNpc(firstObjId);
        if (firstNpc != null && _specialtyNpc.ContainsKey(firstNpc.TemplateId))
            return firstObjId;

        var secondNpc = WorldManager.Instance.GetNpc(secondObjId);
        if (secondNpc != null && _specialtyNpc.ContainsKey(secondNpc.TemplateId))
            return secondObjId;

        return 0;
    }

    public uint GetDestinationZoneGroup(uint npcObjId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || !_specialtyNpc.TryGetValue(npc.TemplateId, out var specialtyNpc))
            return 0;

        return specialtyNpc.ZoneGroupId != 0
            ? specialtyNpc.ZoneGroupId
            : ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
    }

    public void ConsumeRatio()
    {
        foreach (var (itemId, zoneInfo) in _soldPackAmountInTick)
        {
            foreach (var (zoneGroupId, count) in zoneInfo)
            {
                if (count <= 0)
                    continue;

                var ratioDecrease = (int)Math.Ceiling(count * AppConfiguration.Instance.Specialty.RatioDecreasePerPack);
                InitRatioInZoneForPack(itemId, zoneGroupId);
                _soldPackAmountInTick[itemId][zoneGroupId] = 0;

                var initialRatio = _priceRatios[itemId][zoneGroupId];
                _priceRatios[itemId][zoneGroupId] = Math.Max(AppConfiguration.Instance.Specialty.MinSpecialtyRatio, initialRatio - ratioDecrease);
            }
        }
    }

    public void RegenRatio()
    {
        foreach (var soldPackItems in _soldPackAmountInTick)
        {
            foreach (var soldPacksInZone in soldPackItems.Value)
            {
                InitRatioInZoneForPack(soldPackItems.Key, soldPacksInZone.Key);
                var initialRatio = _priceRatios[soldPackItems.Key][soldPacksInZone.Key];
                _priceRatios[soldPackItems.Key][soldPacksInZone.Key] = Math.Min(
                    AppConfiguration.Instance.Specialty.MaxSpecialtyRatio,
                    initialRatio + AppConfiguration.Instance.Specialty.RatioIncreasePerTick);
            }
        }
    }

    /// <summary>
    /// Makes sure a base rate exists for the given item and zone combination
    /// </summary>
    /// <param name="itemId"></param>
    /// <param name="zoneGroupId"></param>
    private void InitRatioInZoneForPack(uint itemId, uint zoneGroupId)
    {
        if (!_priceRatios.ContainsKey(itemId))
            _priceRatios.Add(itemId, new Dictionary<uint, double>());

        if (!_priceRatios[itemId].ContainsKey(zoneGroupId))
            _priceRatios[itemId].Add(zoneGroupId, AppConfiguration.Instance.Specialty.MaxSpecialtyRatio);
    }

    // Dummy for tests
    public static int GetValueOfOne()
    {
        return 1;
    }
}
