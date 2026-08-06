using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Spheres;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class SphereGameData : Singleton<SphereGameData>, IGameDataLoader
{
    private Dictionary<uint, List<Spheres>> _spheres;
    private Dictionary<uint, List<SphereQuests>> _sphereQuests;
    private Dictionary<uint, List<SphereSkills>> _sphereSkills;
    private Dictionary<uint, List<SphereSounds>> _sphereSounds;
    private Dictionary<uint, List<SphereDoodadInteracts>> _sphereDoodadInteracts;
    private Dictionary<uint, List<SphereChatBubbles>> _sphereChatBubbles;
    private Dictionary<uint, List<SphereBuffs>> _sphereBuffs;
    private Dictionary<uint, List<SphereBubbles>> _sphereBubbles;
    private Dictionary<uint, List<SphereAcceptQuests>> _sphereAcceptQuests;
    private Dictionary<uint, List<SphereAcceptQuestQuests>> _sphereAcceptQuestQuests;
    private Dictionary<uint, List<PlotUnitRequirement>> _sphereUnitReqs;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _spheres = new Dictionary<uint, List<Spheres>>();
        _sphereQuests = new Dictionary<uint, List<SphereQuests>>();
        _sphereSkills = new Dictionary<uint, List<SphereSkills>>();
        _sphereSounds = new Dictionary<uint, List<SphereSounds>>();
        _sphereDoodadInteracts = new Dictionary<uint, List<SphereDoodadInteracts>>();
        _sphereChatBubbles = new Dictionary<uint, List<SphereChatBubbles>>();
        _sphereBuffs = new Dictionary<uint, List<SphereBuffs>>();
        _sphereBubbles = new Dictionary<uint, List<SphereBubbles>>();
        _sphereAcceptQuests = new Dictionary<uint, List<SphereAcceptQuests>>();
        _sphereAcceptQuestQuests = new Dictionary<uint, List<SphereAcceptQuestQuests>>();
        _sphereUnitReqs = new Dictionary<uint, List<PlotUnitRequirement>>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT
                    target.id,
                    target.name,
                    COALESCE(target.enter_or_leave, fallback.enter_or_leave) AS enter_or_leave,
                    COALESCE(target.sphere_detail_id, fallback.sphere_detail_id) AS sphere_detail_id,
                    COALESCE(target.sphere_detail_type, fallback.sphere_detail_type) AS sphere_detail_type,
                    COALESCE(target.trigger_condition_id, fallback.trigger_condition_id) AS trigger_condition_id,
                    COALESCE(target.trigger_condition_time, fallback.trigger_condition_time, 0) AS trigger_condition_time,
                    target.team_msg,
                    COALESCE(target.category_id, fallback.category_id) AS category_id,
                    COALESCE(target.or_unit_reqs, fallback.or_unit_reqs) AS or_unit_reqs,
                    COALESCE(target.is_personal_msg, fallback.is_personal_msg) AS is_personal_msg
                FROM spheres AS target
                LEFT JOIN client_fallback.spheres AS fallback ON fallback.id = target.id
                WHERE COALESCE(target.enter_or_leave, fallback.enter_or_leave) IS NOT NULL
                  AND COALESCE(target.sphere_detail_id, fallback.sphere_detail_id) IS NOT NULL
                  AND COALESCE(target.sphere_detail_type, fallback.sphere_detail_type) IS NOT NULL
                  AND COALESCE(target.trigger_condition_id, fallback.trigger_condition_id) IS NOT NULL
                  AND COALESCE(target.category_id, fallback.category_id) IS NOT NULL
                  AND COALESCE(target.or_unit_reqs, fallback.or_unit_reqs) IS NOT NULL
                  AND COALESCE(target.is_personal_msg, fallback.is_personal_msg) IS NOT NULL";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Spheres()
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        EnterOrLeave = reader.GetBoolean("enter_or_leave"),
                        SphereDetailId = reader.GetUInt32("sphere_detail_id"),
                        SphereDetailType = reader.GetString("sphere_detail_type"),
                        TriggerConditionId = reader.GetUInt32("trigger_condition_id"),
                        TriggerConditionTime = reader.GetUInt32("trigger_condition_time", 0),
                        TeamMsg = reader.GetString("team_msg"),
                        CategoryId = reader.GetUInt32("category_id"),
                        OrUnitReqs = reader.GetBoolean("or_unit_reqs"),
                        IsPersonalMsg = reader.GetBoolean("is_personal_msg"),
                        //template.MilestoneId = reader.GetUInt32("milestone_id"); // there is no such field in the database for version 3.0.3.0
                        //template.NameTr = reader.GetBoolean("name_tr"); // there is no such field in the database for version 3.0.3.0
                        //template.TeamMsgTr = reader.GetBoolean("team_msg_tr"); // there is no such field in the database for version 3.0.3.0
                    };

                    if (!_spheres.ContainsKey(template.Id))
                        _spheres.Add(template.Id, new List<Spheres>());

                    _spheres[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereQuests()
                    {
                        Id = reader.GetUInt32("id"),
                        QuestId = reader.GetUInt32("quest_id"),
                        QuestTriggerId = (QuestTrigger)reader.GetUInt32("quest_trigger_id")
                    };

                    if (!_sphereQuests.ContainsKey(template.Id))
                        _sphereQuests.Add(template.Id, new List<SphereQuests>());

                    _sphereQuests[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_skills";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereSkills()
                    {
                        Id = reader.GetUInt32("id"),
                        SkillId = reader.GetUInt32("skill_id"),
                        MaxRate = reader.GetUInt32("max_rate"),
                        MinRate = reader.GetUInt32("min_rate")
                    };

                    if (!_sphereSkills.ContainsKey(template.Id))
                        _sphereSkills.Add(template.Id, new List<SphereSkills>());

                    _sphereSkills[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_sounds";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereSounds()
                    {
                        Id = reader.GetUInt32("id"),
                        SoundId = reader.GetUInt32("sound_id"),
                        Broadcast = reader.GetBoolean("broadcast")
                    };

                    if (!_sphereSounds.ContainsKey(template.Id))
                        _sphereSounds.Add(template.Id, new List<SphereSounds>());

                    _sphereSounds[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_doodad_interacts";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereDoodadInteracts()
                    {
                        Id = reader.GetUInt32("id"),
                        SkillId = reader.GetUInt32("skill_id"),
                        DoodadFamilyId = reader.GetUInt32("doodad_family_id")
                    };

                    if (!_sphereDoodadInteracts.ContainsKey(template.Id))
                        _sphereDoodadInteracts.Add(template.Id, new List<SphereDoodadInteracts>());

                    _sphereDoodadInteracts[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_chat_bubbles";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereChatBubbles()
                    {
                        Id = reader.GetUInt32("id"),
                        SphereBubbleId = reader.GetUInt32("sphere_bubble_id"),
                        IsStart = reader.GetBoolean("is_start"),
                        Speech = reader.GetString("speech"),
                        NpcId = reader.GetUInt32("npc_id", 0),
                        NpcSpawnerId = reader.GetUInt32("npc_spawner_id", 0),
                        NextBubble = reader.GetUInt32("next_bubble", 0),
                        SoundId = reader.GetUInt32("sound_id", 0),
                        Angle = reader.GetUInt32("angle", 0),
                        ChatBubbleKindId = (ChatBubbleKind)reader.GetUInt32("chat_bubble_kind_id"),
                        Facial = reader.GetString("facial", ""),
                        CameraId = reader.GetUInt32("camera_id", 0),
                        ChangeSpeakerName = reader.GetString("change_speaker_name", "")
                    };

                    if (!_sphereChatBubbles.ContainsKey(template.Id))
                        _sphereChatBubbles.Add(template.Id, new List<SphereChatBubbles>());

                    _sphereChatBubbles[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_buffs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereBuffs()
                    {
                        Id = reader.GetUInt32("id"),
                        BuffId = reader.GetUInt32("buff_id"),
                        RemoveOnLeaveBuffId = reader.GetUInt32("remove_on_leave_buff_id", 0),
                        AndPet = reader.GetBoolean("and_pet", false)
                    };

                    if (!_sphereBuffs.ContainsKey(template.Id))
                        _sphereBuffs.Add(template.Id, new List<SphereBuffs>());

                    _sphereBuffs[template.Id].Add(template);
                }
            }
        }

        // What a sphere demands of the unit standing in it. The harbour spheres name a faction
        // each, which is what keeps a Nuian ship from mooring at a Haranyan pier.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT owner_id, display_msg, kind_id, value1, value2, value3
                                    FROM unit_reqs
                                    WHERE owner_type = 'Sphere'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var sphereId = reader.GetUInt32("owner_id");
                    var requirement = new PlotUnitRequirement
                    {
                        DisplayMessage = reader.GetBoolean("display_msg", false),
                        KindId = reader.GetInt32("kind_id", 0),
                        Value1 = reader.GetInt32("value1", 0),
                        Value2 = reader.GetInt32("value2", 0),
                        Value3 = reader.GetInt32("value3", 0)
                    };

                    if (!_sphereUnitReqs.ContainsKey(sphereId))
                        _sphereUnitReqs.Add(sphereId, new List<PlotUnitRequirement>());

                    _sphereUnitReqs[sphereId].Add(requirement);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_bubbles";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereBubbles()
                    {
                        Id = reader.GetUInt32("id")
                    };

                    if (!_sphereBubbles.ContainsKey(template.Id))
                        _sphereBubbles.Add(template.Id, new List<SphereBubbles>());

                    _sphereBubbles[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_accept_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereAcceptQuests()
                    {
                        Id = reader.GetUInt32("id")
                    };

                    if (!_sphereAcceptQuests.ContainsKey(template.Id))
                        _sphereAcceptQuests.Add(template.Id, new List<SphereAcceptQuests>());

                    _sphereAcceptQuests[template.Id].Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_accept_quest_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereAcceptQuestQuests()
                    {
                        Id = reader.GetUInt32("id"),
                        SphereAcceptQuestId = reader.GetUInt32("sphere_accept_quest_id"),
                        QuestId = reader.GetUInt32("quest_id")
                    };

                    if (!_sphereAcceptQuestQuests.ContainsKey(template.Id))
                        _sphereAcceptQuestQuests.Add(template.Id, new List<SphereAcceptQuestQuests>());

                    _sphereAcceptQuestQuests[template.Id].Add(template);
                }
            }
        }
    }

    /// <summary>
    /// The sphere row, or null when the client database does not describe it.
    /// </summary>
    public Spheres GetSphere(uint sphereId)
    {
        return _spheres.TryGetValue(sphereId, out var spheres) && spheres.Count > 0 ? spheres[0] : null;
    }

    /// <summary>
    /// The buff a sphere hands out, looked up by the sphere's detail id.
    /// </summary>
    public SphereBuffs GetSphereBuff(uint sphereDetailId)
    {
        return _sphereBuffs.TryGetValue(sphereDetailId, out var buffs) && buffs.Count > 0 ? buffs[0] : null;
    }

    /// <summary>
    /// What a unit has to be for a sphere to act on it. Empty when the sphere asks nothing.
    /// </summary>
    public List<PlotUnitRequirement> GetSphereUnitRequirements(uint sphereId)
    {
        return _sphereUnitReqs.TryGetValue(sphereId, out var requirements)
            ? requirements
            : new List<PlotUnitRequirement>();
    }

    public static List<string> GetQuestSphere(uint questId)
    {
        var sphereIds = new List<string>();

        var questTemplate = QuestManager.Instance.GetTemplate(questId);
        if (questTemplate == null)
            return null;
        foreach (QuestComponentKind step in Enum.GetValues(typeof(QuestComponentKind)))
        {
            var components = questTemplate.GetComponents(step);
            if (components.Length == 0)
                continue;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var component = components[componentIndex];
                var acts = QuestManager.Instance.GetActs(component.Id);

                if (acts.Length > 0)
                {
                    for (var actIndex = 0; actIndex < components.Length; actIndex++)
                    {
                        var act = acts[actIndex];
                        if (act.DetailType == "QuestActObjSphere")
                        {
                            var actTemplate = acts[actIndex].GetTemplate<QuestActObjSphere>();
                            sphereIds.Add(actTemplate.SphereId.ToString());
                        }
                    }
                }
            }
        }

        return sphereIds;
    }

    public void PostLoad()
    {
    }
}
