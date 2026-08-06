using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CombatText : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target,
        SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject,
        DateTime time, int value1, int value2, int value3, int value4)
    {
        if (caster == null || target == null)
            return;

        var textType = (byte)Math.Clamp(value1, byte.MinValue, byte.MaxValue);
        caster.BroadcastPacket(new SCCombatTextPacket(caster.ObjId, target.ObjId, textType), true);
    }
}
