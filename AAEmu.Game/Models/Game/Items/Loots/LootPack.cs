using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootPack
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; set; }
    public string Name { get; set; }
    public bool WarDrop { get; set; }
    public uint ExtraGainActGroupId { get; set; }
    public uint GroupCount { get; set; }
    public List<Loot> Loots { get; set; }
    public Dictionary<uint, LootGroups> Groups { get; set; }
    public Dictionary<uint, LootActabilityGroups> ActabilityGroups { get; set; }
    public Dictionary<uint, List<Loot>> LootsByGroupNo { get; set; }

    // unused private List<(uint itemId, int count, byte grade)> _generatedPack;


    /// <summary>
    /// Generates the contents of a LootPack, in the form of a list of tuples. This list is stored internally
    /// </summary>
    /// <param name="player">Player who's loot multipliers need to be used</param>
    /// <returns></returns>
    public List<(uint itemId, int count, byte grade)> GeneratePack(Character player)
    {
        var lootDropRate = (100f + player.DropRateMul) / 100f;
        var lootGoldRate = (100f + player.LootGoldMul) / 100f;
        return GeneratePack(lootDropRate, lootGoldRate, player);
    }

    /// <summary>
    /// Generates the contents of a LootPack, in the form of a list of tuples. This list is stored internally
    /// </summary>
    /// <param name="lootDropRate">1.0f = 100%</param>
    /// <param name="lootGoldRate">1.0f = 100% applies to coins item only</param>
    /// <param name="player">Player to check quest-locked loot eligibility for; null skips that check (e.g. raw NPC corpse generation)</param>
    /// <returns></returns>
    public List<(uint itemId, int count, byte grade)> GeneratePack(float lootDropRate, float lootGoldRate, Character player = null)
    {
        // Use 8000022 as an example

        var items = new List<(uint itemId, int count, byte grade)>();

        // Logger.Info($"Rolling loot pack {Id} containing max group Id: {GroupCount}");

        // For every group
        for (uint gIdx = 0; gIdx <= GroupCount; gIdx++)
        {
            var hasLootGroup = false;
            var lootGradeDistribId = 0u;
            var alwaysDropGroup = gIdx == 0;

            if (!LootsByGroupNo.ContainsKey(gIdx))
                continue;

            // Logger.Debug($"Rolling loot with pack {Id}, Group {gIdx}/{GroupCount}, checking Groups conditions");

            // If that group has a LootGroup, roll the dice
            if (Groups.TryGetValue(gIdx, out var lootGroup))
            {
                hasLootGroup = true;
                lootGradeDistribId = lootGroup.ItemGradeDistributionId;
                var dice = (long)Rand.Next(0, 10000000);

                // Use generic loot multiplier for the groups ?
                dice = (long)Math.Floor(dice / (lootDropRate * AppConfiguration.Instance.World.LootRate));

                // Logger.Debug($"Rolling loot with pack {Id}, GroupNo {gIdx} rolled {dice}/{lootGroup.DropRate}");

                if ((lootGroup.DropRate > 1) && (dice > lootGroup.DropRate))
                    continue;
            }

            // Logger.Debug($"Rolling loot with pack {Id}, Group {gIdx}/{GroupCount}, checking ActAbilityGroups conditions");

            // If that group has a LootActGroup, roll the dice
            if (ActabilityGroups.TryGetValue(gIdx, out var actabilityGroup))
            {
                var dice = (long)Rand.Next(0, 10000);

                // Use generic loot multiplier for the ActGroups ?
                dice = (long)Math.Floor(dice / (lootDropRate * AppConfiguration.Instance.World.LootRate));

                // Logger.Debug($"Rolling loot with pack {Id}, ActAbilityGroupNo {gIdx} rolled {dice}/{actabilityGroup.MinDice}~{actabilityGroup.MaxDice}");

                // TODO: Use MinDice for something as well?
                if (dice > actabilityGroup.MaxDice)
                    continue;
            }

            var loots = LootsByGroupNo[gIdx];
            if (loots == null || loots.Count == 0)
                continue;

            var uniqueItemDrop = loots[0].DropRate == 1;
            var itemRoll = Rand.Next(0, 10000000);

            // Apply multiplier for loot drop rate
            itemRoll = (int)Math.Round(itemRoll / lootDropRate);

            var itemStackingRoll = 0u;

            List<Loot> selected = new List<Loot>();


            if ((alwaysDropGroup == false) && (uniqueItemDrop || hasLootGroup || (GroupCount <= 1)))
            {
                selected.Add(loots.RandomElementByWeight(l => l.DropRate));
            }
            else
            {

                selected.AddRange(loots.Where(loot => loot.AlwaysDrop || loot.DropRate == 10000000 || alwaysDropGroup).ToList());

                foreach (var loot in loots.Where(loot => !(loot.AlwaysDrop || loot.DropRate == 10000000 || alwaysDropGroup)))
                {
                    if (alwaysDropGroup)
                    {
                        selected.Add(loot);
                        continue;
                    }
                    if (loot.DropRate + itemStackingRoll < itemRoll)
                    {
                        itemStackingRoll += loot.DropRate;
                        continue;
                    }

                    itemStackingRoll += loot.DropRate;

                    selected.Add(loot);
                    break;
                }
            }

            // Replace any item missing from the (possibly still-migrating) item database with
            // the highest-drop-rate valid item from the same group, instead of dropping nothing.
            var finalSelected = new List<Loot>();
            foreach (var selectedPack in selected)
            {
                if (ItemManager.Instance.GetTemplate(selectedPack.ItemId) != null)
                {
                    finalSelected.Add(selectedPack);
                    continue;
                }

                var fallbackLoot = loots
                    .Where(l => ItemManager.Instance.GetTemplate(l.ItemId) != null)
                    .OrderByDescending(l => l.DropRate)
                    .FirstOrDefault();
                if (fallbackLoot != null)
                {
                    finalSelected.Add(fallbackLoot);
                    Logger.Debug("LootPack {0}: item {1} missing from item database, substituted {2}", Id, selectedPack.ItemId, fallbackLoot.ItemId);
                }
                else
                {
                    Logger.Warn("LootPack {0}: item {1} missing from item database and no valid fallback in group {2}", Id, selectedPack.ItemId, gIdx);
                }
            }

            foreach (var selectedPack in finalSelected)
            {
                // Quest-reward items should only drop for players who actually have that quest active
                if (player != null)
                {
                    var itemTemplate = ItemManager.Instance.GetTemplate(selectedPack.ItemId);
                    if (itemTemplate?.LootQuestId > 0 && !player.Quests.HasQuest(itemTemplate.LootQuestId))
                        continue;
                }

                var lootCount = Rand.Next(selectedPack.MinAmount, selectedPack.MaxAmount + 1);

                var grade = selectedPack.GradeId;
                if (lootGradeDistribId > 0)
                    grade = GetGradeFromDistribution(lootGradeDistribId);

                // Multiply gold as needed
                if (selectedPack.ItemId == Item.Coins)
                    lootCount = (int)Math.Round(lootCount * (lootGoldRate * AppConfiguration.Instance.World.GoldLootMultiplier));

                items.Add((selectedPack.ItemId, lootCount, grade));
            }
        }

        // unused _generatedPack = items;
        return items;
    }

    /// <summary>
    /// Generates the contents of a LootPack per loot-group weighted roll, including the profession
    /// proficiency bonus roll from ExtraGainActGroupId. This is the group-drop-chance model (each
    /// group in the pack rolls independently) as opposed to GeneratePack's one-item-total-per-group
    /// weighted-pick model; both exist because different loot packs are authored for either style.
    /// </summary>
    /// <param name="lootDropRate">1.0f = 100%</param>
    /// <param name="lootGoldRate">1.0f = 100% applies to coins item only</param>
    /// <param name="killer">Player credited with the kill/gather, used for the proficiency bonus roll and quest-item eligibility</param>
    /// <param name="eligiblePlayers">Players allowed to receive quest-locked loot from this pack (defaults to just killer)</param>
    /// <param name="actabilityType">Actability used to compute the ExtraGainActGroupId bonus-roll chance</param>
    /// <param name="npcGradeMultiplier">Extra multiplier applied to group/item roll thresholds, e.g. for higher-tier NPCs</param>
    public List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> GeneratePackNew(
        float lootDropRate,
        float lootGoldRate,
        Character killer,
        IEnumerable<Character> eligiblePlayers,
        ActabilityType actabilityType,
        float npcGradeMultiplier = 1.0f)
    {
        var items = new List<(uint itemId, int count, byte grade, uint lootGroupOrigin)>();

        GeneratePackItems(items, lootDropRate, lootGoldRate, killer, eligiblePlayers, npcGradeMultiplier);

        if (ExtraGainActGroupId > 0 && killer != null)
        {
            var statId = ExtraGainActGroupId switch
            {
                5 => 284, // Husbandry
                6 => 285, // Farming
                7 => 286, // Fishing
                8 => 287, // Logging
                9 => 288, // Gathering
                13 => 289, // Mining
                _ => 0
            };

            if (statId != 0)
            {
                var statValue = killer.CalculateWithBonuses(0, (UnitAttribute)statId);
                if (statValue > 0 && Rand.Next(0, 10000) < statValue)
                {
                    Logger.Debug("LootPack {0}: bonus roll triggered for {1} from ExtraGainActGroup {2} (stat {3}: {4})", Id, killer.Name, ExtraGainActGroupId, statId, statValue);
                    GeneratePackItems(items, lootDropRate, lootGoldRate, killer, eligiblePlayers, npcGradeMultiplier);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Convenience overload of <see cref="GeneratePackNew"/> that derives loot/gold rate from the player.
    /// </summary>
    public List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> GeneratePackNew(Character player, ActabilityType actabilityType)
    {
        var lootDropRate = (100f + player.DropRateMul) / 100f;
        var lootGoldRate = (100f + player.LootGoldMul) / 100f;
        return GeneratePackNew(lootDropRate, lootGoldRate, player, [player], actabilityType);
    }

    private void GeneratePackItems(
        List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> items,
        float lootDropRate,
        float lootGoldRate,
        Character killer,
        IEnumerable<Character> eligiblePlayers,
        float npcGradeMultiplier)
    {
        var eligiblePlayerList = eligiblePlayers?.ToList();

        bool IsLootEligible(Loot loot)
        {
            var itemTemplate = ItemManager.Instance.GetTemplate(loot.ItemId);
            if (itemTemplate?.LootQuestId > 0)
                return eligiblePlayerList != null && eligiblePlayerList.Any(p => p.Quests.HasQuest(itemTemplate.LootQuestId));

            return true;
        }

        foreach (var (groupNo, groupLootList) in LootsByGroupNo)
        {
            var group = Groups.GetValueOrDefault(groupNo);
            var selectedItemsByGroup = new Dictionary<uint, List<Loot>>();

            if (groupNo == 0)
            {
                foreach (var loot in groupLootList)
                {
                    if (!IsLootEligible(loot))
                        continue;

                    // Group 0 items always drop if AlwaysDrop or 100% drop rate
                    if (loot.AlwaysDrop || loot.DropRate == 10000000)
                    {
                        selectedItemsByGroup.TryAdd(0, []);
                        selectedItemsByGroup[0].Add(loot);
                        continue;
                    }

                    var itemRate = loot.DropRate / 10_000_000f;
                    var requiresDice = (long)Math.Floor(10_000_000f * itemRate * lootDropRate * AppConfiguration.Instance.World.LootRate * npcGradeMultiplier);
                    var dice = (long)Rand.Next(0, 10000000);
                    if (dice < requiresDice)
                    {
                        selectedItemsByGroup.TryAdd(0, []);
                        selectedItemsByGroup[0].Add(loot);
                    }
                }
            }
            else
            {
                // A group with no loot_groups row is assumed to always trigger (e.g. guaranteed gathering/doodad drops)
                var groupRate = group != null ? group.DropRate / 10_000_000f : 1.0f;
                var requiresDice = (long)Math.Floor(10_000_000f * groupRate * lootDropRate * AppConfiguration.Instance.World.LootRate * npcGradeMultiplier);
                var dice = (long)Rand.Next(0, 10000000);
                if (dice < requiresDice)
                {
                    var eligibleItems = groupLootList.Where(IsLootEligible).ToList();
                    if (eligibleItems.Count > 0)
                    {
                        var selected = eligibleItems.RandomElementByWeight(l => l.DropRate);
                        selectedItemsByGroup.TryAdd(groupNo, []);
                        selectedItemsByGroup[groupNo].Add(selected);
                    }
                }
            }

            foreach (var (groupId, loots) in selectedItemsByGroup)
            {
                foreach (var loot in loots)
                {
                    var count = Rand.Next(loot.MinAmount, loot.MaxAmount + 1);
                    if (loot.ItemId == Item.Coins)
                        count = (int)Math.Round(count * (lootGoldRate * AppConfiguration.Instance.World.GoldLootMultiplier));

                    var grade = loot.GradeId;
                    if (group?.ItemGradeDistributionId > 0)
                        grade = GetGradeFromDistribution(group.ItemGradeDistributionId);

                    items.Add((loot.ItemId, count, grade, loot.Group));
                }
            }
        }
    }

    /// <summary>
    /// Gives a lootpack generated via <see cref="GeneratePackNew"/> to the specified player.
    /// </summary>
    public bool GiveLootPack(Character character, ItemTaskType taskType, List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> generatedList)
    {
        foreach (var (itemId, count, _, _) in generatedList)
        {
            if (itemId == Item.Coins)
                continue;
            if (character.Inventory.Bag.SpaceLeftForItem(itemId) < count)
                return false;
        }

        var coinCount = 0;
        foreach (var (itemId, count, grade, _) in generatedList)
        {
            if (itemId == Item.Coins)
            {
                coinCount += count;
                continue;
            }

            var itemTemplate = ItemManager.Instance.GetTemplate(itemId);
            var gradeToAdd = itemTemplate?.FixedGrade > 0 ? itemTemplate.FixedGrade : grade;

            if (!character.Inventory.TryAddNewItem(taskType, itemId, count, gradeToAdd))
                Logger.Error($"Unable to give loot to {character.Name} - ItemId: {itemId} x {count} at grade {gradeToAdd}");
        }

        if (coinCount > 0)
            character.AddMoney(SlotType.Inventory, coinCount, taskType);

        return true;
    }

    public List<Item> GenerateNpcPackItems(ref ulong baseId, float lootDropRate = 1.0f, float lootGoldRate = 1.0f)
    {
        var packList = GeneratePack(lootDropRate, lootGoldRate);
        var itemList = packList
            .Select(tuple => ItemManager.Instance.Create(tuple.itemId, tuple.count, tuple.grade, false)).ToList();
        foreach (var item in itemList)
        {
            item.Id = ++baseId;
        }

        return itemList;
    }

    /// <summary>
    /// Gives a lootpack to the specified player. It is possible to pass in a pre-generated list if we wanted to do some extra checks on our player's inventory.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="taskType"></param>
    /// <param name="generatedList"></param>
    public bool GiveLootPack(Character character, ItemTaskType taskType, List<(uint itemId, int count, byte grade)> generatedList = null)
    {
        // If it is not generated yet, generate loot pack info now
        generatedList ??= GeneratePack(character);

        // Check for room before giving anything, so a full bag doesn't result in a partial loot pack
        foreach (var (itemId, count, _) in generatedList)
        {
            if (itemId == Item.Coins)
                continue;
            if (character.Inventory.Bag.SpaceLeftForItem(itemId) < count)
                return false;
        }

        var coinCount = 0;
        foreach (var (itemId, count, grade) in generatedList)
        {
            if (itemId == Item.Coins)
            {
                coinCount += count;
                continue;
            }

            var itemTemplate = ItemManager.Instance.GetTemplate(itemId);
            var gradeToAdd = itemTemplate?.FixedGrade > 0 ? itemTemplate.FixedGrade : grade;

            if (!character.Inventory.TryAddNewItem(taskType, itemId, count, gradeToAdd))
                Logger.Error($"Unable to give loot to {character.Name} - ItemId: {itemId} x {count} at grade {gradeToAdd}");
        }

        if (coinCount > 0)
            character.AddMoney(SlotType.Inventory, coinCount, taskType);

        return true;
    }

    private static byte GetGradeFromDistribution(uint id)
    {
        byte gradeId = 0;
        var distributions = ItemManager.Instance.GetGradeDistributions((byte)id);

        var array = new[]
        {
            distributions.Weight0, distributions.Weight1, distributions.Weight2, distributions.Weight3,
            distributions.Weight4, distributions.Weight5, distributions.Weight6, distributions.Weight7,
            distributions.Weight8, distributions.Weight9, distributions.Weight10, distributions.Weight11,
            distributions.Weight12
        };

        var old = 0;
        var gradeDrop = Rand.Next(0, 100);
        for (byte i = 0; i <= 12; i++)
        {
            if (gradeDrop <= array[i] + old)
            {
                gradeId = i;
                i = 12;
            }
            else
            {
                old += array[i];
            }
        }

        return gradeId;
    }
}
