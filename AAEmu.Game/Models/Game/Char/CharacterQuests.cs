using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterQuests
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>How many quests one stored completion record covers, a bit each.</summary>
    private const int CompletedQuestsPerBlock = 64;

    private readonly List<uint> _removed;
    private uint _activeCinemaId;

    private Character Owner { get; set; }
    public Dictionary<uint, Quest> ActiveQuests { get; }
    private Dictionary<ushort, CompletedQuest> CompletedQuests { get; }

    public CharacterQuests(Character owner)
    {
        Owner = owner;
        ActiveQuests = new Dictionary<uint, Quest>();
        CompletedQuests = new Dictionary<ushort, CompletedQuest>();
        _removed = new List<uint>();
    }

    public bool HasQuest(uint questId)
    {
        return ActiveQuests.ContainsKey(questId);
    }

    public bool HasQuestCompleted(uint questId)
    {
        var questBlockId = (ushort)(questId / 64);
        var questBlockIndex = (int)(questId % 64);
        return CompletedQuests.TryGetValue(questBlockId, out var questBlock) && questBlock.Body.Get(questBlockIndex);
    }

    public bool Add(uint questId, bool forcibly = false, uint npcObjId = 0, uint doodadObjId = 0, uint sphereId = 0, uint itemId = 0)
    {
        if (ActiveQuests.ContainsKey(questId))
        {
            if (!forcibly)
            {
                Logger.Info("Duplicate quest {0}, not added!", questId);
                return false;
            }
            Drop(questId, true, true);
        }

        var template = QuestManager.Instance.GetTemplate(questId);
        if (template == null)
        {
            Logger.Warn("Quest template {0} not found", questId);
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 1));
            return false;
        }

        if (HasQuestCompleted(questId) && !forcibly && !template.Repeatable)
        {
            Owner.SendErrorMessage(ErrorMessageType.QuestDailyLimit);
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 2));
            return false;
        }
        if (forcibly)
            ResetCompletedQuest(questId);

        var sourceType = QuestAcceptorType.Unknown;
        uint sourceObjectId = 0;
        uint sourceTemplateId = 0;
        if (npcObjId != 0)
        {
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc != null)
            {
                sourceType = QuestAcceptorType.Npc;
                sourceObjectId = npc.ObjId;
                sourceTemplateId = npc.TemplateId;
                Owner.CurrentTarget = npc;
            }
        }
        else if (doodadObjId != 0)
        {
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad != null)
            {
                sourceType = QuestAcceptorType.Doodad;
                sourceObjectId = doodad.ObjId;
                sourceTemplateId = doodad.TemplateId;
            }
        }
        else if (sphereId != 0)
        {
            sourceType = QuestAcceptorType.Sphere;
            sourceTemplateId = sphereId;
        }
        else if (itemId != 0)
        {
            sourceType = QuestAcceptorType.Item;
            sourceTemplateId = itemId;
        }

        var quest = new Quest(template)
        {
            Id = QuestIdManager.Instance.GetNextId(),
            Owner = Owner,
            Status = QuestStatus.Invalid,
            Condition = QuestConditionObj.Progress
        };

        ActiveQuests.Add(questId, quest);
        if (!quest.StartRuntime(forcibly, sourceType, sourceObjectId, sourceTemplateId))
        {
            ActiveQuests.Remove(questId);
            QuestIdManager.Instance.ReleaseId((uint)quest.Id);
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 3));
            return false;
        }

        Owner.SendPacket(new SCQuestContextStartedPacket(quest, quest.CurrentComponentId));

        // Starting the quest is what hands over whatever it sends the player off with, and that
        // happens above, before the client has heard of the quest at all: the goods arrive, the
        // client counts them against nothing, and the objective that asks for them sits at zero
        // while they are plainly in the bag.
        //
        // The message that carries progress is a merge onto a quest the client already knows, and
        // it is dropped without a word when there is none - so it could not be sent any earlier
        // than this. Sent here it restates the objectives against what the player is actually
        // holding, which is what the count should have been all along.
        if (quest.HasStartingSupply())
            quest.SendRuntimeUpdate();

        RefreshQuestNotifier();
        Logger.Info("Quest {0} started for {1}, runtime={2}, component={3}, status={4}, source={5}:{6}",
            questId, Owner.Name, quest.Id, quest.CurrentComponentId, quest.Status, sourceType, sourceTemplateId);

        if (quest.ShouldAutoComplete())
            CompleteTarget(questId, 0, 0, 0, false);
        return true;
    }

    /// <summary>
    /// Complete - завершаем квест, получаем награду
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="selected"></param>
    /// <param name="supply"></param>
    public void Complete(uint questId, int selected, bool supply = true)
    {
        CompleteTarget(questId, 0, 0, selected, false, supply);
    }

    public bool CompleteTarget(uint questId, uint reportField0, uint reportField1, int selected,
        bool allowEarly, bool includeLevelSupply = true)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest))
        {
            Logger.Warn("CompleteTarget: quest {0} is not active", questId);
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 1));
            return false;
        }

        if (allowEarly && quest.Status == QuestStatus.Progress && !quest.TryEnableLetItDone())
        {
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 4));
            return false;
        }
        if (quest.Status != QuestStatus.Ready)
        {
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, 4));
            return false;
        }
        if (!quest.ValidateReport(reportField0, reportField1))
        {
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.InvalidNpcOrQuest));
            return false;
        }
        if (!quest.ValidateRewardSelection(selected))
        {
            Logger.Warn("Quest {0}: invalid client reward selection {1}", questId, selected);
            Owner.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.InvalidRewardSelection));
            return false;
        }
        if (!quest.TryEnterCompletion())
            return false;

        var completed = false;
        try
        {
            // The quest is handed in first and paid for afterwards. It used to be the other way
            // round, and the reward could be handed over and then something further down refuse -
            // leaving the player holding the goods with the quest still on their list, ready to be
            // turned in again for the same reward, and again after that.
            //
            // The order costs us the other failure: if paying out goes wrong now, the quest is
            // already reported finished and the player is owed something they did not get. That is
            // the one worth having - it is visible, it is one player, and it does not multiply.
            var componentId = quest.PeekCompletionComponentId();

            var blockId = (ushort)(questId / CompletedQuestsPerBlock);
            if (!CompletedQuests.TryGetValue(blockId, out var completedBlock))
            {
                completedBlock = new CompletedQuest(blockId);
                CompletedQuests[blockId] = completedBlock;
            }
            completedBlock.Body.Set((int)(questId % CompletedQuestsPerBlock), true);

            quest.MarkRuntimeCompleted(componentId);
            ActiveQuests.Remove(questId);
            _removed.Add(questId);
            CancelTimeoutTask(quest);
            QuestIdManager.Instance.ReleaseId((uint)quest.Id);

            Owner.SendPacket(new SCQuestContextCompletedPacket(questId, componentId));
            completed = true;

            if (!quest.ApplyRuntimeRewards(selected, includeLevelSupply, out _))
            {
                Logger.Error(
                    "Quest {0} was turned in by {1} but its reward could not be handed over; the player is owed it",
                    questId, Owner.Name);
                Owner.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.CantSupplyRewards));
            }

            RefreshQuestNotifier();
            DispatchRuntimeEvent(new QuestRuntimeEvent
            {
                Type = QuestRuntimeEventType.QuestCompleted,
                TemplateId = questId,
                Count = 1
            });
            Logger.Info("Quest {0} completed for {1}, component={2}, reward={3}", questId, Owner.Name, componentId, selected);
            return true;
        }
        finally
        {
            quest.LeaveCompletion(completed);
        }
    }

    public void Drop(uint questId, bool update, bool forcibly = false)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest))
        {
            if (forcibly)
                ResetCompletedQuest(questId);
            return;
        }

        quest.CleanupQuestItems(true);
        quest.Status = QuestStatus.Dropped;
        quest.Step = QuestComponentKind.Drop;
        if (update)
            quest.SendRuntimeUpdate();

        ActiveQuests.Remove(questId);
        _removed.Add(questId);
        if (forcibly)
            ResetCompletedQuest(questId);
        CancelTimeoutTask(quest);
        QuestIdManager.Instance.ReleaseId((uint)quest.Id);
        RefreshQuestNotifier();
        Logger.Info("Quest {0} dropped for {1}", questId, Owner.Name);
    }

    private static void CancelTimeoutTask(Quest quest)
    {
        if (!QuestManager.Instance.QuestTimeoutTask.TryGetValue(quest.Owner.Id, out var ownerTasks) ||
            !ownerTasks.TryGetValue(quest.TemplateId, out var task))
            return;
        _ = task.CancelAsync();
        ownerTasks.Remove(quest.TemplateId);
    }

    public void DispatchRuntimeEvent(QuestRuntimeEvent runtimeEvent)
    {
        foreach (var quest in ActiveQuests.Values.ToArray())
        {
            if (quest.IsCompletionLocked)
                continue;
            var changed = quest.DispatchRuntimeEvent(runtimeEvent);
            if (changed && quest.ShouldAutoComplete() && ActiveQuests.ContainsKey(quest.TemplateId))
                CompleteTarget(quest.TemplateId, 0, 0, 0, false);
        }
    }

    public bool SetStep(uint questContextId, uint step)
    {
        if (step > 8)
            return false;

        if (!ActiveQuests.ContainsKey(questContextId))
            return false;

        var quest = ActiveQuests[questContextId];
        quest.Step = (QuestComponentKind)step;
        return true;
    }

    public void OnReportToNpc(uint objId, uint questId, int selected)
    {
        var npc = WorldManager.Instance.GetNpc(objId);
        if (npc == null || npc.GetDistanceTo(Owner) > 12.0f || !ActiveQuests.TryGetValue(questId, out var quest))
            return;
        quest.SetReportSource(npc.ObjId, npc.TemplateId);
        CompleteTarget(questId, npc.ObjId, 0, selected, false);
    }

    public void OnReportToDoodad(uint objId, uint questId, int selected)
    {
        var doodad = WorldManager.Instance.GetDoodad(objId);
        if (doodad == null || MathUtil.CalculateDistance(doodad, Owner) > 12.0f || !ActiveQuests.TryGetValue(questId, out var quest))
            return;
        quest.SetReportSource(doodad.ObjId, doodad.TemplateId);
        CompleteTarget(questId, 0, doodad.ObjId, selected, false);
    }

    public void OnTalkMade(uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || npc.GetDistanceTo(Owner) > 12.0f)
            return;
        if (ActiveQuests.TryGetValue(questContextId, out var quest))
            quest.SetReportSource(npc.ObjId, npc.TemplateId);
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.TalkNpc,
            SourceObjectId = Owner.ObjId,
            TargetObjectId = npc.ObjId,
            TemplateId = npc.TemplateId,
            SecondaryId = questComponentId,
            Value = (int)questActId
        });
    }

    public void OnKill(Npc npc)
    {
        if (npc == null)
            return;
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.NpcKill,
            SourceObjectId = Owner.ObjId,
            TargetObjectId = npc.ObjId,
            TemplateId = npc.TemplateId,
            ZoneId = zoneGroupId,
            Level = npc.Level,
            Rank = (int)npc.Template.NpcGradeId,
            Count = 1
        });
    }

    public void OnAggro(Npc npc)
    {
        if (npc == null)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.Aggro,
            TargetObjectId = npc.ObjId,
            TemplateId = npc.TemplateId,
            Level = npc.Level
        });
    }

    public void OnItemGather(Item item, int count)
    {
        if (item == null)
            return;
        OnItemGather(item.TemplateId, count, item.Grade);
    }

    public void OnItemGather(uint templateId, int count, int grade = -1)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ItemGather,
            TemplateId = templateId,
            Count = Math.Max(1, count),
            Grade = grade
        });
    }

    /// <summary>
    /// Использование предмета в инвентаре (Use of an item from your inventory)
    /// </summary>
    /// <param name="item"></param>
    public void OnItemUse(Item item)
    {
        if (item == null)
            return;
        OnItemUse(item.TemplateId, 1, item.Grade);
    }

    public void OnItemUse(uint templateId, int count = 1, int grade = -1)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ItemUse,
            TemplateId = templateId,
            Count = Math.Max(1, count),
            Grade = grade
        });
    }

    /// <summary>
    /// Взаимодействие с doodad, например ломаем шахту по квесту (Interaction with doodad, for example, breaking a mine on a quest)
    /// </summary>
    /// <param name="type"></param>
    /// <param name="target"></param>
    public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
    {
        if (target == null)
            return;
        var phase = target is Doodad doodad ? (int)doodad.FuncGroupId : 0;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.DoodadInteraction,
            SourceObjectId = Owner.ObjId,
            TargetObjectId = target.ObjId,
            TemplateId = target.TemplateId,
            SecondaryId = (uint)type,
            Value = phase,
            Count = 1
        });
    }

    public void OnDoodadPhaseChanged(Doodad doodad)
    {
        if (doodad == null)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.DoodadPhaseChanged,
            TargetObjectId = doodad.ObjId,
            TemplateId = doodad.TemplateId,
            Value = (int)doodad.FuncGroupId,
            Count = 1
        });
    }

    public void OnExpressFire(uint emotionId, uint objId, uint obj2Id)
    {
        var npc = WorldManager.Instance.GetNpc(obj2Id);
        if (npc == null)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ExpressFired,
            SourceObjectId = objId,
            TargetObjectId = obj2Id,
            TemplateId = emotionId,
            SecondaryId = npc.TemplateId,
            Count = 1
        });
    }

    public void OnLevelUp()
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.LevelChanged,
            TemplateId = Owner.Id,
            Level = Owner.Level,
            Value = Owner.Level
        });
    }

    public void OnAbilityLevelChanged(uint abilityId, int level)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.AbilityLevelChanged,
            TemplateId = abilityId,
            Level = level,
            Value = level
        });
    }

    public void OnExperienceGained(int amount)
    {
        if (amount <= 0)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ExperienceGained,
            Count = amount,
            Value = amount
        });
    }

    public void OnHonorGained(int amount)
    {
        if (amount <= 0)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.HonorGained,
            Count = amount,
            Value = amount
        });
    }

    public void OnVocationGained(int amount)
    {
        if (amount <= 0)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.VocationGained,
            Count = amount,
            Value = amount
        });
    }

    public void OnLaborSpent(int amount, uint actabilityGroupId = 0)
    {
        if (amount <= 0)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.LaborSpent,
            GroupId = actabilityGroupId,
            Count = amount,
            Value = amount
        });
    }

    public void OnPlayerKill(Character victim, bool isParty)
    {
        if (victim == null)
            return;
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(victim.Transform.ZoneId)?.GroupId ?? 0;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.PcKill,
            TargetObjectId = victim.ObjId,
            TemplateId = victim.Id,
            ZoneId = zoneGroupId,
            Level = victim.Level,
            Count = 1,
            IsParty = isParty,
            IsPlayer = true
        });
    }

    public void OnMateLevelChanged(Units.Mate mate)
    {
        if (mate == null)
            return;
        var itemTemplateId = ItemManager.Instance.GetItemByItemId(mate.ItemId)?.TemplateId ?? mate.TemplateId;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.MateLevelChanged,
            TargetObjectId = mate.ObjId,
            TemplateId = itemTemplateId,
            SecondaryId = mate.TemplateId,
            Level = mate.Level,
            Value = mate.Level
        });
    }

    public void OnPositionChanged()
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.PositionChanged,
            SourceObjectId = Owner.ObjId,
            ZoneId = ZoneManager.Instance.GetZoneByKey(Owner.Transform.ZoneId)?.GroupId ?? 0,
            Count = 1
        });
    }

    public void OnQuestComplete(uint questId)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.QuestCompleted,
            TemplateId = questId,
            Count = 1
        });
    }

    public void OnEnterSphere(SphereQuest sphereQuest)
    {
        if (sphereQuest == null)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.EnterSphere,
            TemplateId = sphereQuest.QuestId,
            SecondaryId = sphereQuest.ComponentId,
            ZoneId = sphereQuest.ZoneId,
            Count = 1
        });
    }

    public void OnCraft(Craft craft)
    {
        if (craft == null)
            return;
        OnCraft(craft.Id, craft.WiId);
    }

    public void OnCraft(uint craftId, uint wiId = 0)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.Craft,
            TemplateId = craftId,
            SecondaryId = wiId,
            Count = 1
        });
    }

    public void OnCinemaStarted(uint cinemaId)
    {
        _activeCinemaId = cinemaId;
    }

    public void OnCinemaCompleted()
    {
        var cinemaId = _activeCinemaId;
        _activeCinemaId = 0;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.CinemaCompleted,
            TemplateId = cinemaId,
            Count = 1
        });
    }

    public void OnMailSent(IEnumerable<Item> attachments)
    {
        var items = attachments?
            .Where(x => x != null)
            .GroupBy(x => x.TemplateId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Count))
            ?? new Dictionary<uint, int>();
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.MailSent,
            Count = 1,
            Items = items
        });
    }

    public void OnBackpackSold(uint itemTemplateId)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.BackpackSold,
            TemplateId = itemTemplateId,
            Count = 1
        });
    }

    public void OnTeamInvite(Character target)
    {
        if (target == null)
            return;
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.TeamInvite,
            TargetObjectId = target.ObjId,
            FactionId = target.Faction?.Id ?? 0,
            Count = 1
        });
    }

    public void OnEffectFired(uint effectId)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.EffectFired,
            TemplateId = effectId,
            Count = 1
        });
    }

    public void OnEvolvingMaterialConsumed(int count = 1)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.EvolvingMaterialConsumed,
            Count = Math.Max(1, count)
        });
    }

    public void OnEnchantScaleChanged(int count = 1)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.EnchantScaleChanged,
            Count = Math.Max(1, count)
        });
    }

    public void OnConditionChanged(uint conditionId)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ConditionChanged,
            TemplateId = conditionId,
            Count = 1
        });
    }

    public void OnConquestWarResult(uint zoneGroupId, int rank)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.ConquestWarResult,
            ZoneId = zoneGroupId,
            Rank = rank,
            Count = 1
        });
    }

    public void OnFactionCompetitionResult(uint zoneGroupId, int rank)
    {
        DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.FactionCompetitionResult,
            ZoneId = zoneGroupId,
            Rank = rank,
            Count = 1
        });
    }

    public void AddCompletedQuest(CompletedQuest quest)
    {
        CompletedQuests.Add(quest.Id, quest);
    }

    public void ResetCompletedQuest(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        var quest = GetCompletedQuest(completeId);

        if (quest == null) { return; }

        quest.Body.Set((int)questId - completeId * 64, false);
        CompletedQuests[completeId] = quest;
    }

    public CompletedQuest GetCompletedQuest(ushort id)
    {
        return CompletedQuests.TryGetValue(id, out var quest) ? quest : null;
    }

    public bool IsQuestComplete(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        if (!CompletedQuests.ContainsKey(completeId))
            return false;
        return CompletedQuests[completeId].Body[(int)(questId - completeId * 64)];
    }

    public bool CanDisplayDoodadQuestMarker(Doodad doodad, DoodadFuncQuest questFunc,
        out uint componentId, out string reason)
    {
        componentId = 0;
        if (doodad == null || questFunc == null)
        {
            reason = "missing-doodad-or-quest-function";
            return false;
        }

        var template = QuestManager.Instance.GetTemplate(questFunc.QuestId);
        if (template == null)
        {
            reason = "quest-template-not-loaded";
            return false;
        }

        if (questFunc.QuestKindId == 1)
        {
            if (ActiveQuests.ContainsKey(questFunc.QuestId))
            {
                reason = "quest-already-active";
                return false;
            }

            if (HasQuestCompleted(questFunc.QuestId) && !template.Repeatable)
            {
                reason = "quest-completed-and-not-repeatable";
                return false;
            }

            var probe = new Quest(template)
            {
                Owner = Owner
            };
            return probe.CanStartFromDoodad(doodad.ObjId, doodad.TemplateId, out componentId, out reason);
        }

        if (questFunc.QuestKindId == 2)
        {
            if (!ActiveQuests.TryGetValue(questFunc.QuestId, out var activeQuest))
            {
                reason = "quest-is-not-active";
                return false;
            }

            return activeQuest.CanReportAtDoodad(doodad.ObjId, doodad.TemplateId, out componentId, out reason);
        }

        reason = $"unsupported-quest-kind-{questFunc.QuestKindId}";
        return false;
    }

    public void LogDoodadQuestMarkerCandidates(Doodad doodad)
    {
        if (doodad == null)
            return;

        var currentFuncs = DoodadManager.Instance.GetFuncsForGroup(doodad.FuncGroupId);
        var questFuncs = currentFuncs
            .Where(x => x.FuncType == nameof(DoodadFuncQuest))
            .Select(x => DoodadManager.Instance.GetFuncTemplate(x.FuncId, x.FuncType))
            .OfType<DoodadFuncQuest>()
            .ToArray();

        var configuredQuestGroups = doodad.Template?.FuncGroups
            .SelectMany(group => DoodadManager.Instance.GetFuncsForGroup(group.Id)
                .Where(x => x.FuncType == nameof(DoodadFuncQuest))
                .Select(x => new
                {
                    Group = group,
                    Quest = DoodadManager.Instance.GetFuncTemplate(x.FuncId, x.FuncType) as DoodadFuncQuest
                }))
            .Where(x => x.Quest != null)
            .Select(x => $"{x.Group.Id}/{x.Group.GroupKindId}:q{x.Quest.QuestId}:k{x.Quest.QuestKindId}")
            .ToArray() ?? Array.Empty<string>();

        if (questFuncs.Length == 0)
        {
            Logger.Warn(
                "Doodad quest marker diagnostic: char={0}:{1} level={2} race={3}, doodad={4}:{5}, currentGroup={6}, currentGroupHasQuest=False, configuredQuestGroups=[{7}]",
                Owner.Name, Owner.Id, Owner.Level, Owner.Race, doodad.TemplateId, doodad.ObjId, doodad.FuncGroupId,
                string.Join(",", configuredQuestGroups));
            return;
        }

        foreach (var questFunc in questFuncs)
        {
            var available = CanDisplayDoodadQuestMarker(doodad, questFunc, out var componentId, out var reason);
            Logger.Warn(
                "Doodad quest marker diagnostic: char={0}:{1} level={2} race={3}, doodad={4}:{5}, currentGroup={6}, quest={7}, kind={8}, component={9}, available={10}, reason={11}, configuredQuestGroups=[{12}]",
                Owner.Name, Owner.Id, Owner.Level, Owner.Race, doodad.TemplateId, doodad.ObjId, doodad.FuncGroupId,
                questFunc.QuestId, questFunc.QuestKindId, componentId, available, reason,
                string.Join(",", configuredQuestGroups));
        }
    }

    public void RefreshQuestNotifier()
    {
        Owner.SendPacket(new SCQuestNotifierInitPacket(true));
    }

    public void Send()
    {
        var quests = ActiveQuests.Values.ToArray();
        if (quests.Length <= 20)
        {
            Owner.SendPacket(new SCQuestsPacket(quests));
            return;
        }

        for (var i = 0; i < quests.Length; i += 20)
        {
            var size = quests.Length - i >= 20 ? 20 : quests.Length - i;
            var res = new Quest[size];
            Array.Copy(quests, i, res, 0, size);
            Owner.SendPacket(new SCQuestsPacket(res));
        }
    }

    /// <summary>
    /// Tells the client which quests this player has already finished.
    /// </summary>
    /// <remarks>
    /// We keep them packed - one record per sixty-four quests, a bit each - and the client wants
    /// them one by one, so they are unpacked here. Sending our own packing instead left the client
    /// with no idea which quests were done, so a giver went on offering a finished quest that the
    /// server then refused to hand out: visible, unclickable, forever.
    ///
    /// Numbers no design exists for are left out. They cost a lookup each, once per login, and the
    /// client would only discard them after doing the same lookup itself.
    /// </remarks>
    public void SendCompleted()
    {
        var questIds = new List<uint>();
        foreach (var (blockId, block) in CompletedQuests)
        {
            for (var bit = 0; bit < CompletedQuestsPerBlock; bit++)
            {
                if (!block.Body[bit])
                    continue;

                var questId = (uint)blockId * CompletedQuestsPerBlock + (uint)bit;
                if (QuestManager.Instance.GetTemplate(questId) == null)
                    continue;

                questIds.Add(questId);
            }
        }

        if (questIds.Count == 0)
        {
            Owner.SendPacket(new SCCompletedQuestsPacket([]));
            return;
        }

        for (var i = 0; i < questIds.Count; i += SCCompletedQuestsPacket.MaxEntries)
        {
            var size = Math.Min(SCCompletedQuestsPacket.MaxEntries, questIds.Count - i);
            Owner.SendPacket(new SCCompletedQuestsPacket(questIds.GetRange(i, size).ToArray()));
        }
    }

    public void ResetQuests(QuestDetail questDetail, bool sendIfChanged = true) => ResetQuests(new QuestDetail[] { questDetail }, sendIfChanged);

    private void ResetQuests(QuestDetail[] questDetail, bool sendIfChanged = true)
    {
        foreach (var (completeBlockId, completeBlock) in CompletedQuests)
        {
            for (var blockIndex = 0; blockIndex < 64; blockIndex++)
            {
                var questId = (uint)(completeBlockId * 64) + (uint)blockIndex;
                var q = QuestManager.Instance.GetTemplate(questId);
                // Skip unused Ids
                if (q == null)
                    continue;
                // Skip if quest still active
                if (HasQuest(questId))
                    continue;

                foreach (var qd in questDetail)
                {
                    if ((q.DetailId == qd) && (completeBlock.Body[blockIndex]))
                    {
                        completeBlock.Body.Set(blockIndex, false);
                        Logger.Info($"QuestReset by {Owner.Name}, reset {questId}");
                        if (sendIfChanged)
                        {
                            var body = new byte[8];
                            completeBlock.Body.CopyTo(body, 0);
                            Owner.SendPacket(new SCQuestContextResetPacket(questId, body, completeBlockId));
                        }
                    }
                }
            }
        }
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM completed_quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var quest = new CompletedQuest
                {
                    Id = reader.GetUInt16("id"),
                    Body = new BitArray((byte[])reader.GetValue("data"))
                };
                CompletedQuests[quest.Id] = quest;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var templateId = reader.GetUInt32("template_id");
                var template = QuestManager.Instance.GetTemplate(templateId);
                if (template == null)
                {
                    Logger.Warn("Skipping persisted quest {0}: target template is missing", templateId);
                    continue;
                }
                var quest = new Quest(template)
                {
                    Id = reader.GetUInt32("id"),
                    TemplateId = templateId,
                    Owner = Owner,
                    Status = (QuestStatus)reader.GetByte("status")
                };
                quest.ReadData((byte[])reader.GetValue("data"));
                quest.RestoreRuntimeAfterLoad();
                ActiveQuests[templateId] = quest;
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                var ids = string.Join(",", _removed);
                command.CommandText = $"DELETE FROM quests WHERE owner = @owner AND template_id IN({ids})";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            _removed.Clear();
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO completed_quests(`id`,`data`,`owner`) VALUES(@id,@data,@owner)";
            foreach (var quest in CompletedQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                var body = new byte[8];
                quest.Body.CopyTo(body, 0);
                command.Parameters.AddWithValue("@data", body);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO quests(`id`,`template_id`,`data`,`status`,`owner`) VALUES(@id,@template_id,@data,@status,@owner)";

            foreach (var quest in ActiveQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                command.Parameters.AddWithValue("@template_id", quest.TemplateId);
                command.Parameters.AddWithValue("@data", quest.WriteData());
                command.Parameters.AddWithValue("@status", (byte)quest.Status);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }
    }

    public void CheckDailyResetAtLogin()
    {
        // TODO: Put Server timezone offset in configuration file, currently using local machine midnight
        // var utcDelta = DateTime.Now - DateTime.UtcNow;
        // var isOld = (DateTime.Today + utcDelta - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        var isOld = (DateTime.Today - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        if (isOld)
            ResetDailyQuests(false);
    }

    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        Owner.Quests.ResetQuests(
            new QuestDetail[]
            {
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            }, true
        );
    }

    public void RecallEvents()
    {
        foreach (var quest in ActiveQuests.Values)
            quest.RestoreRuntimeAfterLoad();
    }
}
