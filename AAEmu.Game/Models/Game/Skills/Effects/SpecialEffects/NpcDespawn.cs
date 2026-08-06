using System;

using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class NpcDespawn : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target,
        SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject,
        DateTime time, int value1, int value2, int value3, int value4)
    {
        if (target is not Npc npc)
            return;

        Logger.Debug("Special effects: NpcDespawn target={0}, template={1}", npc.ObjId, npc.TemplateId);
        npc.Spawner?.DoDespawn(npc);
    }
}
