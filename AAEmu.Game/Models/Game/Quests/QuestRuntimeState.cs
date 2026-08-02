using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Quests;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Target 10.8 data-driven quest runtime. Static definitions come from QuestSQLite;
/// mutable progress is stored here per quest_acts.id and persisted in MySQL quests.data.
/// </summary>
public partial class Quest
{
    public const int ClientObjectiveCount = 10;
    private const uint RuntimeBlobMagic = 0x34545351; // QST4 little endian
    private const byte RuntimeBlobVersion = 4;
    private const int MaxComponentTransitions = 128;

    private readonly object _runtimeLock = new();
    private readonly Dictionary<uint, int> _runtimeActProgress = new();
    private readonly HashSet<uint> _runtimeCompletedComponents = new();
    private uint _objectiveComponentId;
    private uint _acceptedSourceObjectId;
    private uint _acceptedSourceTemplateId;
    private uint _reportSourceObjectId;
    private uint _reportSourceTemplateId;
    private DateTime _acceptedAt = DateTime.UtcNow;
    private int _runtimeScore;
    private int _completionState;

    public IReadOnlyDictionary<uint, int> RuntimeActProgress => _runtimeActProgress;
    public IReadOnlyCollection<uint> RuntimeCompletedComponents => _runtimeCompletedComponents;
    public uint ObjectiveComponentId => _objectiveComponentId;
    public uint AcceptedSourceObjectId => _acceptedSourceObjectId;
    public uint AcceptedSourceTemplateId => _acceptedSourceTemplateId;
    public uint ReportSourceObjectId => _reportSourceObjectId;
    public uint ReportSourceTemplateId => _reportSourceTemplateId;
    public DateTime AcceptedAt => _acceptedAt;
    public int RuntimeScore => _runtimeScore;
    public bool IsCompletionLocked => Volatile.Read(ref _completionState) != 0;

    public bool StartRuntime(bool forcibly, QuestAcceptorType sourceType, uint sourceObjectId, uint sourceTemplateId)
    {
        lock (_runtimeLock)
        {
            if (Template == null || Owner == null)
                return false;

            if (!forcibly && !ValidateContextRequirements(out var contextReason))
            {
                Logger.Warn("Quest {0}: context requirements failed for {1}: {2}",
                    TemplateId, Owner.Name, contextReason);
                return false;
            }

            _runtimeActProgress.Clear();
            _runtimeCompletedComponents.Clear();
            _objectiveComponentId = 0;
            _runtimeScore = 0;
            _acceptedAt = DateTime.UtcNow;
            Time = DateTime.MinValue;
            QuestAcceptorType = sourceType;
            AcceptorType = sourceTemplateId;
            _acceptedSourceObjectId = sourceObjectId;
            _acceptedSourceTemplateId = sourceTemplateId;
            _reportSourceObjectId = 0;
            _reportSourceTemplateId = 0;
            DoodadId = sourceType == QuestAcceptorType.Doodad ? sourceObjectId : 0;
            Status = QuestStatus.Invalid;
            Step = QuestComponentKind.None;

            var startComponent = FindStartComponent(forcibly);
            if (startComponent == null)
            {
                Logger.Warn("Quest {0}: no acceptable Start/None component for source {1}:{2}",
                    TemplateId, sourceType, sourceTemplateId);
                return false;
            }

            CurrentComponentId = startComponent.Id;
            ComponentId = startComponent.Id;
            Step = startComponent.KindId;
            _runtimeCompletedComponents.Add(startComponent.Id);
            ConfigureComponentTimer(startComponent);

            if (!AdvanceFrom(startComponent))
                return false;

            RebuildClientObjectives();
            return true;
        }
    }

    public void RestoreRuntimeAfterLoad()
    {
        lock (_runtimeLock)
        {
            if (Template == null)
                return;

            if (CurrentComponentId == 0 || !Template.Components.ContainsKey(CurrentComponentId))
            {
                var progress = Template.GetComponents(QuestComponentKind.Progress).OrderBy(x => x.Id).FirstOrDefault();
                var ready = Template.GetComponents(QuestComponentKind.Ready).OrderBy(x => x.Id).FirstOrDefault();
                var fallback = Status == QuestStatus.Ready ? ready : progress;
                fallback ??= Template.Components.Values.OrderBy(x => x.Id).FirstOrDefault();
                CurrentComponentId = fallback?.Id ?? 0;
            }

            if (CurrentComponentId != 0 && Template.Components.TryGetValue(CurrentComponentId, out var current))
            {
                Step = current.KindId;
                ComponentId = current.Id;
                if (current.KindId == QuestComponentKind.Progress)
                    _objectiveComponentId = current.Id;
            }

            if (_acceptedAt == default)
                _acceptedAt = DateTime.UtcNow;

            InitializeCurrentComponent(false);
            RebuildClientObjectives();
        }
    }

    public bool CanStartFromDoodad(uint sourceObjectId, uint sourceTemplateId, out uint componentId, out string reason)
    {
        lock (_runtimeLock)
        {
            componentId = 0;
            reason = null;
            if (Template == null || Owner == null)
            {
                reason = "missing-template-or-owner";
                return false;
            }

            if (!ValidateContextRequirements(out reason))
                return false;

            var previousAcceptorType = QuestAcceptorType;
            var previousSourceObjectId = _acceptedSourceObjectId;
            var previousSourceTemplateId = _acceptedSourceTemplateId;
            try
            {
                QuestAcceptorType = QuestAcceptorType.Doodad;
                _acceptedSourceObjectId = sourceObjectId;
                _acceptedSourceTemplateId = sourceTemplateId;

                foreach (var component in GetStartComponents())
                {
                    if (component.HideQuestMarker)
                    {
                        reason = $"component-{component.Id}-hide_quest_marker";
                        continue;
                    }

                    var doodadActs = component.Acts.OfType<QuestAct>()
                        .Where(x => x.DetailType == "QuestActConAcceptDoodad")
                        .OrderBy(x => x.Id)
                        .ToArray();
                    if (doodadActs.Length == 0)
                    {
                        reason = $"component-{component.Id}-has-no-accept-doodad-act";
                        continue;
                    }

                    var sourceMatches = doodadActs.Any(act =>
                    {
                        var expectedTemplateId = act.Definition?.GetUInt32("doodad_id") ?? 0;
                        return expectedTemplateId == 0 || expectedTemplateId == sourceTemplateId;
                    });
                    if (!sourceMatches)
                    {
                        var expected = string.Join(",", doodadActs
                            .Select(x => x.Definition?.GetUInt32("doodad_id") ?? 0)
                            .Distinct());
                        reason = $"component-{component.Id}-accept-doodad-mismatch(expected={expected},actual={sourceTemplateId})";
                        continue;
                    }

                    if (!ValidateAcceptComponent(component, out reason))
                        continue;

                    componentId = component.Id;
                    reason = $"available-start-component-{component.Id}";
                    return true;
                }

                reason ??= "no-visible-start-component";
                return false;
            }
            finally
            {
                QuestAcceptorType = previousAcceptorType;
                _acceptedSourceObjectId = previousSourceObjectId;
                _acceptedSourceTemplateId = previousSourceTemplateId;
            }
        }
    }

    public bool CanReportAtDoodad(uint sourceObjectId, uint sourceTemplateId, out uint componentId, out string reason)
    {
        lock (_runtimeLock)
        {
            componentId = 0;
            reason = null;
            if (Status != QuestStatus.Ready)
            {
                reason = $"quest-status-{Status}";
                return false;
            }

            var components = Template.GetComponents(QuestComponentKind.Ready)
                .Where(x => CurrentComponentId == 0 || x.Id == CurrentComponentId)
                .OrderBy(x => x.Id)
                .ToArray();
            if (components.Length == 0)
            {
                reason = "no-current-ready-component";
                return false;
            }

            foreach (var component in components)
            {
                if (component.HideQuestMarker)
                {
                    reason = $"component-{component.Id}-hide_quest_marker";
                    continue;
                }

                var reportActs = component.Acts.OfType<QuestAct>()
                    .Where(x => x.DetailType == "QuestActConReportDoodad")
                    .OrderBy(x => x.Id)
                    .ToArray();
                if (reportActs.Length == 0)
                {
                    reason = $"component-{component.Id}-has-no-report-doodad-act";
                    continue;
                }

                if (!reportActs.Any(x => x.Definition?.GetUInt32("doodad_id") == sourceTemplateId))
                {
                    var expected = string.Join(",", reportActs
                        .Select(x => x.Definition?.GetUInt32("doodad_id") ?? 0)
                        .Distinct());
                    reason = $"component-{component.Id}-report-doodad-mismatch(expected={expected},actual={sourceTemplateId})";
                    continue;
                }

                componentId = component.Id;
                reason = $"available-ready-component-{component.Id}";
                return true;
            }

            reason ??= "no-visible-ready-component";
            return false;
        }
    }

    private bool ValidateContextRequirements(out string reason)
    {
        if (Template.MinLevel > 0 && Owner.Level < Template.MinLevel)
        {
            reason = $"level-{Owner.Level}-below-min-{Template.MinLevel}";
            return false;
        }

        if (Template.MaxLevel > 0 && Owner.Level > Template.MaxLevel)
        {
            reason = $"level-{Owner.Level}-above-max-{Template.MaxLevel}";
            return false;
        }

        var raceMask = Template.RaceMask;
        if (raceMask is not (0 or byte.MaxValue))
        {
            var race = (int)Owner.Race;
            if (race <= 0 || race > 8)
            {
                reason = $"race-{Owner.Race}-has-no-mask-bit";
                return false;
            }

            var raceBit = 1 << (race - 1);
            if ((raceMask & raceBit) == 0)
            {
                reason = $"race-{Owner.Race}-not-in-mask-0x{raceMask:X2}";
                return false;
            }
        }

        reason = "context-requirements-ok";
        return true;
    }

    private QuestComponent[] GetStartComponents()
    {
        var starts = Template.Components.Values
            .Where(x => x.KindId is QuestComponentKind.None or QuestComponentKind.Start)
            .OrderBy(x => x.KindId)
            .ThenBy(x => x.Id)
            .ToArray();
        return starts.Length == 0 ? GetRootComponents() : starts;
    }

    private QuestComponent FindStartComponent(bool forcibly)
    {
        return GetStartComponents().FirstOrDefault(x => forcibly || ValidateAcceptComponent(x));
    }

    private QuestComponent[] GetRootComponents()
    {
        var incoming = Template.Components.Values
            .Where(x => x.NextComponent != 0)
            .Select(x => x.NextComponent)
            .ToHashSet();
        return Template.Components.Values
            .Where(x => !incoming.Contains(x.Id))
            .OrderBy(x => x.KindId)
            .ThenBy(x => x.Id)
            .ToArray();
    }

    private bool ValidateAcceptComponent(QuestComponent component) => ValidateAcceptComponent(component, out _);

    private bool ValidateAcceptComponent(QuestComponent component, out string reason)
    {
        var acts = component.Acts.OfType<QuestAct>().OrderBy(x => x.Id).ToArray();
        var acceptActs = acts.Where(x => x.DetailType.StartsWith("QuestActConAccept", StringComparison.Ordinal)).ToArray();
        if (acceptActs.Length == 0)
        {
            reason = $"component-{component.Id}-has-no-accept-conditions";
            return true;
        }

        var sourceActs = acceptActs.Where(IsSourceAcceptAct).ToArray();
        var conditionActs = acceptActs.Where(x => !IsSourceAcceptAct(x)).ToArray();
        foreach (var conditionAct in conditionActs)
        {
            if (EvaluateAcceptAct(conditionAct))
                continue;
            reason = $"component-{component.Id}-condition-{conditionAct.Id}-{conditionAct.DetailType}-failed";
            return false;
        }

        if (sourceActs.Length == 0)
        {
            reason = $"component-{component.Id}-conditions-ok-no-source-act";
            return true;
        }

        if (sourceActs.Any(EvaluateAcceptAct))
        {
            reason = $"component-{component.Id}-accept-source-ok";
            return true;
        }

        reason = $"component-{component.Id}-source-condition-failed";
        return false;
    }

    private static bool IsSourceAcceptAct(QuestAct act) => act.DetailType is
        "QuestActConAcceptDoodad" or
        "QuestActConAcceptItem" or
        "QuestActConAcceptNpc" or
        "QuestActConAcceptNpcEmotion" or
        "QuestActConAcceptNpcGroup" or
        "QuestActConAcceptNpcKill" or
        "QuestActConAcceptSphere" or
        "QuestActConAcceptUi";

    private bool EvaluateAcceptAct(QuestAct act)
    {
        var d = act.Definition;
        if (d == null)
            return false;

        switch (act.DetailType)
        {
            case "QuestActConAcceptBuff":
                return Owner.Buffs?.CheckBuff(d.GetUInt32("buff_id")) == true;
            case "QuestActConAcceptComponent":
            {
                var questId = d.GetUInt32("quest_context_id");
                return Owner.Quests.HasQuest(questId) || Owner.Quests.HasQuestCompleted(questId);
            }
            case "QuestActConAcceptDoodad":
                return QuestAcceptorType == QuestAcceptorType.Doodad &&
                       SourceMatches(d.GetUInt32("doodad_id"));
            case "QuestActConAcceptItem":
            {
                var itemId = d.GetUInt32("item_id");
                if (QuestAcceptorType != QuestAcceptorType.Item || !SourceMatches(itemId))
                    return false;
                return !d.GetBoolean("check_exist") || GetInventoryCount(itemId, -1) > 0;
            }
            case "QuestActConAcceptItemGain":
                return GetInventoryCount(d.GetUInt32("item_id"), -1) >= Math.Max(1, d.GetInt32("count", 1));
            case "QuestActConAcceptLevelRange":
                return Owner.Level >= d.GetInt32("level_min") && Owner.Level <= d.GetInt32("level_max", byte.MaxValue);
            case "QuestActConAcceptLevelUp":
                return Owner.Level >= d.GetInt32("level");
            case "QuestActConAcceptNpc":
            case "QuestActConAcceptNpcEmotion":
            case "QuestActConAcceptNpcKill":
                return QuestAcceptorType == QuestAcceptorType.Npc && SourceMatches(d.GetUInt32("npc_id"));
            case "QuestActConAcceptNpcGroup":
                return QuestAcceptorType == QuestAcceptorType.Npc &&
                       QuestManager.Instance.CheckGroupNpc(d.GetUInt32("quest_monster_group_id"), _acceptedSourceTemplateId);
            case "QuestActConAcceptSphere":
                return QuestAcceptorType == QuestAcceptorType.Sphere && SourceMatches(d.GetUInt32("sphere_id"));
            case "QuestActConAcceptUi":
                return QuestAcceptorType is QuestAcceptorType.Unknown or QuestAcceptorType.Skill or QuestAcceptorType.Buff;
            case "QuestActConAutoComplete":
                return true;
            default:
                Logger.Warn("Quest {0}: unhandled accept act {1}; accepted as data-only condition", TemplateId, act.DetailType);
                return true;
        }
    }

    private bool SourceMatches(uint expectedTemplateId) => expectedTemplateId == 0 || expectedTemplateId == _acceptedSourceTemplateId;

    private bool AdvanceFrom(QuestComponent completedComponent)
    {
        var visited = new HashSet<uint>();
        var next = ResolveNextComponent(completedComponent);
        for (var transitions = 0; transitions < MaxComponentTransitions; transitions++)
        {
            if (next == null)
            {
                Status = QuestStatus.Ready;
                Step = QuestComponentKind.Ready;
                return true;
            }

            if (!visited.Add(next.Id))
            {
                Logger.Error("Quest {0}: component cycle detected at {1}", TemplateId, next.Id);
                return false;
            }

            CurrentComponentId = next.Id;
            ComponentId = next.Id;
            Step = next.KindId;
            ConfigureComponentTimer(next);

            switch (next.KindId)
            {
                case QuestComponentKind.None:
                case QuestComponentKind.Start:
                    if (!ValidatePassiveChecks(next))
                        return false;
                    _runtimeCompletedComponents.Add(next.Id);
                    next = ResolveNextComponent(next);
                    continue;

                case QuestComponentKind.Supply:
                    if (!ApplySupplyComponent(next, false, 0, 100))
                        return false;
                    _runtimeCompletedComponents.Add(next.Id);
                    next = ResolveNextComponent(next);
                    continue;

                case QuestComponentKind.Progress:
                    _objectiveComponentId = next.Id;
                    Status = QuestStatus.Progress;
                    InitializeProgressComponent(next);
                    if (IsProgressComponentComplete(next, false))
                    {
                        _runtimeCompletedComponents.Add(next.Id);
                        next = ResolveNextComponent(next);
                        continue;
                    }
                    return true;

                case QuestComponentKind.Ready:
                    Status = QuestStatus.Ready;
                    InitializeCurrentComponent(false);
                    return true;

                case QuestComponentKind.Reward:
                    Status = QuestStatus.Ready;
                    return true;

                case QuestComponentKind.Fail:
                    Status = QuestStatus.Failed;
                    return true;

                case QuestComponentKind.Drop:
                    Status = QuestStatus.Dropped;
                    return true;

                default:
                    Logger.Error("Quest {0}: unknown component kind {1} at {2}", TemplateId, next.KindId, next.Id);
                    return false;
            }
        }

        Logger.Error("Quest {0}: exceeded component transition limit", TemplateId);
        return false;
    }

    private QuestComponent ResolveNextComponent(QuestComponent current)
    {
        if (current.NextComponent != 0 && Template.Components.TryGetValue(current.NextComponent, out var linked))
            return linked;

        return Template.Components.Values
            .Where(x => !_runtimeCompletedComponents.Contains(x.Id) &&
                        (x.KindId > current.KindId || (x.KindId == current.KindId && x.Id > current.Id)))
            .OrderBy(x => x.KindId)
            .ThenBy(x => x.Id)
            .FirstOrDefault();
    }

    private void InitializeCurrentComponent(bool sendUpdate)
    {
        if (CurrentComponentId == 0 || !Template.Components.TryGetValue(CurrentComponentId, out var component))
            return;
        if (component.KindId == QuestComponentKind.Progress)
            InitializeProgressComponent(component);
        ConfigureComponentTimer(component);
        if (sendUpdate)
            SendRuntimeUpdate();
    }

    private void InitializeProgressComponent(QuestComponent component)
    {
        foreach (var act in component.Acts.OfType<QuestAct>().OrderBy(x => x.Id))
        {
            if (!_runtimeActProgress.ContainsKey(act.Id))
                _runtimeActProgress[act.Id] = GetInitialProgress(act);
        }
        RebuildClientObjectives();
    }

    private int GetInitialProgress(QuestAct act)
    {
        var d = act.Definition;
        if (d == null)
            return 0;
        switch (act.DetailType)
        {
            case "QuestActObjItemGather" when d.GetBoolean("check_exist"):
                return GetInventoryCount(d.GetUInt32("item_id"), d.GetBoolean("use_grade") ? d.GetInt32("item_grade_id") : -1);
            case "QuestActObjItemGroupGather" when d.GetBoolean("check_exist"):
                return QuestManager.Instance.GetGroupItems(d.GetUInt32("item_group_id")).Sum(x => GetInventoryCount(x, -1));
            case "QuestActObjItemUse" when d.GetBoolean("check_exist"):
                return GetInventoryCount(d.GetUInt32("item_id"), -1);
            case "QuestActObjLevel":
                return Owner.Level;
            case "QuestActObjAbilityLevel":
                return GetAbilityLevel(d.GetUInt32("ability_id"));
            case "QuestActObjCompleteQuest":
                return Owner.Quests.HasQuestCompleted(d.GetUInt32("quest_id")) ? Math.Max(1, d.GetInt32("count", 1)) : 0;
            case "QuestActObjCompleteQuestGroup":
                return QuestManager.Instance.IsAnyQuestInGroupCompleted(Owner, d.GetUInt32("quest_context_group_id"))
                    ? Math.Max(1, d.GetInt32("count", 1))
                    : 0;
            case "QuestActObjDoodadPhaseCheck":
                return IsDoodadInRequiredPhase(d) ? 1 : 0;
            default:
                return 0;
        }
    }

    private bool ValidatePassiveChecks(QuestComponent component)
    {
        foreach (var act in component.Acts.OfType<QuestAct>().OrderBy(x => x.Id))
        {
            var d = act.Definition;
            if (d == null)
                continue;
            switch (act.DetailType)
            {
                case "QuestActCheckCompleteComponent":
                    if (!_runtimeCompletedComponents.Contains(d.GetUInt32("complete_component")))
                        return false;
                    break;
                case "QuestActCheckGuard":
                    if (WorldManager.Instance.GetNpcByTemplateId(d.GetUInt32("npc_id")) == null)
                        return false;
                    break;
                case "QuestActCheckSphere":
                    if (_acceptedSourceTemplateId != d.GetUInt32("sphere_id") && _reportSourceTemplateId != d.GetUInt32("sphere_id"))
                        return false;
                    break;
                case "QuestActCheckTimer":
                    if (Time != DateTime.MinValue && Time <= DateTime.UtcNow)
                        return false;
                    break;
            }
        }
        return true;
    }

    private void ConfigureComponentTimer(QuestComponent component)
    {
        CancelRuntimeTimer();

        var timer = component.Acts.OfType<QuestAct>()
            .FirstOrDefault(x => x.DetailType == "QuestActCheckTimer")?.Definition;
        if (timer == null)
            return;
        var limit = timer.GetInt32("limit_time");
        if (limit <= 0)
            return;
        if (Time == DateTime.MinValue || Time <= DateTime.UtcNow)
            Time = DateTime.UtcNow.AddMilliseconds(limit);

        var task = new QuestTimeoutTask(Owner, TemplateId);
        if (!QuestManager.Instance.QuestTimeoutTask.TryGetValue(Owner.Id, out var ownerTasks))
        {
            ownerTasks = new Dictionary<uint, QuestTimeoutTask>();
            QuestManager.Instance.QuestTimeoutTask[Owner.Id] = ownerTasks;
        }
        ownerTasks[TemplateId] = task;
        _taskManager.Schedule(task, TimeSpan.FromMilliseconds(Math.Max(1, (Time - DateTime.UtcNow).TotalMilliseconds)));
    }

    private void CancelRuntimeTimer()
    {
        if (!QuestManager.Instance.QuestTimeoutTask.TryGetValue(Owner.Id, out var ownerTasks) ||
            !ownerTasks.TryGetValue(TemplateId, out var task))
            return;

        _ = task.CancelAsync();
        ownerTasks.Remove(TemplateId);
        if (ownerTasks.Count == 0)
            QuestManager.Instance.QuestTimeoutTask.Remove(Owner.Id);
    }

    public bool DispatchRuntimeEvent(QuestRuntimeEvent runtimeEvent, bool sendUpdate = true)
    {
        if (runtimeEvent == null)
            return false;

        lock (_runtimeLock)
        {
            var previousStatus = Status;
            var previousComponentId = CurrentComponentId;
            if (Status != QuestStatus.Progress || CurrentComponentId == 0 ||
                !Template.Components.TryGetValue(CurrentComponentId, out var component) ||
                component.KindId != QuestComponentKind.Progress)
                return false;

            var changed = false;
            foreach (var act in component.Acts.OfType<QuestAct>().OrderBy(x => x.Id))
            {
                if (!IsObjectiveAct(act) || !MatchesRuntimeEvent(act, runtimeEvent))
                    continue;

                var oldValue = GetActProgress(act.Id);
                var newValue = CalculateProgressAfterEvent(act, runtimeEvent, oldValue);
                if (newValue == oldValue)
                    continue;

                _runtimeActProgress[act.Id] = newValue;
                changed = true;
                if (Template.Score > 0)
                    _runtimeScore = CalculateScore(component);
            }

            if (!changed)
                return false;

            RebuildClientObjectives();
            var complete = IsProgressComponentComplete(component, Template.LetItDone && EarlyCompletion);
            if (complete && (!Template.LetItDone || GetCompletionPercent() >= 100))
            {
                _runtimeCompletedComponents.Add(component.Id);
                AdvanceFrom(component);
                RebuildClientObjectives();
            }

            if (sendUpdate)
                SendRuntimeUpdate(previousStatus != Status || previousComponentId != CurrentComponentId);
            return true;
        }
    }

    public bool TryEnableLetItDone()
    {
        lock (_runtimeLock)
        {
            if (!Template.LetItDone || Status != QuestStatus.Progress)
                return Status == QuestStatus.Ready;
            if (GetCompletionPercent() < 50)
                return false;

            EarlyCompletion = true;
            if (Template.Components.TryGetValue(CurrentComponentId, out var component) && component.KindId == QuestComponentKind.Progress)
            {
                _runtimeCompletedComponents.Add(component.Id);
                if (!AdvanceFrom(component))
                    return false;
            }
            RebuildClientObjectives();
            SendRuntimeUpdate(true);
            return Status == QuestStatus.Ready;
        }
    }

    private int CalculateProgressAfterEvent(QuestAct act, QuestRuntimeEvent e, int oldValue)
    {
        var required = GetRequiredProgress(act);
        var increment = Math.Max(1, e.Count);
        switch (act.DetailType)
        {
            case "QuestActObjLevel":
            case "QuestActObjAbilityLevel":
                return Math.Max(oldValue, e.Level > 0 ? e.Level : e.Value);
            case "QuestActObjGainExpPoint":
            case "QuestActObjGainHonorPoint":
            case "QuestActObjGainLivingPoint":
            case "QuestActObjLaborPower":
                increment = Math.Max(0, e.Value != 0 ? e.Value : e.Count);
                break;
            case "QuestActObjDoodadPhaseCheck":
            case "QuestActObjSphere":
            case "QuestActObjTalk":
            case "QuestActObjTalkNpcGroup":
            case "QuestActObjAggro":
            case "QuestActObjCinema":
            case "QuestActObjCondition":
                return Math.Max(oldValue, 1);
        }

        if (Template.Score > 0)
            return checked(oldValue + increment);
        var result = checked(oldValue + increment);
        return required > 0 ? Math.Min(result, required) : result;
    }

    private bool IsProgressComponentComplete(QuestComponent component, bool allowEarly)
    {
        var objectives = component.Acts.OfType<QuestAct>().Where(IsObjectiveAct).ToArray();
        if (objectives.Length == 0)
            return ValidatePassiveChecks(component);

        if (Template.Score > 0)
        {
            _runtimeScore = CalculateScore(component);
            var target = allowEarly ? Math.Max(1, (int)Math.Ceiling(Template.Score * 0.5)) : Template.Score;
            return _runtimeScore >= target;
        }

        if (allowEarly)
            return GetCompletionPercent(component) >= 50;
        return objectives.All(x => GetActProgress(x.Id) >= GetRequiredProgress(x));
    }

    private int CalculateScore(QuestComponent component)
    {
        long score = 0;
        foreach (var act in component.Acts.OfType<QuestAct>().Where(IsObjectiveAct))
        {
            var weight = Math.Max(1, act.Definition?.GetInt32("count", 1) ?? 1);
            score += (long)GetActProgress(act.Id) * weight;
        }
        return (int)Math.Min(int.MaxValue, score);
    }

    public int GetCompletionPercent()
    {
        if (CurrentComponentId != 0 && Template.Components.TryGetValue(CurrentComponentId, out var current) && current.KindId == QuestComponentKind.Progress)
            return GetCompletionPercent(current);
        if (_objectiveComponentId != 0 && Template.Components.TryGetValue(_objectiveComponentId, out var objective))
            return GetCompletionPercent(objective);
        return Status == QuestStatus.Ready ? 100 : 0;
    }

    private int GetCompletionPercent(QuestComponent component)
    {
        if (Template.Score > 0)
            return Template.Score <= 0 ? 0 : Math.Clamp(CalculateScore(component) * 100 / Template.Score, 0, 150);

        var objectives = component.Acts.OfType<QuestAct>().Where(IsObjectiveAct).ToArray();
        if (objectives.Length == 0)
            return 100;
        long done = 0;
        foreach (var act in objectives)
        {
            var required = Math.Max(1, GetRequiredProgress(act));
            done += Math.Min(100, GetActProgress(act.Id) * 100 / required);
        }
        return (int)Math.Clamp(done / objectives.Length, 0, 150);
    }

    public void SetReportSource(uint objectId, uint templateId)
    {
        lock (_runtimeLock)
        {
            _reportSourceObjectId = objectId;
            _reportSourceTemplateId = templateId;
        }
    }

    public bool ValidateReport(uint field0, uint field1)
    {
        lock (_runtimeLock)
        {
            if (Status != QuestStatus.Ready)
                return false;

            ResolveReportObject(field0);
            ResolveReportObject(field1);

            var readyComponents = Template.GetComponents(QuestComponentKind.Ready)
                .Where(x => x.Id == CurrentComponentId || CurrentComponentId == 0)
                .DefaultIfEmpty(Template.GetComponents(QuestComponentKind.Ready).OrderBy(x => x.Id).FirstOrDefault())
                .Where(x => x != null)
                .ToArray();
            if (readyComponents.Length == 0)
                return true;

            foreach (var component in readyComponents)
            {
                if (!ValidatePassiveChecks(component))
                    continue;
                var reports = component.Acts.OfType<QuestAct>()
                    .Where(x => x.DetailType.StartsWith("QuestActConReport", StringComparison.Ordinal))
                    .ToArray();
                if (reports.Length == 0 || reports.Any(MatchesReportAct))
                    return true;
            }
            return false;
        }
    }

    private void ResolveReportObject(uint objectId)
    {
        if (objectId == 0)
            return;
        var npc = WorldManager.Instance.GetNpc(objectId);
        if (npc != null)
        {
            _reportSourceObjectId = npc.ObjId;
            _reportSourceTemplateId = npc.TemplateId;
            return;
        }
        var doodad = WorldManager.Instance.GetDoodad(objectId);
        if (doodad != null)
        {
            _reportSourceObjectId = doodad.ObjId;
            _reportSourceTemplateId = doodad.TemplateId;
        }
    }

    private bool MatchesReportAct(QuestAct act)
    {
        var d = act.Definition;
        if (d == null)
            return false;
        switch (act.DetailType)
        {
            case "QuestActConReportJournal":
                return true;
            case "QuestActConReportNpc":
                return _reportSourceTemplateId == d.GetUInt32("npc_id") && IsReportObjectNear();
            case "QuestActConReportNpcGroup":
                return QuestManager.Instance.CheckGroupNpc(d.GetUInt32("quest_monster_group_id"), _reportSourceTemplateId) && IsReportObjectNear();
            case "QuestActConReportDoodad":
                return _reportSourceTemplateId == d.GetUInt32("doodad_id") && IsReportObjectNear();
            default:
                return false;
        }
    }

    private bool IsReportObjectNear()
    {
        if (_reportSourceObjectId == 0)
            return false;
        if (Owner is not BaseUnit ownerUnit)
            return false;
        var unit = WorldManager.Instance.GetUnit(_reportSourceObjectId);
        if (unit != null)
            return unit.GetDistanceTo(ownerUnit) <= 12.0f;
        var doodad = WorldManager.Instance.GetDoodad(_reportSourceObjectId);
        return doodad != null && MathUtil.CalculateDistance(doodad, ownerUnit) <= 12.0f;
    }

    public bool TryEnterCompletion()
    {
        if (Status is QuestStatus.Completed or QuestStatus.DailyCompleted or QuestStatus.Dropped)
            return false;
        return Interlocked.CompareExchange(ref _completionState, 1, 0) == 0;
    }

    public void LeaveCompletion(bool completed) => Volatile.Write(ref _completionState, completed ? 2 : 0);


    private QuestAct[] GetSelectiveRewardActs() => Template.GetComponents(QuestComponentKind.Reward)
        .OrderBy(x => x.Id)
        .SelectMany(x => x.Acts.OfType<QuestAct>().OrderBy(a => a.Id))
        .Where(x => x.DetailType == "QuestActSupplySelectiveItem")
        .ToArray();

    public bool ValidateRewardSelection(int selected)
    {
        var selectiveCount = GetSelectiveRewardActs().Length;
        if (selectiveCount == 0)
            return true;

        // Target client uses 1..N. Zero is accepted for server-side automatic/default completion.
        return selected == 0 || selected >= 1 && selected <= selectiveCount;
    }

    private static int NormalizeSelectiveRewardIndex(int selected) => selected <= 0 ? 0 : selected - 1;

    /// <summary>
    /// Whether accepting this quest hands the player anything.
    /// </summary>
    /// <remarks>
    /// Only such a quest needs its objectives restated once the client knows about it: the goods
    /// are handed over while the quest is still being started, so the client sees them arrive
    /// against a quest it has not been told about yet.
    /// </remarks>
    public bool HasStartingSupply()
    {
        return Template.GetComponents(QuestComponentKind.Supply)
            .SelectMany(component => component.Acts)
            .Any(act => act.DetailType is "QuestActSupplyItem"
                or "QuestActSupplySelectiveItem"
                or "QuestActSupplyRankedItem"
                or "QuestActSupplyResultRankedItem");
    }

    /// <summary>
    /// Which component the turn-in is reported under, worked out without paying anything.
    /// </summary>
    /// <remarks>
    /// The payout used to answer this on its way through, which forced the reward to be handed
    /// over before the quest could be reported finished. Asking separately lets the two happen in
    /// the order they belong in.
    /// </remarks>
    public uint PeekCompletionComponentId()
    {
        lock (_runtimeLock)
        {
            var rewardComponents = Template.GetComponents(QuestComponentKind.Reward).OrderBy(x => x.Id).ToArray();
            return rewardComponents.Length > 0 ? rewardComponents[^1].Id : CurrentComponentId;
        }
    }

    public bool ApplyRuntimeRewards(int selected, bool includeLevelSupply, out uint completedComponentId)
    {
        lock (_runtimeLock)
        {
            completedComponentId = CurrentComponentId;
            var scale = Template.LetItDone ? Math.Clamp(GetCompletionPercent(), 50, 150) : 100;
            var selectiveRewards = GetSelectiveRewardActs();
            uint? selectedSelectiveActId = null;
            if (selectiveRewards.Length > 0)
            {
                var selectedIndex = NormalizeSelectiveRewardIndex(selected);
                if (selectedIndex < 0 || selectedIndex >= selectiveRewards.Length)
                    return false;
                selectedSelectiveActId = selectiveRewards[selectedIndex].Id;
            }

            var rewardComponents = Template.GetComponents(QuestComponentKind.Reward).OrderBy(x => x.Id).ToArray();
            foreach (var component in rewardComponents)
            {
                if (!ApplySupplyComponent(component, true, selected, scale, selectedSelectiveActId))
                    return false;
                completedComponentId = component.Id;
                _runtimeCompletedComponents.Add(component.Id);
            }

            if (includeLevelSupply)
            {
                var levelSupply = QuestManager.Instance.GetSupplies(Template.Level);
                if (levelSupply != null)
                {
                    var exp = ScaleReward(levelSupply.Exp, scale);
                    var copper = ScaleReward(levelSupply.Copper, scale);
                    if (exp != 0)
                        Owner.AddExp(exp, true);
                    if (copper != 0 && !Owner.ChangeMoney(SlotType.Inventory, copper, ItemTaskType.QuestComplete))
                        return false;
                }
            }

            CleanupQuestItems(false);
            return true;
        }
    }

    private bool ApplySupplyComponent(QuestComponent component, bool rewardPhase, int selected, int scale,
        uint? selectedSelectiveActId = null)
    {
        var acts = component.Acts.OfType<QuestAct>().OrderBy(x => x.Id).ToArray();
        var selective = acts.Where(x => x.DetailType == "QuestActSupplySelectiveItem").ToArray();
        QuestAct selectedAct = null;
        if (selective.Length > 0)
        {
            selectedAct = selectedSelectiveActId.HasValue
                ? selective.FirstOrDefault(x => x.Id == selectedSelectiveActId.Value)
                : selective[0];
        }

        foreach (var act in acts)
        {
            if (act.DetailType == "QuestActSupplySelectiveItem" && !ReferenceEquals(act, selectedAct))
                continue;
            if (!ApplySupplyAct(act, rewardPhase, selected, scale))
                return false;
        }
        return true;
    }

    private bool ApplySupplyAct(QuestAct act, bool rewardPhase, int selected, int scale)
    {
        var d = act.Definition;
        if (d == null)
            return false;
        switch (act.DetailType)
        {
            case "QuestActSupplyItem":
            case "QuestActSupplySelectiveItem":
            case "QuestActSupplyRankedItem":
            case "QuestActSupplyResultRankedItem":
            case "QuestActEtcItemObtain":
            {
                if ((act.DetailType is "QuestActSupplyRankedItem" or "QuestActSupplyResultRankedItem") &&
                    d.GetInt32("rank") > 0 && selected > 0 && d.GetInt32("rank") != selected)
                    return true;
                var itemId = d.GetUInt32("item_id");
                var count = Math.Max(1, d.GetInt32("count", 1));
                var grade = d.GetInt32("grade_id", 0);
                if (rewardPhase)
                    count = Math.Max(1, ScaleReward(count, scale));
                return AcquireQuestItem(itemId, count, grade);
            }
            case "QuestActSupplyRemoveItem":
                return ConsumeQuestItem(d.GetUInt32("item_id"), Math.Max(1, d.GetInt32("count", 1)));
            case "QuestActSupplyCopper":
                return Owner.ChangeMoney(SlotType.Inventory, ScaleReward(d.GetInt32("amount"), scale), ItemTaskType.QuestComplete);
            case "QuestActSupplyExp":
                Owner.AddExp(ScaleReward(d.GetInt32("exp"), scale), true);
                return true;
            case "QuestActSupplyHonorPoint":
                Owner.ChangeGamePoints(GamePointKind.Honor, ScaleReward(d.GetInt32("point"), scale));
                return true;
            case "QuestActSupplyLivingPoint":
                Owner.ChangeGamePoints(GamePointKind.Vocation, ScaleReward(d.GetInt32("point"), scale));
                return true;
            case "QuestActSupplyLp":
                Owner.ChangeLabor(checked((short)Math.Clamp(d.GetInt32("lp"), short.MinValue, short.MaxValue)), 0);
                return true;
            case "QuestActSupplyLocalLp":
                if (Owner is Character localLaborOwner)
                    localLaborOwner.LocalLaborPower = checked((short)Math.Clamp(localLaborOwner.LocalLaborPower + d.GetInt32("local_lp"), short.MinValue, short.MaxValue));
                return true;
            case "QuestActSupplyActability":
                if (Owner is Character actabilityOwner)
                    actabilityOwner.Actability.AddPoint(d.GetUInt32("actability_group_id"), d.GetInt32("point"));
                return true;
            case "QuestActSupplyAppellation":
                Owner.Appellations.Add(d.GetUInt32("appellation_id"));
                return true;
            case "QuestActSupplyCrimePoint":
                if (Owner is Character crimeOwner)
                    crimeOwner.CrimePoint = checked((short)Math.Clamp(crimeOwner.CrimePoint + d.GetInt32("point"), short.MinValue, short.MaxValue));
                return true;
            case "QuestActSupplySkill":
                if (Owner is Character skillOwner)
                    skillOwner.Skills.AddSkill(d.GetUInt32("skill_id"));
                return true;
            case "QuestActSupplyArchePassPoint":
            case "QuestActSupplyContributionPoint":
            case "QuestActSupplyExpeditionExp":
            case "QuestActSupplyFactionChange":
            case "QuestActSupplyFamilyExp":
            case "QuestActSupplyJuryPoint":
            case "QuestActSupplyLeadershipPoint":
            case "QuestActSupplyResidentCharge":
            case "QuestActSupplyResidentPoint":
                Logger.Warn("Quest {0}: reward {1} loaded but subsystem mutation API is not implemented in this source", TemplateId, act.DetailType);
                return true;
            default:
                if (act.DetailType.StartsWith("QuestActSupply", StringComparison.Ordinal))
                {
                    Logger.Warn("Quest {0}: unknown supply act {1}", TemplateId, act.DetailType);
                    return true;
                }
                return true;
        }
    }

    private static int ScaleReward(int value, int scale)
    {
        if (value == 0)
            return 0;
        var scaled = (long)value * scale / 100;
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }

    private bool AcquireQuestItem(uint itemId, int count, int grade)
    {
        if (itemId == 0 || count <= 0)
            return true;
        bool result;
        if (ItemManager.Instance.IsAutoEquipTradePack(itemId))
            result = Owner.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, itemId, count, grade);
        else
            result = Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, itemId, count, grade);
        if (!result)
            Owner.SendErrorMessage(ErrorMessageType.BagFull);
        return result;
    }

    private bool ConsumeQuestItem(uint itemId, int count)
    {
        if (itemId == 0 || count <= 0)
            return true;
        return Owner.Inventory.ConsumeItem(new[] { SlotType.Inventory }, ItemTaskType.QuestRemoveSupplies, itemId, count, null) == count;
    }

    public void CleanupQuestItems(bool dropped)
    {
        lock (_runtimeLock)
        {
            // Reward acts carry the same cleanup/destroy_when_drop flags as the items a
            // quest hands out for its own use, but those flags describe the quest's
            // supplies, not its payout. ApplyRuntimeRewards calls this right after
            // granting the rewards, so including the Reward component here handed the
            // player their items and immediately consumed them again - the reason only
            // the selective reward (which the client draws by itself) appeared to survive.
            foreach (var component in Template.Components.Values)
            {
                if (component.KindId == QuestComponentKind.Reward)
                    continue;

                foreach (var act in component.Acts.OfType<QuestAct>())
                {
                var d = act.Definition;
                if (d == null)
                    continue;
                if (!d.Has("item_id"))
                    continue;
                var cleanup = d.GetBoolean("cleanup");
                var destroyOnDrop = d.GetBoolean("destroy_when_drop");
                if (!cleanup && !(dropped && destroyOnDrop))
                    continue;
                var itemId = d.GetUInt32("item_id");
                var count = Math.Max(1, d.GetInt32("count", int.MaxValue));
                var available = GetInventoryCount(itemId, -1);
                if (available > 0)
                    Owner.Inventory.ConsumeItem(new[] { SlotType.Inventory }, ItemTaskType.QuestRemoveSupplies, itemId, Math.Min(available, count), null);
                }
            }
        }
    }

    public void MarkRuntimeCompleted(uint completedComponentId)
    {
        lock (_runtimeLock)
        {
            if (completedComponentId != 0)
            {
                CurrentComponentId = completedComponentId;
                ComponentId = completedComponentId;
                _runtimeCompletedComponents.Add(completedComponentId);
            }
            Status = QuestStatus.Completed;
            Step = QuestComponentKind.Reward;
        }
    }

    public bool ShouldAutoComplete()
    {
        if (Status != QuestStatus.Ready)
            return false;
        return Template.Components.Values
            .SelectMany(x => x.Acts)
            .OfType<QuestAct>()
            .Any(x => x.DetailType == "QuestActConAutoComplete") ||
               Template.GetComponents(QuestComponentKind.Ready).Length == 0;
    }

    public void SendRuntimeUpdate(bool refreshNotifier = false)
    {
        if (Owner == null)
            return;
        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, CurrentComponentId));
        if (refreshNotifier)
            Owner.SendPacket(new SCQuestNotifierInitPacket(true));
    }

    public int[] GetClientObjectiveTargets()
    {
        var result = new int[ClientObjectiveCount];
        if (_objectiveComponentId == 0 || !Template.Components.TryGetValue(_objectiveComponentId, out var component))
            return result;
        var acts = component.Acts.OfType<QuestAct>().Where(IsObjectiveAct).OrderBy(x => x.Id).ToArray();
        for (var i = 0; i < acts.Length; i++)
        {
            var slot = Math.Min(i, ClientObjectiveCount - 1);
            result[slot] = checked(result[slot] + Math.Max(1, GetRequiredProgress(acts[i])));
        }
        return result;
    }

    private void RebuildClientObjectives()
    {
        if (Objectives == null || Objectives.Length != ClientObjectiveCount)
            Objectives = new int[ClientObjectiveCount];
        Array.Clear(Objectives, 0, Objectives.Length);
        if (_objectiveComponentId == 0 || !Template.Components.TryGetValue(_objectiveComponentId, out var component))
            return;
        var acts = component.Acts.OfType<QuestAct>().Where(IsObjectiveAct).OrderBy(x => x.Id).ToArray();
        for (var i = 0; i < acts.Length; i++)
        {
            var slot = Math.Min(i, ClientObjectiveCount - 1);
            Objectives[slot] = checked(Objectives[slot] + GetActProgress(acts[i].Id));
        }
    }

    private int GetActProgress(uint actId) => _runtimeActProgress.TryGetValue(actId, out var value) ? value : 0;

    private static bool IsObjectiveAct(QuestAct act) => act.DetailType.StartsWith("QuestActObj", StringComparison.Ordinal) ||
                                                        act.DetailType == "QuestActEtcItemObtain";

    private int GetRequiredProgress(QuestAct act)
    {
        var d = act.Definition;
        if (d == null)
            return 1;
        return act.DetailType switch
        {
            "QuestActObjAbilityLevel" => Math.Max(1, d.GetInt32("level", 1)),
            "QuestActObjGainExpPoint" or "QuestActObjGainHonorPoint" or "QuestActObjGainLivingPoint" => Math.Max(1, d.GetInt32("point", 1)),
            "QuestActObjLaborPower" => Math.Max(1, d.GetInt32("count", 1)),
            "QuestActObjLevel" or "QuestActObjMateLevel" => Math.Max(1, d.GetInt32("level", 1)),
            "QuestActObjZoneKill" => Math.Max(1, d.GetInt32("count_npc", 0) + d.GetInt32("count_pk", 0)),
            _ => Math.Max(1, d.GetInt32("count", 1))
        };
    }

    private bool MatchesRuntimeEvent(QuestAct act, QuestRuntimeEvent e)
    {
        var d = act.Definition;
        if (d == null)
            return false;
        switch (act.DetailType)
        {
            case "QuestActObjMonsterHunt":
            case "QuestActObjMonsterContrHunt":
                return e.Type == QuestRuntimeEventType.NpcKill && e.TemplateId == d.GetUInt32("npc_id");
            case "QuestActObjMonsterGroupHunt":
            case "QuestActObjMonsterContrGroupHunt":
                return e.Type == QuestRuntimeEventType.NpcKill && QuestManager.Instance.CheckGroupNpc(d.GetUInt32("quest_monster_group_id"), e.TemplateId);
            case "QuestActObjNpcKill":
                return e.Type == QuestRuntimeEventType.NpcKill && MatchNpcKillRestrictions(d, e);
            case "QuestActObjPcKill":
                return e.Type == QuestRuntimeEventType.PcKill && (!d.GetBoolean("is_party") || e.IsParty);
            case "QuestActObjZoneKill":
                return e.Type is QuestRuntimeEventType.NpcKill or QuestRuntimeEventType.PcKill &&
                       (d.GetUInt32("zone_id") == 0 || d.GetUInt32("zone_id") == e.ZoneId);
            case "QuestActObjItemGather":
                return e.Type == QuestRuntimeEventType.ItemGather && e.TemplateId == d.GetUInt32("item_id") &&
                       (!d.GetBoolean("use_grade") || e.Grade == d.GetInt32("item_grade_id"));
            case "QuestActObjItemGroupGather":
                return e.Type == QuestRuntimeEventType.ItemGather && QuestManager.Instance.CheckGroupItem(d.GetUInt32("item_group_id"), e.TemplateId);
            case "QuestActObjItemUse":
                return e.Type == QuestRuntimeEventType.ItemUse && e.TemplateId == d.GetUInt32("item_id");
            case "QuestActObjItemGroupUse":
                return e.Type == QuestRuntimeEventType.ItemUse && QuestManager.Instance.CheckGroupItem(d.GetUInt32("item_group_id"), e.TemplateId);
            case "QuestActObjInteraction":
                return e.Type == QuestRuntimeEventType.DoodadInteraction && MatchInteraction(d, e);
            case "QuestActObjDoodadPhaseCheck":
                return e.Type == QuestRuntimeEventType.DoodadPhaseChanged && e.TemplateId == d.GetUInt32("doodad_id") &&
                       (e.Value == d.GetInt32("phase1") || e.Value == d.GetInt32("phase2"));
            case "QuestActObjTalk":
                return e.Type == QuestRuntimeEventType.TalkNpc && e.TemplateId == d.GetUInt32("npc_id");
            case "QuestActObjTalkNpcGroup":
                return e.Type == QuestRuntimeEventType.TalkNpc && QuestManager.Instance.CheckGroupNpc(d.GetUInt32("npc_group_id"), e.TemplateId);
            case "QuestActObjCraft":
                return e.Type == QuestRuntimeEventType.Craft && e.TemplateId == d.GetUInt32("craft_id");
            case "QuestActObjSphere":
                return e.Type == QuestRuntimeEventType.EnterSphere && (e.TemplateId == d.GetUInt32("sphere_id") || e.SecondaryId == act.ComponentId);
            case "QuestActObjLevel":
                return e.Type == QuestRuntimeEventType.LevelChanged && e.Level >= d.GetInt32("level");
            case "QuestActObjAbilityLevel":
                return e.Type == QuestRuntimeEventType.AbilityLevelChanged &&
                       (d.GetUInt32("ability_id") == 0 || e.TemplateId == d.GetUInt32("ability_id")) && e.Level >= d.GetInt32("level");
            case "QuestActObjMateLevel":
                return e.Type == QuestRuntimeEventType.MateLevelChanged &&
                       (d.GetUInt32("item_id") == 0 || e.TemplateId == d.GetUInt32("item_id")) &&
                       e.Level >= d.GetInt32("level");
            case "QuestActObjDistance":
                return e.Type == QuestRuntimeEventType.PositionChanged && MatchesDistanceObjective(d);
            case "QuestActObjCompleteQuest":
                return e.Type == QuestRuntimeEventType.QuestCompleted && e.TemplateId == d.GetUInt32("quest_id");
            case "QuestActObjCompleteQuestGroup":
                return e.Type == QuestRuntimeEventType.QuestCompleted && QuestManager.Instance.CheckGroupQuest(d.GetUInt32("quest_context_group_id"), e.TemplateId);
            case "QuestActObjAggro":
                return e.Type == QuestRuntimeEventType.Aggro;
            case "QuestActObjCinema":
                return e.Type == QuestRuntimeEventType.CinemaCompleted && e.TemplateId != 0 && e.TemplateId == d.GetUInt32("cinema_id");
            case "QuestActObjCondition":
                return e.Type == QuestRuntimeEventType.ConditionChanged && e.TemplateId == d.GetUInt32("condition_id");
            case "QuestActObjConquestWar":
                return e.Type == QuestRuntimeEventType.ConquestWarResult && e.ZoneId == d.GetUInt32("zone_group_id") && e.Rank <= d.GetInt32("complete_rank");
            case "QuestActObjFactionCompetition":
                return e.Type == QuestRuntimeEventType.FactionCompetitionResult && e.ZoneId == d.GetUInt32("zone_group_id") && e.Rank <= d.GetInt32("complete_rank");
            case "QuestActObjConsumeEvolvingMaterial":
                return e.Type == QuestRuntimeEventType.EvolvingMaterialConsumed;
            case "QuestActObjEffectFire":
                return e.Type == QuestRuntimeEventType.EffectFired && e.TemplateId == d.GetUInt32("effect_id");
            case "QuestActObjEnchantScaleCount":
                return e.Type == QuestRuntimeEventType.EnchantScaleChanged;
            case "QuestActObjExpressFire":
                return e.Type == QuestRuntimeEventType.ExpressFired && e.TemplateId == d.GetUInt32("express_key_id") &&
                       (d.GetUInt32("npc_group_id") == 0 || QuestManager.Instance.CheckGroupNpc(d.GetUInt32("npc_group_id"), e.SecondaryId));
            case "QuestActObjGainExpPoint":
                return e.Type == QuestRuntimeEventType.ExperienceGained;
            case "QuestActObjGainHonorPoint":
                return e.Type == QuestRuntimeEventType.HonorGained;
            case "QuestActObjGainLivingPoint":
                return e.Type == QuestRuntimeEventType.VocationGained;
            case "QuestActObjInviteTeamFaction":
            {
                if (e.Type != QuestRuntimeEventType.TeamInvite)
                    return false;
                var target = WorldManager.Instance.GetCharacterByObjId(e.TargetObjectId);
                var requiredBuff = d.GetUInt32("buff_id");
                return target != null && (requiredBuff == 0 || target.Buffs.CheckBuff(requiredBuff));
            }
            case "QuestActObjLaborPower":
                return e.Type == QuestRuntimeEventType.LaborSpent &&
                       (d.GetUInt32("actability_group_id") == 0 || e.GroupId == d.GetUInt32("actability_group_id"));
            case "QuestActObjSendMail":
                return e.Type == QuestRuntimeEventType.MailSent && MatchesMailAttachments(d, e);
            case "QuestActObjSellBackpackGood":
                return e.Type == QuestRuntimeEventType.BackpackSold &&
                       (d.GetUInt32("content_item_id") == 0 || e.TemplateId == d.GetUInt32("content_item_id"));
            case "QuestActEtcItemObtain":
                return e.Type == QuestRuntimeEventType.ItemGather && e.TemplateId == d.GetUInt32("item_id");
            default:
                return false;
        }
    }



    private static bool MatchesMailAttachments(QuestActDefinition d, QuestRuntimeEvent e)
    {
        var items = e.Items;
        for (var i = 1; i <= 3; i++)
        {
            var itemId = d.GetUInt32($"item{i}_id");
            var count = d.GetInt32($"count{i}");
            if (itemId == 0 || count <= 0)
                continue;
            if (items == null || !items.TryGetValue(itemId, out var actual) || actual < count)
                return false;
        }
        return true;
    }

    private bool MatchesDistanceObjective(QuestActDefinition d)
    {
        var npcId = d.GetUInt32("npc_id");
        var npc = npcId == 0 ? null : WorldManager.Instance.GetNpcByTemplateId(npcId);
        if (npc == null || Owner is not BaseUnit ownerUnit)
            return false;

        // Dedicated data stores this legacy objective distance as a squared
        // centimetre value. Convert it to the world-unit distance used by BaseUnit.
        var storedDistance = Math.Max(0L, d.GetInt64("distance"));
        var worldDistance = storedDistance > 10000
            ? Math.Sqrt(storedDistance) / 100.0
            : storedDistance;
        var actualDistance = MathUtil.CalculateDistance(ownerUnit, npc);
        return d.GetBoolean("within")
            ? actualDistance <= worldDistance
            : actualDistance >= worldDistance;
    }

    private static bool MatchNpcKillRestrictions(QuestActDefinition d, QuestRuntimeEvent e)
    {
        if (d.GetInt32("level_min") > 0 && e.Level < d.GetInt32("level_min"))
            return false;
        if (d.GetInt32("level_max") > 0 && e.Level > d.GetInt32("level_max"))
            return false;
        if (d.GetBoolean("is_party") && !e.IsParty)
            return false;
        return true;
    }

    private static bool MatchInteraction(QuestActDefinition d, QuestRuntimeEvent e)
    {
        var doodadId = d.GetUInt32("doodad_id");
        if (doodadId != 0 && e.TemplateId != doodadId)
            return false;
        var wi = d.GetUInt32("wi_id");
        if (wi != 0 && e.SecondaryId != wi)
            return false;
        var phase = d.GetInt32("phase");
        return phase == 0 || e.Value == phase;
    }

    private bool IsDoodadInRequiredPhase(QuestActDefinition d)
    {
        Doodad doodad = null;
        if (_reportSourceObjectId != 0)
            doodad = WorldManager.Instance.GetDoodad(_reportSourceObjectId);
        if (doodad == null && _acceptedSourceObjectId != 0)
            doodad = WorldManager.Instance.GetDoodad(_acceptedSourceObjectId);
        if (doodad == null || doodad.TemplateId != d.GetUInt32("doodad_id"))
            return false;
        var phase1 = d.GetUInt32("phase1");
        var phase2 = d.GetUInt32("phase2");
        return doodad.FuncGroupId == phase1 || doodad.FuncGroupId == phase2;
    }

    private int GetInventoryCount(uint itemId, int grade)
    {
        if (itemId == 0)
            return 0;
        Owner.Inventory.GetAllItemsByTemplate(new[] { SlotType.Inventory }, itemId, grade, out _, out var count);
        return count;
    }

    private int GetAbilityLevel(uint abilityId)
    {
        if (Owner is not Character character)
            return 0;
        if (abilityId == 0)
            return character.Abilities.Values.Select(x => ExperienceManager.Instance.GetLevelFromExp(x.Exp)).DefaultIfEmpty((byte)0).Max();
        var key = (AbilityType)abilityId;
        return character.Abilities.Abilities.TryGetValue(key, out var ability)
            ? ExperienceManager.Instance.GetLevelFromExp(ability.Exp)
            : 0;
    }

    public PacketStream WriteTargetQuestContext(PacketStream stream)
    {
        RebuildClientObjectives();
        stream.Write(Id);
        stream.Write(TemplateId);
        stream.Write((byte)Status);
        QuestPacketCodec.WriteSignedPisc(stream, Objectives);
        stream.Write(false);
        stream.WriteBc(QuestAcceptorType == QuestAcceptorType.Npc ? _acceptedSourceObjectId : 0);
        stream.WriteBc(QuestAcceptorType == QuestAcceptorType.Doodad ? _acceptedSourceObjectId : 0);
        stream.Write(_acceptedSourceTemplateId);
        stream.WriteBc(_reportSourceObjectId);
        stream.Write(LeftTime);
        stream.Write(CurrentComponentId);
        stream.Write(DoodadId);
        stream.Write(_acceptedAt);
        stream.Write((byte)QuestAcceptorType);
        stream.Write(AcceptorType);
        return stream;
    }

    public byte[] WriteRuntimeData()
    {
        lock (_runtimeLock)
        {
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory);
            writer.Write(RuntimeBlobMagic);
            writer.Write(RuntimeBlobVersion);
            writer.Write(CurrentComponentId);
            writer.Write(_objectiveComponentId);
            writer.Write((byte)Step);
            writer.Write((byte)Status);
            writer.Write((byte)QuestAcceptorType);
            writer.Write(AcceptorType);
            writer.Write(_acceptedSourceObjectId);
            writer.Write(_acceptedSourceTemplateId);
            writer.Write(_reportSourceObjectId);
            writer.Write(_reportSourceTemplateId);
            writer.Write(_acceptedAt.Ticks);
            writer.Write(Time.Ticks);
            writer.Write(_runtimeScore);
            writer.Write(EarlyCompletion);
            writer.Write(_runtimeActProgress.Count);
            foreach (var pair in _runtimeActProgress.OrderBy(x => x.Key))
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
            writer.Write(_runtimeCompletedComponents.Count);
            foreach (var id in _runtimeCompletedComponents.OrderBy(x => x))
                writer.Write(id);
            writer.Flush();
            return memory.ToArray();
        }
    }

    public void ReadRuntimeData(byte[] data)
    {
        if (data == null || data.Length == 0)
            return;
        lock (_runtimeLock)
        {
            if (data.Length >= 5 && BitConverter.ToUInt32(data, 0) == RuntimeBlobMagic)
            {
                using var memory = new MemoryStream(data, false);
                using var reader = new BinaryReader(memory);
                _ = reader.ReadUInt32();
                var version = reader.ReadByte();
                if (version > RuntimeBlobVersion)
                    throw new InvalidDataException($"Unsupported quest runtime blob version {version}");
                CurrentComponentId = reader.ReadUInt32();
                ComponentId = CurrentComponentId;
                _objectiveComponentId = reader.ReadUInt32();
                Step = (QuestComponentKind)reader.ReadByte();
                Status = (QuestStatus)reader.ReadByte();
                QuestAcceptorType = (QuestAcceptorType)reader.ReadByte();
                AcceptorType = reader.ReadUInt32();
                _acceptedSourceObjectId = reader.ReadUInt32();
                _acceptedSourceTemplateId = reader.ReadUInt32();
                _reportSourceObjectId = reader.ReadUInt32();
                _reportSourceTemplateId = reader.ReadUInt32();
                _acceptedAt = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                Time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                _runtimeScore = reader.ReadInt32();
                EarlyCompletion = reader.ReadBoolean();
                _runtimeActProgress.Clear();
                var progressCount = Math.Clamp(reader.ReadInt32(), 0, 100000);
                for (var i = 0; i < progressCount; i++)
                    _runtimeActProgress[reader.ReadUInt32()] = reader.ReadInt32();
                _runtimeCompletedComponents.Clear();
                var completedCount = Math.Clamp(reader.ReadInt32(), 0, 10000);
                for (var i = 0; i < completedCount; i++)
                    _runtimeCompletedComponents.Add(reader.ReadUInt32());
                RebuildClientObjectives();
                return;
            }

            ReadLegacyRuntimeData(data);
        }
    }

    private void ReadLegacyRuntimeData(byte[] data)
    {
        var stream = new PacketStream(data);
        var legacy = new int[5];
        for (var i = 0; i < legacy.Length; i++)
            legacy[i] = stream.ReadInt32();
        Step = (QuestComponentKind)stream.ReadByte();
        QuestAcceptorType = (QuestAcceptorType)stream.ReadByte();
        CurrentComponentId = stream.ReadUInt32();
        ComponentId = CurrentComponentId;
        AcceptorType = stream.ReadUInt32();
        Time = stream.ReadDateTime();
        _acceptedAt = DateTime.UtcNow;
        _acceptedSourceTemplateId = AcceptorType;

        if (Template != null)
        {
            var component = Template.Components.TryGetValue(CurrentComponentId, out var current)
                ? current
                : Template.GetComponents(QuestComponentKind.Progress).OrderBy(x => x.Id).FirstOrDefault();
            if (component != null)
            {
                _objectiveComponentId = component.Id;
                var acts = component.Acts.OfType<QuestAct>().Where(IsObjectiveAct).OrderBy(x => x.Id).ToArray();
                for (var i = 0; i < acts.Length && i < legacy.Length; i++)
                    _runtimeActProgress[acts[i].Id] = legacy[i];
            }
        }
        RebuildClientObjectives();
    }
}

internal static class QuestPacketCodec
{
    /// <summary>Writes target signed PISC blocks. Negative values always use four bytes.</summary>
    public static void WriteSignedPisc(PacketStream stream, IReadOnlyList<int> values)
    {
        for (var offset = 0; offset < values.Count; offset += 4)
        {
            var count = Math.Min(4, values.Count - offset);
            byte selector = 0;
            var payload = new PacketStream();
            for (var i = 0; i < count; i++)
            {
                var value = values[offset + i];
                var width = value < 0 ? 4 : value switch
                {
                    <= byte.MaxValue => 1,
                    <= ushort.MaxValue => 2,
                    <= 0x00FF_FFFF => 3,
                    _ => 4
                };
                selector |= (byte)((width - 1) << (i * 2));
                switch (width)
                {
                    case 1: payload.Write((byte)value); break;
                    case 2: payload.Write((ushort)value); break;
                    case 3: payload.WriteBc((uint)value); break;
                    default: payload.Write(value); break;
                }
            }
            stream.Write(selector);
            stream.Write(payload, false);
        }
    }
}
