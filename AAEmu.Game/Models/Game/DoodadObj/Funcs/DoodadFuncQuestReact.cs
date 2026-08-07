using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Moves a doodad to another phase for someone who stands at a given point in a given quest.
/// </summary>
/// <remarks>
/// The unknown ore vein waits in a phase that holds nothing but this reaction; only the phase it
/// sends you to names "break the unknown ore vein". Until this existed the vein could not be
/// broken by anyone, and 3046 rows of doodad_func_quest_reacts did nothing at all.
///
/// The reaction is per-character in the original, whereas a doodad here has one phase for
/// everybody, so the phase moves for the world when a qualifying character interacts. That is how
/// DoodadFuncClimateReact already behaves, and the phase carries its own way back.
/// </remarks>
public class DoodadFuncQuestReact : DoodadPhaseFuncTemplate
{
    public uint QuestId { get; set; }
    public QuestStatus QuestStatusId { get; set; }
    public uint QuestComponentId { get; set; }
    public int NextPhase { get; set; }
    public bool BubbleOnce { get; set; }
    public uint BubbleId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncQuestReact QuestId {0}, status {1}, component {2}, nextPhase {3}",
            QuestId, QuestStatusId, QuestComponentId, NextPhase);

        // Nowhere to send anyone, or nobody with a quest log to ask - the spawn pass arrives with
        // no caster at all, and a vein must not open itself just because it was placed.
        if (NextPhase <= 0 || QuestId == 0 || caster is not Character character)
        {
            return false;
        }

        if (!MatchesQuestState(character))
        {
            return false; // not this character's reaction; let the next phase function have a go
        }

        Logger.Debug("DoodadFuncQuestReact: {0} is at quest {1} status {2}, moving doodad {3} to phase {4}",
            character.Name, QuestId, QuestStatusId, owner.TemplateId, NextPhase);

        owner.OverridePhase = NextPhase;
        return true;
    }

    private bool MatchesQuestState(Character character)
    {
        switch (QuestStatusId)
        {
            case QuestStatus.Completed:
            case QuestStatus.DailyCompleted:
                return character.Quests.HasQuestCompleted(QuestId);

            case QuestStatus.Invalid:
                // "Has not got there yet": neither carrying it nor finished with it.
                return !character.Quests.HasQuest(QuestId) && !character.Quests.HasQuestCompleted(QuestId);

            default:
                if (!character.Quests.ActiveQuests.TryGetValue(QuestId, out var quest) ||
                    quest.Status != QuestStatusId)
                {
                    return false;
                }

                // A component narrows the reaction to one step of the quest. Zero means the whole
                // quest, which is what all but 124 of the rows say.
                return QuestComponentId == 0 || quest.ComponentId == QuestComponentId;
        }
    }
}
