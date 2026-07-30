using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Quests;

public enum QuestRuntimeEventType
{
    None,
    NpcKill,
    PcKill,
    Aggro,
    ItemGather,
    ItemUse,
    DoodadInteraction,
    DoodadPhaseChanged,
    TalkNpc,
    Craft,
    EnterSphere,
    ExitSphere,
    LevelChanged,
    AbilityLevelChanged,
    MateLevelChanged,
    PositionChanged,
    QuestCompleted,
    EffectFired,
    ExpressFired,
    LaborSpent,
    ExperienceGained,
    HonorGained,
    VocationGained,
    MailSent,
    CinemaCompleted,
    ConditionChanged,
    ConquestWarResult,
    FactionCompetitionResult,
    TeamInvite,
    EvolvingMaterialConsumed,
    EnchantScaleChanged,
    BackpackSold
}

public sealed class QuestRuntimeEvent
{
    public QuestRuntimeEventType Type { get; init; }
    public uint SourceObjectId { get; init; }
    public uint TargetObjectId { get; init; }
    public uint TemplateId { get; init; }
    public uint SecondaryId { get; init; }
    public uint GroupId { get; init; }
    public uint ZoneId { get; init; }
    public uint FactionId { get; init; }
    public int Count { get; init; } = 1;
    public int Value { get; init; }
    public int Level { get; init; }
    public int Grade { get; init; } = -1;
    public int Rank { get; init; }
    public bool IsParty { get; init; }
    public bool IsPlayer { get; init; }
    public IReadOnlyDictionary<uint, int> Items { get; init; }
}
