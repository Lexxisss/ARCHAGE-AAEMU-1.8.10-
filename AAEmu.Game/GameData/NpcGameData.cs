using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class NpcGameData : Singleton<NpcGameData>, IGameDataLoader
{
    private Dictionary<uint, List<NpcSkill>> _skillsForNpc;
    private Dictionary<uint, List<NpcPassiveBuff>> _passivesForNpc;
    public Dictionary<uint, NpcSpawnerNpc> _npcSpawnerTemplateNpcs;      // Id, nsn
    public Dictionary<uint, NpcSpawnerTemplate> _npcSpawnerTemplates;    // NpcSpawnerTemplateId, template
    public Dictionary<uint, List<uint>> _npcMemberAndSpawnerTemplateIds; // memberId, List<npcSpawnerId>
    private Dictionary<uint, List<NpcGroupMember>> _npcGroupMembers;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _skillsForNpc = new Dictionary<uint, List<NpcSkill>>();
        _passivesForNpc = new Dictionary<uint, List<NpcPassiveBuff>>();
        _npcSpawnerTemplateNpcs = new Dictionary<uint, NpcSpawnerNpc>();
        _npcSpawnerTemplates = new Dictionary<uint, NpcSpawnerTemplate>();
        _npcMemberAndSpawnerTemplateIds = new Dictionary<uint, List<uint>>();
        _npcGroupMembers = new Dictionary<uint, List<NpcGroupMember>>();

        using (var command = connection2.CreateCommand())
        {
            command.CommandText = "SELECT * FROM np_skills";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcSkill()
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    SkillId = reader.GetUInt32("skill_id"),
                    SkillUseCondition = (SkillUseConditionKind)reader.GetUInt32("skill_use_condition_id"),
                    SkillUseParam1 = reader.GetFloat("skill_use_param1"),
                    SkillUseParam2 = reader.GetFloat("skill_use_param2")
                };

                if (!_skillsForNpc.ContainsKey(template.OwnerId))
                    _skillsForNpc.Add(template.OwnerId, new List<NpcSkill>());

                _skillsForNpc[template.OwnerId].Add(template);
            }
        }

        using (var command = connection2.CreateCommand())
        {
            command.CommandText = "SELECT * FROM np_passive_buffs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcPassiveBuff()
                {
                    Id = reader.GetUInt32("id"),
                    OwnerId = reader.GetUInt32("owner_id"),
                    OwnerType = reader.GetString("owner_type"),
                    PassiveBuffId = reader.GetUInt32("passive_buff_id")
                };

                if (!_passivesForNpc.ContainsKey(template.OwnerId))
                    _passivesForNpc.Add(template.OwnerId, new List<NpcPassiveBuff>());

                _passivesForNpc[template.OwnerId].Add(template);
            }
        }

        // Spawn descriptors are part of the current NPC-spawn migration and live in
        // base.sqlite3. Keep this database choice local to this mechanic.
        using var spawnConnection = SQLite.CreateFallbackClientConnection();

        using (var command = spawnConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_spawners";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new NpcSpawnerTemplate();
                template.Id = reader.GetUInt32("id"); // matches NpcSpawnerTemplateId
                template.NpcSpawnerCategoryId = (NpcSpawnerCategory)reader.GetUInt32("npc_spawner_category_id");
                template.Name = reader.GetString("name");
                template.Comment = reader.GetString("comment", "");
                template.MaxPopulation = reader.GetUInt32("maxPopulation");
                template.StartTime = reader.GetFloat("startTime");
                template.EndTime = reader.GetFloat("endTime");
                template.DestroyTime = reader.GetFloat("destroyTime");
                template.SpawnDelayMin = reader.GetFloat("spawn_delay_min");
                // base.sqlite3 keeps its booleans as the text 't' and 'f', so they have to be read
                // as strings. Read as a plain boolean, every one of the twenty thousand active
                // spawners came back inactive, which is how the whole world ended up spawning from
                // npc_spawns.json instead of the placements.
                template.ActivationState = reader.GetBoolean("activation_state", true);
                template.SaveIndun = reader.GetBoolean("save_indun", true);
                template.MinPopulation = reader.GetUInt32("min_population");
                template.TestRadiusNpc = reader.GetFloat("test_radius_npc");
                template.TestRadiusPc = reader.GetFloat("test_radius_pc");
                template.SuspendSpawnCount = reader.GetUInt32("suspend_spawn_count");
                template.SpawnDelayMax = reader.GetFloat("spawn_delay_max");
                template.Npcs = new List<NpcSpawnerNpc>();
                _npcSpawnerTemplates.Add(template.Id, template);
            }
        }

        using (var command = spawnConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_spawner_npcs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var nsn = new NpcSpawnerNpc();
                nsn.Id = reader.GetUInt32("id");
                nsn.NpcSpawnerTemplateId = reader.GetUInt32("npc_spawner_id");
                nsn.MemberId = reader.GetUInt32("member_id");
                nsn.MemberType = reader.GetString("member_type");
                nsn.Weight = reader.GetFloat("weight");

                if (!_npcSpawnerTemplates.TryGetValue(nsn.NpcSpawnerTemplateId, out var spawnerTemplate))
                    continue;

                _npcSpawnerTemplateNpcs.Add(nsn.Id, nsn);
                spawnerTemplate.Npcs.Add(nsn);
            }
        }

        using (var command = spawnConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_group_members ORDER BY npc_group_id, is_leader DESC, id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var member = new NpcGroupMember
                {
                    Id = reader.GetUInt32("id"),
                    NpcGroupId = reader.GetUInt32("npc_group_id"),
                    NpcId = reader.GetUInt32("npc_id"),
                    IsLeader = reader.GetBoolean("is_leader", true),
                    IsMoveLeader = reader.GetBoolean("is_move_leader", true),
                    FormationOffset = new Vector3(
                        reader.GetFloat("formation_offset_x"),
                        reader.GetFloat("formation_offset_y"),
                        reader.GetFloat("formation_offset_z")),
                    FormationTension = reader.GetFloat("formation_tension")
                };

                if (!_npcGroupMembers.TryGetValue(member.NpcGroupId, out var members))
                {
                    members = new List<NpcGroupMember>();
                    _npcGroupMembers.Add(member.NpcGroupId, members);
                }
                members.Add(member);
            }
        }
    }

    public void AddNpcSpawner(NpcSpawnerTemplate template)
    {
        _npcSpawnerTemplates.Add(template.Id, template);
    }
    public void AddNpcSpawnerNpc(NpcSpawnerNpc nsn)
    {
        _npcSpawnerTemplateNpcs.Add(nsn.Id, nsn);
        //_npcSpawnerTemplates[nsn.NpcSpawnerTemplateId].Npcs.Add(nsn);
    }

    public void PostLoad()
    {
        foreach (var (templateId, skills) in _skillsForNpc)
        {
            NpcManager.Instance.BindSkillsToTemplate(templateId, skills);
        }

        foreach (var passiveBuff in _passivesForNpc.Values.SelectMany(i => i))
        {
            if (passiveBuff.PassiveBuff != null)
                continue;
            passiveBuff.PassiveBuff = SkillManager.Instance.GetPassiveBuffTemplate(passiveBuff.PassiveBuffId);
        }

        foreach (var (templateId, passives) in _passivesForNpc)
        {
            var template = NpcManager.Instance.GetTemplate(templateId);
            template?.PassiveBuffs.AddRange(passives);
        }
    }

    public void LoadMemberAndSpawnerTemplateIds()
    {
        _npcMemberAndSpawnerTemplateIds = new Dictionary<uint, List<uint>>();
        //var npcMemberAndSpawnerId = new Dictionary<uint, List<uint>>();

        foreach (var nsn in _npcSpawnerTemplateNpcs.Values)
        {
            if (!_npcMemberAndSpawnerTemplateIds.ContainsKey(nsn.MemberId))
            {
                _npcMemberAndSpawnerTemplateIds.Add(nsn.MemberId, new List<uint> { nsn.NpcSpawnerTemplateId });
            }
            else
            {
                _npcMemberAndSpawnerTemplateIds[nsn.MemberId].Add(nsn.NpcSpawnerTemplateId);
            }
        }
    }
    public void AddMemberAndSpawnerTemplateIds(NpcSpawnerNpc nsn)
    {
        if (!_npcMemberAndSpawnerTemplateIds.ContainsKey(nsn.MemberId))
            _npcMemberAndSpawnerTemplateIds.Add(nsn.MemberId, new List<uint> { nsn.NpcSpawnerTemplateId });
        else
            _npcMemberAndSpawnerTemplateIds[nsn.MemberId].Add(nsn.NpcSpawnerTemplateId);
    }

    public List<uint> GetSpawnerIds(uint memberId)
    {
        _npcMemberAndSpawnerTemplateIds.TryGetValue(memberId, out var list);

        return list;
    }

    public NpcSpawnerTemplate GetNpcSpawnerTemplate(uint npcSpawnerTemplateId)
    {
        _npcSpawnerTemplates.TryGetValue(npcSpawnerTemplateId, out var template);

        return template;
    }

    public IReadOnlyList<NpcGroupMember> GetNpcGroupMembers(uint npcGroupId)
    {
        return _npcGroupMembers.TryGetValue(npcGroupId, out var members) ? members : [];
    }

    public NpcSpawnerNpc GetNpcSpawnerNpc(uint spawnerId)
    {
        //_npcSpawnerTemplateNpcs.TryGetValue(spawnerId, out var nsn);
        return _npcSpawnerTemplateNpcs.Values.FirstOrDefault(nsn => nsn.NpcSpawnerTemplateId == spawnerId);
    }

    public List<NpcSkill> GetNpSkill(uint npcId, SkillUseConditionKind skillCondition = SkillUseConditionKind.None)
    {
        if (_skillsForNpc.ContainsKey(npcId))
        {
            if (skillCondition == SkillUseConditionKind.None)
                return _skillsForNpc[npcId];
            return _skillsForNpc[npcId].Where(npSkill => npSkill.SkillUseCondition == skillCondition).ToList();
        }

        return null;
    }
}
