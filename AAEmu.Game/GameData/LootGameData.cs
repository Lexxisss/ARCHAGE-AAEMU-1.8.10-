using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class LootGameData : Singleton<LootGameData>, IGameDataLoader
{
    // Main data 
    private Dictionary<uint, LootPack> _lootPacks;

    // Data storage
    private Dictionary<uint, Loot> _loots;
    private Dictionary<uint, LootGroups> _lootGroups;
    private Dictionary<uint, LootActabilityGroups> _lootActabilityGroups;

    // Simple refmaps for ease of use
    private Dictionary<uint, List<Loot>> _lootsByPackId;
    private Dictionary<uint, List<LootGroups>> _lootGroupsByPackId;
    private Dictionary<uint, List<LootActabilityGroups>> _lootActabilityGroupsByPackId;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _lootPacks = new Dictionary<uint, LootPack>();

        _loots = new Dictionary<uint, Loot>();
        _lootGroups = new Dictionary<uint, LootGroups>();
        _lootActabilityGroups = new Dictionary<uint, LootActabilityGroups>();

        _lootsByPackId = new Dictionary<uint, List<Loot>>();
        _lootGroupsByPackId = new Dictionary<uint, List<LootGroups>>();
        _lootActabilityGroupsByPackId = new Dictionary<uint, List<LootActabilityGroups>>();

        // loots/loot_groups/loot_actability_groups/loot_packs live only in base.sqlite3,
        // not in the target client database or the server reference table database.
        using var lootConnection = SQLite.CreateConnection("Data", SQLite.FallbackClientDatabase);

        // table 'loots'
        using (var command = lootConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loots";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Loot()
                    {
                        Id = reader.GetUInt32("id"),
                        Group = reader.GetUInt32("group"),
                        ItemId = reader.GetUInt32("item_id"),
                        DropRate = reader.GetUInt32("drop_rate"),
                        MinAmount = reader.GetInt32("min_amount"),
                        MaxAmount = reader.GetInt32("max_amount"),
                        LootPackId = reader.GetUInt32("loot_pack_id"),
                        GradeId = reader.GetByte("grade_id"),
                        AlwaysDrop = reader.GetBoolean("always_drop", true),
                        MsgToWorld = reader.GetBoolean("msg_to_world")
                    };

                    _loots.Add(template.Id, template);

                    if (!_lootsByPackId.ContainsKey(template.LootPackId))
                        _lootsByPackId.Add(template.LootPackId, new List<Loot>());

                    _lootsByPackId[template.LootPackId].Add(template);
                }
            }
        }

        // table 'loot_groups'
        using (var command = lootConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loot_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new LootGroups()
                    {
                        Id = reader.GetUInt32("id"),
                        PackId = reader.GetUInt32("pack_id"),
                        GroupNo = reader.GetUInt32("group_no"),
                        DropRate = reader.GetUInt32("drop_rate"),
                        ItemGradeDistributionId = reader.GetByte("item_grade_distribution_id"),
                        ZoneGroupId = reader.GetUInt32("zone_group_id")
                    };

                    _lootGroups.Add(template.Id, template);

                    if (!_lootGroupsByPackId.ContainsKey(template.PackId))
                        _lootGroupsByPackId.Add(template.PackId, new List<LootGroups>());

                    _lootGroupsByPackId[template.PackId].Add(template);
                }
            }
        }

        // table 'loot_actability_groups'
        using (var command = lootConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loot_actability_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new LootActabilityGroups()
                    {
                        Id = reader.GetUInt32("id"),
                        LootPackId = reader.GetUInt32("loot_pack_id"),
                        GroupId = reader.GetUInt32("loot_group_id"),
                        MaxDice = reader.GetUInt32("max_dice"),
                        MinDice = reader.GetUInt32("min_dice")
                    };

                    _lootActabilityGroups.Add(template.Id, template);

                    if (!_lootActabilityGroupsByPackId.ContainsKey(template.LootPackId))
                        _lootActabilityGroupsByPackId.Add(template.LootPackId, new List<LootActabilityGroups>());

                    _lootActabilityGroupsByPackId[template.LootPackId].Add(template);
                }
            }
        }

        // table 'loot_packs'. Reading the pack list itself (rather than inferring packs from
        // 'loots' rows) ensures packs with no direct loot entries - referenced only through
        // loot_groups/loot_actability_groups, or genuinely empty but still referenced by an
        // npc - still get created instead of silently resolving to null.
        var packMeta = new Dictionary<uint, (string name, bool warDrop, uint extraGainActGroupId)>();
        using (var command = lootConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM loot_packs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var id = reader.GetUInt32("id");
                    packMeta[id] = (
                        reader.GetString("name"),
                        reader.GetBoolean("war_drop"),
                        reader.GetUInt32("extra_gain_act_group_id"));
                }
            }
        }

        // Generate packs. Union of loot_packs rows and any pack id only referenced by
        // loots/loot_groups/loot_actability_groups, in case loot_packs is incomplete.
        var packIds = new HashSet<uint>(packMeta.Keys);
        packIds.UnionWith(_lootsByPackId.Keys);
        packIds.UnionWith(_lootGroupsByPackId.Keys);
        packIds.UnionWith(_lootActabilityGroupsByPackId.Keys);

        foreach (var lootPackId in packIds)
        {
            packMeta.TryGetValue(lootPackId, out var meta);
            var pack = new LootPack()
            {
                Id = lootPackId,
                Name = meta.name,
                WarDrop = meta.warDrop,
                ExtraGainActGroupId = meta.extraGainActGroupId,
                Loots = _lootsByPackId.GetValueOrDefault(lootPackId, new List<Loot>()),
                Groups = new Dictionary<uint, LootGroups>(),
                ActabilityGroups = new Dictionary<uint, LootActabilityGroups>(),
                LootsByGroupNo = new Dictionary<uint, List<Loot>>(),
                GroupCount = 0
            };

            if (_lootGroupsByPackId.TryGetValue(lootPackId, out var lootGroupsList))
                foreach (var lootGroup in lootGroupsList)
                    pack.Groups.Add(lootGroup.GroupNo, lootGroup);

            if (_lootActabilityGroupsByPackId.TryGetValue(lootPackId, out var lootActAbilityGroups))
                foreach (var lag in lootActAbilityGroups)
                    pack.ActabilityGroups.Add(lag.GroupId, lag);

            foreach (var loot in pack.Loots)
            {
                if (!pack.LootsByGroupNo.ContainsKey(loot.Group))
                    pack.LootsByGroupNo.Add(loot.Group, new List<Loot>());

                pack.LootsByGroupNo[loot.Group].Add(loot);

                if (pack.GroupCount < loot.Group)
                    pack.GroupCount = loot.Group;
            }

            _lootPacks.Add(pack.Id, pack);
        }
    }

    public void PostLoad()
    {
    }

    public LootPack GetPack(uint id)
    {
        if (_lootPacks.TryGetValue(id, out var pack))
            return pack;
        return null;
    }
}
