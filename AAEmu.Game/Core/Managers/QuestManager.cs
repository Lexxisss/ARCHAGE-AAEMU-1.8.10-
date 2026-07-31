using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Quests;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

using QuestNpcAiName = AAEmu.Game.Models.Game.Quests.Static.QuestNpcAiName;

namespace AAEmu.Game.Core.Managers;

public partial class QuestManager : Singleton<QuestManager>, IQuestManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;
    protected Dictionary<uint, QuestTemplate> _templates = new();
    protected Dictionary<byte, QuestSupplies> _supplies = new();
    protected Dictionary<uint, List<IQuestAct>> _acts = new();
    private Dictionary<string, List<IQuestAct>> _actsByType = new();
    private Dictionary<uint, QuestAct> _actsDic = new();
    private readonly Dictionary<string, Dictionary<uint, QuestActDefinition>> _actDefinitions = new(StringComparer.Ordinal);
    protected Dictionary<string, Dictionary<uint, QuestActTemplate>> _actTemplates = new();
    private Dictionary<uint, List<uint>> _groupItems = new();
    private Dictionary<uint, List<uint>> _groupNpcs = new();
    private Dictionary<uint, List<uint>> _groupQuestContexts = new();
    private Dictionary<uint, QuestComponent> _templateComponents = new();
    public Dictionary<uint, Dictionary<uint, QuestTimeoutTask>> QuestTimeoutTask = new();

    public QuestTemplate GetTemplate(uint id)
    {
        return _templates.TryGetValue(id, out var template) ? template : null;
    }

    public QuestComponent[] GetTemplate(uint id, uint componentId)
    {
        _templates.TryGetValue(id, out var template);
        var results = template?.GetComponents(componentId);

        return results;
    }

    public QuestSupplies GetSupplies(byte level)
    {
        return _supplies.TryGetValue(level, out var supply) ? supply : null;
    }

    public IQuestAct[] GetActs(uint id)
    {
        var res = (_acts.TryGetValue(id, out var act) ? act : new List<IQuestAct>()).ToArray();
        //Array.Sort(res); // На некоторых данных вызывает System.InvalidOperationException: Failed to compare two elements in the array. System.InvalidOperationException: Failed to compare two elements in the array.
        //
        return res;
    }

    public QuestActTemplate GetActTemplate(uint id, string type)
    {
        if (!_actTemplates.ContainsKey(type))
            return null;
        return _actTemplates[type].ContainsKey(id) ? _actTemplates[type][id] : null;
    }

    public T GetActTemplate<T>(uint id, string type) where T : QuestActTemplate
    {
        if (!_actTemplates.ContainsKey(type))
            return default;
        return _actTemplates[type].ContainsKey(id) ? (T)_actTemplates[type][id] : default;
    }

    public QuestActDefinition GetActDefinition(uint id, string type)
    {
        return _actDefinitions.TryGetValue(type, out var definitions) && definitions.TryGetValue(id, out var definition)
            ? definition
            : null;
    }

    public IReadOnlyCollection<string> GetLoadedActTypes() => _actDefinitions.Keys;

    public List<uint> GetGroupItems(uint groupId)
    {
        return _groupItems.TryGetValue(groupId, out var item) ? (item) : new List<uint>();
    }

    public bool CheckGroupItem(uint groupId, uint itemId)
    {
        return _groupItems.ContainsKey(groupId) && (_groupItems[groupId].Contains(itemId));
    }

    public bool CheckGroupNpc(uint groupId, uint npcId)
    {
        return _groupNpcs.ContainsKey(groupId) && (_groupNpcs[groupId].Contains(npcId));
    }

    public void QuestCompleteTask(ICharacter owner, uint questId)
    {
        owner.Quests.Complete(questId, 0);
    }

    public void CancelQuest(ICharacter owner, uint questId)
    {
        owner.Quests.Drop(questId, true);
        owner.SendMessage($"[Quest] {owner.Name}, quest {questId} time is over, you didn't make it. Try again.");
        Logger.Warn($"[Quest] {owner.Name}, quest {questId} time is over, you didn't make it. Try again.");
    }

    private void UpdateQuestComponentActs()
    {
        foreach (var questTemplate in _templates.Values)
        {
            foreach (var questComponent in questTemplate.Components)
            {
                if (_acts.TryGetValue(questComponent.Key, out var questActs))
                {
                    questComponent.Value.ActTemplates.AddRange(questActs.Select(a => a.Template).Where(x => x != null));
                    questComponent.Value.Acts.AddRange(questActs);
                }
            }
        }
    }

    public void Load()
    {
        if (_loaded)
            return;

        Logger.Info("Loading target 10.8 quest system...");
        using (var connection = QuestSQLite.CreateConnection())
        {
            LoadQuestContexts(connection);
            LoadQuestComponents(connection);
            LoadQuestSupplies(connection);
            LoadQuestActs(connection);
            LoadQuestActDefinitions(connection);
            LoadQuestItemGroups(connection);
            LoadQuestMonsterNpcs(connection);
            LoadQuestContextGroups(connection);
            UpdateQuestComponentActs();
        }

        ValidateQuestData();
        _loaded = true;

        var dailyCron = "0 0 0 ? * * *";
        TaskManager.Instance.CronSchedule(new QuestDailyResetTask(), dailyCron);
    }

    /// <summary>
    /// Function needed for a hack to make older quest starter items work
    /// </summary>
    /// <param name="itemItemTemplateId"></param>
    /// <returns></returns>
    public uint GetQuestIdFromStarterItem(uint itemItemTemplateId) =>
        GetQuestIdFromStarterItemNew(itemItemTemplateId);

    /// <summary>Resolves item-started quests from the target quest database.</summary>
    public uint GetQuestIdFromStarterItemNew(uint itemItemTemplateId)
    {
        if (!_actsByType.TryGetValue("QuestActConAcceptItem", out var acts))
            return 0;

        foreach (var questAct in acts.Cast<QuestAct>().OrderBy(x => x.Id))
        {
            var definition = questAct.Definition;
            if (definition == null || definition.GetUInt32("item_id") != itemItemTemplateId)
                continue;
            if (questAct.QuestComponent?.KindId != QuestComponentKind.Start)
                continue;
            return questAct.QuestComponent.QuestTemplate.Id;
        }

        return 0;
    }


    private void LoadQuestContextGroups(SqliteConnection connection)
    {
        if (!QuestSQLite.TableExists(connection, "quest_context_group_members"))
            return;
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_context_group_members")}";
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var groupId = reader.GetUInt32("quest_context_group_id");
            var questId = reader.GetUInt32("context_id");
            if (!_groupQuestContexts.TryGetValue(groupId, out var quests))
            {
                quests = new List<uint>();
                _groupQuestContexts[groupId] = quests;
            }
            quests.Add(questId);
        }
    }

    public bool CheckGroupQuest(uint groupId, uint questId) =>
        _groupQuestContexts.TryGetValue(groupId, out var quests) && quests.Contains(questId);

    public bool IsAnyQuestInGroupCompleted(ICharacter owner, uint groupId) =>
        owner?.Quests != null && _groupQuestContexts.TryGetValue(groupId, out var quests) && quests.Any(owner.Quests.HasQuestCompleted);

    private void LoadQuestMonsterNpcs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_monster_npcs")}";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var groupId = reader.GetUInt32("quest_monster_group_id");
            var npcId = reader.GetUInt32("npc_id");
            List<uint> npcs;
            if (!_groupNpcs.ContainsKey(groupId))
            {
                npcs = new List<uint>();
                _groupNpcs.Add(groupId, npcs);
            }
            else
                npcs = _groupNpcs[groupId];
            npcs.Add(npcId);
        }
    }

    private void LoadQuestItemGroups(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_item_group_items")}";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var groupId = reader.GetUInt32("quest_item_group_id");
            var itemId = reader.GetUInt32("item_id");
            List<uint> items;
            if (!_groupItems.ContainsKey(groupId))
            {
                items = new List<uint>();
                _groupItems.Add(groupId, items);
            }
            else
                items = _groupItems[groupId];

            items.Add(itemId);
        }
    }

    private void LoadQuestActs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_acts")} ORDER BY quest_component_id ASC, id ASC";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var componentId = reader.GetUInt32("quest_component_id");
            _templateComponents.TryGetValue(componentId, out var questComponent);
            var template = new QuestAct(questComponent);

            template.Id = reader.GetUInt32("id");
            template.ComponentId = componentId;
            template.DetailId = reader.GetUInt32("act_detail_id");
            template.DetailType = reader.GetString("act_detail_type");
            List<IQuestAct> list;
            if (!_actsByType.ContainsKey(template.DetailType))
            {
                list = new List<IQuestAct> { template };
                _actsByType.Add(template.DetailType, list);
            }
            else
                _actsByType[template.DetailType].Add(template);
            if (_acts.TryGetValue(template.ComponentId, out var act))
                list = act;
            else
            {
                list = new List<IQuestAct>();
                _acts.Add(template.ComponentId, list);
            }

            list.Add(template);
            _actsDic.Add(template.Id, template);
        }
    }

    private void LoadQuestActDefinitions(SqliteConnection connection)
    {
        foreach (var detailType in _actsByType.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var tableName = QuestActDefinition.GetTableName(detailType);
            if (!QuestSQLite.TableExists(connection, tableName))
                throw new InvalidOperationException($"Missing quest detail table for {detailType}: {tableName}");

            var definitions = new Dictionary<uint, QuestActDefinition>();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, tableName)} ORDER BY id ASC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    var name = reader.GetName(ordinal);
                    values[name] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                }

                var id = values.TryGetValue("id", out var rawId) && rawId != null
                    ? checked((uint)Convert.ToInt64(rawId))
                    : 0;
                if (id == 0)
                    continue;

                definitions[id] = new QuestActDefinition(id, detailType, tableName, values);
            }

            _actDefinitions[detailType] = definitions;
            foreach (var act in _actsByType[detailType].Cast<QuestAct>())
            {
                if (!definitions.TryGetValue(act.DetailId, out var definition))
                    throw new InvalidOperationException($"Missing {detailType} detail id {act.DetailId} for quest act {act.Id}");
                act.Definition = definition;
            }
        }
    }

    private void ValidateQuestData()
    {
        var componentCount = _templates.Values.Sum(x => x.Components.Count);
        var actCount = _acts.Values.Sum(x => x.Count);
        var unbound = _acts.Values.SelectMany(x => x).Cast<QuestAct>().Count(x => x.Definition == null);
        if (unbound != 0)
            throw new InvalidOperationException($"Quest loader left {unbound} acts without definitions");

        Logger.Info(
            "Target quest data loaded: contexts={0}, components={1}, acts={2}, detailTypes={3}",
            _templates.Count,
            componentCount,
            actCount,
            _actDefinitions.Count);
    }

    private void LoadQuestSupplies(SqliteConnection connection)
    {
        Logger.Info("Loaded {0} quests", _templates.Count);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_supplies")}";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new QuestSupplies();
            template.Id = reader.GetUInt32("id");
            template.Level = reader.GetByte("level");
            template.Exp = reader.GetInt32("exp");
            template.Copper = reader.GetInt32("copper");
            _supplies.Add(template.Level, template);
        }
    }

    private void LoadQuestComponents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_components")} ORDER BY quest_context_id ASC, component_kind_id ASC, id ASC";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var questId = reader.GetUInt32("quest_context_id");
            if (!_templates.ContainsKey(questId))
                continue;

            var template = new QuestComponent(_templates[questId]);
            template.Id = reader.GetUInt32("id");
            template.KindId = (QuestComponentKind)reader.GetByte("component_kind_id");
            template.HideQuestMarker = reader.GetBoolean("hide_quest_marker", false);
            template.NextComponent = reader.GetUInt32("next_component", 0);
            template.NpcAiId = (QuestNpcAiName)reader.GetUInt32("npc_ai_id", 0);
            template.NpcId = reader.GetUInt32("npc_id", 0);
            template.SkillId = reader.GetUInt32("skill_id", 0);
            template.SkillSelf = reader.GetBoolean("skill_self", true);
            template.AiPathName = reader.GetString("ai_path_name", string.Empty);
            template.AiPathTypeId = (PathType)reader.GetUInt32("ai_path_type_id");
            template.NpcSpawnerId = reader.GetUInt32("npc_spawner_id", 0);
            template.PlayCinemaBeforeBubble = reader.GetBoolean("play_cinema_before_bubble", true);
            template.AiCommandSetId = reader.GetUInt32("ai_command_set_id", 0);
            template.OrUnitReqs = reader.GetBoolean("or_unit_reqs", true);
            template.CinemaId = reader.GetUInt32("cinema_id", 0);
            template.BuffId = reader.GetUInt32("buff_id", 0);
            _templates[questId].Components.Add(template.Id, template);
            _templateComponents.TryAdd(template.Id, template);
        }
    }

    private void LoadQuestContexts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuestSQLite.ResolveTable(connection, "quest_contexts")} ORDER BY id ASC";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new QuestTemplate();
            template.Id = reader.GetUInt32("id");
            template.Repeatable = reader.GetBoolean("repeatable", true);
            template.Level = reader.GetByte("level", 0);
            template.MinLevel = reader.GetByte("min_level", 0);
            template.MaxLevel = reader.GetByte("max_level", 0);
            template.RaceMask = reader.GetByte("race", byte.MaxValue);
            template.Name = reader.GetString("name", string.Empty);
            template.Selective = reader.GetBoolean("selective", true);
            template.Successive = reader.GetBoolean("successive", true);
            template.RestartOnFail = reader.GetBoolean("restart_on_fail", true);
            template.ChapterIdx = reader.GetUInt32("chapter_idx", 0);
            template.QuestIdx = reader.GetUInt32("quest_idx", 0);
            //template.MilestoneId = reader.GetUInt32("milestone_id", 0); // there is no such field in the database for version 3.0.3.0
            template.LetItDone = reader.GetBoolean("let_it_done", true);
            template.DetailId = (QuestDetail)reader.GetUInt32("detail_id");
            template.ZoneId = reader.GetUInt32("zone_id");
            template.Degree = reader.GetInt32("degree", 0);
            template.UseQuestCamera = reader.GetBoolean("use_quest_camera", true);
            template.Score = reader.GetInt32("score", 0);
            template.UseAcceptMessage = reader.GetBoolean("use_accept_message", true);
            template.UseCompleteMessage = reader.GetBoolean("use_complete_message", true);
            template.GradeId = reader.GetUInt32("grade_id", 0);
            _templates.Add(template.Id, template);
        }
    }

    private void AddActTemplate(QuestActTemplate template)
    {
        var detailType = template.GetType().Name;
        _actTemplates[detailType].Add(template.Id, template);
        foreach (var questAct in _actsDic.Values.Where(qa =>
                qa.DetailId == template.Id &&
                qa.DetailType == detailType))
        {
            questAct.Template = template;
        }
    }

    private void LoadQuestActTemplates(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_check_complete_components";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActCheckCompleteComponent();
                    template.Id = reader.GetUInt32("id");
                    template.CompleteComponent = reader.GetUInt32("complete_component");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_check_distances";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActCheckDistance();
                    template.Id = reader.GetUInt32("id");
                    template.WithIn = reader.GetBoolean("within", true);
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.Distance = reader.GetInt32("distance");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_check_guards";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActCheckGuard();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_check_spheres";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActCheckSphere();
                    template.Id = reader.GetUInt32("id");
                    template.SphereId = reader.GetUInt32("sphere_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_check_timers";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActCheckTimer();
                    template.Id = reader.GetUInt32("id");
                    template.LimitTime = reader.GetInt32("limit_time");
                    template.ForceChangeComponent = reader.GetBoolean("force_change_component", true);
                    template.NextComponent = reader.GetUInt32("next_component");
                    template.PlaySkill = reader.GetBoolean("play_skill", true);
                    template.SkillId = reader.GetUInt32("skill_id", 0);
                    template.CheckBuff = reader.GetBoolean("check_buf", true);
                    template.BuffId = reader.GetUInt32("buff_id", 0);
                    template.SustainBuff = reader.GetBoolean("sustain_buf", true);
                    template.TimerNpcId = reader.GetUInt32("timer_npc_id", 0);
                    template.IsSkillPlayer = reader.GetBoolean("is_skill_player", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_buffs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptBuff();
                    template.Id = reader.GetUInt32("id");
                    template.BuffId = reader.GetUInt32("buff_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_components";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptComponent();
                    template.Id = reader.GetUInt32("id");
                    template.QuestContextId = reader.GetUInt32("quest_context_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_doodads";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptDoodad();
                    template.Id = reader.GetUInt32("id");
                    template.DoodadId = reader.GetUInt32("doodad_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_item_equips";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptItemEquip();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_item_gains";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptItemGain();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_items";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptItem();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_level_ups";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptLevelUp();
                    template.Id = reader.GetUInt32("id");
                    template.Level = reader.GetByte("level");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_npc_emotions";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptNpcEmotion();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.Emotion = reader.GetString("emotion");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_npc_kills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptNpcKill();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_npcs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptNpc();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptSkill();
                    template.Id = reader.GetUInt32("id");
                    template.SkillId = reader.GetUInt32("skill_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_accept_spheres";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAcceptSphere();
                    template.Id = reader.GetUInt32("id");
                    template.SphereId = reader.GetUInt32("sphere_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_auto_completes";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConAutoComplete();
                    template.Id = reader.GetUInt32("id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_fails";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConFail();
                    template.Id = reader.GetUInt32("id");
                    template.ForceChangeComponent = reader.GetBoolean("force_change_component", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_report_doodads";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConReportDoodad();
                    template.Id = reader.GetUInt32("id");
                    template.DoodadId = reader.GetUInt32("doodad_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_report_journals";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConReportJournal();
                    template.Id = reader.GetUInt32("id");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_con_report_npcs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActConReportNpc();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_etc_item_obtains";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActEtcItemObtain();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.HighlightDooadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_ability_levels";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjAbilityLevel();
                    template.Id = reader.GetUInt32("id");
                    template.AbilityId = reader.GetByte("ability_id");
                    template.Level = reader.GetByte("level");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_aggros";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjAggro();
                    template.Id = reader.GetUInt32("id");
                    template.Range = reader.GetInt32("range");
                    template.Rank1 = reader.GetInt32("rank1", 0);
                    template.Rank1Ratio = reader.GetInt32("rank1_ratio", 0);
                    template.Rank1Item = reader.GetBoolean("rank1_item", true);
                    template.Rank2 = reader.GetInt32("rank2", 0);
                    template.Rank2Ratio = reader.GetInt32("rank2_ratio", 0);
                    template.Rank2Item = reader.GetBoolean("rank2_item", true);
                    template.Rank3 = reader.GetInt32("rank3", 0);
                    template.Rank3Ratio = reader.GetInt32("rank3_ratio", 0);
                    template.Rank3Item = reader.GetBoolean("rank3_item", true);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_complete_quests";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjCompleteQuest();
                    template.Id = reader.GetUInt32("id");
                    template.QuestId = reader.GetUInt32("quest_id");
                    template.AcceptWith = reader.GetBoolean("accept_with", true);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_conditions";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjCondition();
                    template.Id = reader.GetUInt32("id");
                    template.ConditionId = reader.GetUInt32("condition_id");
                    template.QuestContextId = reader.GetUInt32("quest_context_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_crafts";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjCraft();
                    template.Id = reader.GetUInt32("id");
                    template.CraftId = reader.GetUInt32("craft_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_distances";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjDistance();
                    template.Id = reader.GetUInt32("id");
                    template.WithIn = reader.GetBoolean("within", true);
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.Distance = reader.GetInt32("distance");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_doodad_phase_checks";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjDoodadPhaseCheck();
                    template.Id = reader.GetUInt32("id");
                    template.DoodadId = reader.GetUInt32("doodad_id");
                    template.Phase1 = reader.GetUInt32("phase1", 0);
                    template.Phase2 = reader.GetUInt32("phase2", 0);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_effect_fires";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjEffectFire();
                    template.Id = reader.GetUInt32("id");
                    template.EffectId = reader.GetUInt32("effect_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_express_fires";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjExpressFire();
                    template.Id = reader.GetUInt32("id");
                    template.ExpressKeyId = reader.GetUInt32("express_key_id");
                    template.NpcGroupId = reader.GetUInt32("npc_group_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_interactions";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjInteraction();
                    template.Id = reader.GetUInt32("id");
                    template.WorldInteractionId = (WorldInteractionType)reader.GetInt32("wi_id");
                    template.Count = reader.GetInt32("count");
                    template.DoodadId = reader.GetUInt32("doodad_id", 0);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.TeamShare = reader.GetBoolean("team_share", true);
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.Phase = reader.GetUInt32("phase", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_item_gathers";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjItemGather();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_item_group_gathers";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjItemGroupGather();
                    template.Id = reader.GetUInt32("id");
                    template.ItemGroupId = reader.GetUInt32("item_group_id");
                    template.Count = reader.GetInt32("count");
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_item_group_uses";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjItemGroupUse();
                    template.Id = reader.GetUInt32("id");
                    template.ItemGroupId = reader.GetUInt32("item_group_id");
                    template.Count = reader.GetInt32("count");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_item_uses";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjItemUse();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_levels";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjLevel();
                    template.Id = reader.GetUInt32("id");
                    template.Level = reader.GetByte("level");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_mate_levels";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjMateLevel();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Level = reader.GetByte("level");
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_monster_group_hunts";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjMonsterGroupHunt();
                    template.Id = reader.GetUInt32("id");
                    template.QuestMonsterGroupId = reader.GetUInt32("quest_monster_group_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_monster_hunts";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjMonsterHunt();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_send_mails";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjSendMail();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId1 = reader.GetUInt32("item1_id", 0);
                    template.Count1 = reader.GetInt32("count1");
                    template.ItemId2 = reader.GetUInt32("item2_id", 0);
                    template.Count2 = reader.GetInt32("count2");
                    template.ItemId3 = reader.GetUInt32("item3_id", 0);
                    template.Count3 = reader.GetInt32("count3");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_spheres";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjSphere();
                    template.Id = reader.GetUInt32("id");
                    template.SphereId = reader.GetUInt32("sphere_id");
                    template.NpcId = reader.GetUInt32("npc_id", 0);
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                    template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_talk_npc_groups";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjTalkNpcGroup();
                    template.Id = reader.GetUInt32("id");
                    template.NpcGroupId = reader.GetUInt32("npc_group_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_talks";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjTalk();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.TeamShare = reader.GetBoolean("team_share", true);
                    template.ItemId = reader.GetUInt32("item_id", 0);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_zone_kills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjZoneKill();
                    template.Id = reader.GetUInt32("id");
                    template.CountPlayerKill = reader.GetInt32("count_pk");
                    template.CountNpc = reader.GetInt32("count_npc");
                    template.ZoneId = reader.GetUInt32("zone_id", 0);
                    template.TeamShare = reader.GetBoolean("team_share", true);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.LvlMin = reader.GetInt32("lv_min");
                    template.LvlMax = reader.GetInt32("lv_max");
                    template.IsParty = reader.GetBoolean("is_party", true);
                    template.LvlMinNpc = reader.GetInt32("lv_min_npc");
                    template.LvlMaxNpc = reader.GetInt32("lv_max_npc");
                    template.PcFactionId = reader.GetUInt32("pc_faction_id", 0);
                    template.PcFactionExclusive = reader.GetBoolean("pc_faction_exclusive", true);
                    template.NpcFactionId = reader.GetUInt32("npc_faction_id", 0);
                    template.NpcFactionExclusive = reader.GetBoolean("npc_faction_exclusive", true);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_zone_monster_hunts";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjZoneMonsterHunt();
                    template.Id = reader.GetUInt32("id");
                    template.ZoneId = reader.GetUInt32("zone_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_zone_npc_talks";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjZoneNpcTalk();
                    template.Id = reader.GetUInt32("id");
                    template.NpcId = reader.GetUInt32("npc_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_obj_zone_quest_completes";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActObjZoneQuestComplete();
                    template.Id = reader.GetUInt32("id");
                    template.ZoneId = reader.GetUInt32("zone_id");
                    template.Count = reader.GetInt32("count");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_aa_points";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyAaPoint();
                    template.Id = reader.GetUInt32("id");
                    template.Point = reader.GetInt32("point");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_appellations";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyAppellation();
                    template.Id = reader.GetUInt32("id");
                    template.AppellationId = reader.GetUInt32("appellation_id");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_coppers";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyCopper();
                    template.Id = reader.GetUInt32("id");
                    template.Amount = reader.GetInt32("amount");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_crime_points";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyCrimePoint();
                    template.Id = reader.GetUInt32("id");
                    template.Point = reader.GetInt32("point");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_exps";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyExp();
                    template.Id = reader.GetUInt32("id");
                    template.Exp = reader.GetInt32("exp");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_honor_points";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyHonorPoint();
                    template.Id = reader.GetUInt32("id");
                    template.Point = reader.GetInt32("point");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_interactions";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyInteraction();
                    template.Id = reader.GetUInt32("id");
                    template.WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_items";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyItem();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.GradeId = reader.GetByte("grade_id");
                    template.ShowActionBar = reader.GetBoolean("show_action_bar", true);
                    template.Cleanup = reader.GetBoolean("cleanup", true);
                    template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                    template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_jury_points";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyJuryPoint();
                    template.Id = reader.GetUInt32("id");
                    template.Point = reader.GetInt32("point");
                    AddActTemplate(template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_living_points";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyLivingPoint();
                    template.Id = reader.GetUInt32("id");
                    template.Point = reader.GetInt32("point");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_lps";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyLp();
                    template.Id = reader.GetUInt32("id");
                    template.LaborPower = reader.GetInt32("lp");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_remove_items";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplyRemoveItem();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_selective_items";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplySelectiveItem();
                    template.Id = reader.GetUInt32("id");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.GradeId = reader.GetByte("grade_id");
                    AddActTemplate(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quest_act_supply_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new QuestActSupplySkill();
                    template.Id = reader.GetUInt32("id");
                    template.SkillId = reader.GetUInt32("skill_id");
                    AddActTemplate(template);
                }
            }
        }
    }

    public void DoReportEvents(ICharacter owner, uint questContextId, uint npcObjId, uint doodadObjId, int selected)
    {
        if (owner?.Quests == null)
            return;
        if (npcObjId != 0)
        {
            owner.Quests.OnReportToNpc(npcObjId, questContextId, selected);
            return;
        }
        if (doodadObjId != 0)
        {
            owner.Quests.OnReportToDoodad(doodadObjId, questContextId, selected);
            return;
        }
        owner.Quests.CompleteTarget(questContextId, 0, 0, selected, false);
    }

    public void DoConsumedEvents(ICharacter owner, uint templateId, int count)
    {
        owner?.Quests?.OnItemUse(templateId, count);
    }

    public void DoAcquiredEvents(ICharacter owner, uint templateId, int count)
    {
        owner?.Quests?.OnItemGather(templateId, count);
    }

    public void DoInteractionEvents(ICharacter owner, uint templateId)
    {
        owner?.Quests?.DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.DoodadInteraction,
            TemplateId = templateId,
            Count = 1
        });
    }

    public void DoTalkMadeEvents(ICharacter owner, uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        owner?.Quests?.OnTalkMade(npcObjId, questContextId, questComponentId, questActId);
    }

    public void DoOnMonsterHuntEvents(ICharacter owner, Npc npc)
    {
        owner?.Quests?.OnKill(npc);
    }

    public void DoOnAggroEvents(ICharacter owner, Npc npc)
    {
        owner?.Quests?.OnAggro(npc);
    }

    public void DoOnExpressFireEvents(ICharacter owner, uint emotionId, uint characterObjId, uint npcObjId)
    {
        owner?.Quests?.OnExpressFire(emotionId, characterObjId, npcObjId);
    }

    public void DoOnLevelUpEvents(ICharacter owner)
    {
        owner?.Quests?.OnLevelUp();
        if (owner is Character character)
        {
            foreach (var ability in character.Abilities.Values)
            {
                var level = ExperienceManager.Instance.GetLevelFromExp(ability.Exp);
                owner.Quests.OnAbilityLevelChanged((uint)ability.Id, level);
            }
        }
    }

    public void DoOnCraftEvents(ICharacter owner, uint craftId)
    {
        owner?.Quests?.OnCraft(craftId);
    }

    public void DoOnEnterSphereEvents(ICharacter owner, SphereQuest sphereQuest)
    {
        owner?.Quests?.OnEnterSphere(sphereQuest);
    }

    public void DispatchRuntimeEvent(ICharacter owner, QuestRuntimeEvent runtimeEvent)
    {
        owner?.Quests?.DispatchRuntimeEvent(runtimeEvent);
    }

}
