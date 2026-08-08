using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncQuest : DoodadFuncTemplate
{
    public uint QuestKindId { get; set; }
    public uint QuestId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Offer(caster, owner, skillId);
    }

    /// <summary>
    /// Reacts to whoever clicked, if this is the reaction their quest log calls for.
    /// </summary>
    /// <remarks>
    /// A phase carries every quest reaction the doodad has - hand this one out, take that one
    /// back - and only one of them can be the answer for a given player. Saying which one
    /// answered lets the caller stop asking the rest.
    /// </remarks>
    /// <returns>Whether this reaction was the one, and something was sent to the client.</returns>
    public bool Offer(BaseUnit caster, Doodad owner, uint skillId)
    {
        Logger.Trace("DoodadFuncQuest: skill={0}, kind={1}, quest={2}, doodad={3}:{4}",
            skillId, QuestKindId, QuestId, owner.TemplateId, owner.ObjId);

        if (caster is not Character character)
            return false;

        var hasQuest = character.Quests.ActiveQuests.TryGetValue(QuestId, out var quest);

        // quest_kind_id is defined by doodad_quest_kinds: 1=give, 2=complete.
        if (QuestKindId == 1)
        {
            // Handing one out again once it has been finished would be the obvious way to get
            // this wrong: a giver whose chain is done would keep offering its first quest.
            if (hasQuest || AlreadyFinished(character))
                return false;

            // The client answers with CSStartQuestContext and supplies the actual doodad object id.
            character.SendPacket(new SCDoodadAcceptQuestPacket(owner.ObjId, QuestId));
            return true;
        }

        if (QuestKindId == 2)
        {
            if (!hasQuest)
                return false;

            quest.SetReportSource(owner.ObjId, owner.TemplateId);
            if (quest.Status != QuestStatus.Ready)
            {
                // Carrying it but not done with it: this reaction has nothing to show, and the
                // doodad is free to go on to whatever else it offers.
                return false;
            }

            // Opens the target reward/turn-in UI. The actual commit is CSCompleteQuestContext.
            character.SendPacket(new SCDoodadCompleteQuestPacket(owner.ObjId, QuestId));
            return true;
        }

        // Unknown/custom quest kind: keep interaction objectives functional, but never invent accept/report semantics.
        if (!hasQuest)
            return false;

        character.Quests.DispatchRuntimeEvent(new QuestRuntimeEvent
        {
            Type = QuestRuntimeEventType.DoodadInteraction,
            SourceObjectId = character.ObjId,
            TargetObjectId = owner.ObjId,
            TemplateId = owner.TemplateId,
            SecondaryId = skillId,
            Value = (int)owner.FuncGroupId,
            Count = 1
        });

        // The objective was fed, but nothing was put in front of the player, so the rest of the
        // phase still gets its turn.
        return false;
    }

    /// <summary>Whether this quest is done with, repeats aside.</summary>
    private bool AlreadyFinished(Character character)
    {
        if (!character.Quests.HasQuestCompleted(QuestId))
            return false;

        return QuestManager.Instance.GetTemplate(QuestId)?.Repeatable != true;
    }
}
