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
        Logger.Trace("DoodadFuncQuest: skill={0}, kind={1}, quest={2}, doodad={3}:{4}",
            skillId, QuestKindId, QuestId, owner.TemplateId, owner.ObjId);

        if (caster is not Character character)
            return;

        var hasQuest = character.Quests.ActiveQuests.TryGetValue(QuestId, out var quest);

        // quest_kind_id is defined by doodad_quest_kinds: 1=give, 2=complete.
        if (QuestKindId == 1)
        {
            if (!hasQuest)
            {
                // The client answers with CSStartQuestContext and supplies the actual doodad object id.
                character.SendPacket(new SCDoodadAcceptQuestPacket(owner.ObjId, QuestId));
            }
            return;
        }

        if (QuestKindId == 2)
        {
            if (!hasQuest)
                return;

            quest.SetReportSource(owner.ObjId, owner.TemplateId);
            if (quest.Status == QuestStatus.Ready)
            {
                // Opens the target reward/turn-in UI. The actual commit is CSCompleteQuestContext.
                character.SendPacket(new SCDoodadCompleteQuestPacket(owner.ObjId, QuestId));
            }
            return;
        }

        // Unknown/custom quest kind: keep interaction objectives functional, but never invent accept/report semantics.
        if (!hasQuest)
            return;

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
    }
}
