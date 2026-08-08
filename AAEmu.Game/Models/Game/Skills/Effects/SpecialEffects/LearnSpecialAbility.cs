using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class LearnSpecialAbility : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        // TARGET/DEV client LearnSpecialAbility does not use SpecialEffect value1..4.
        // It casts hidden skill 33995 and passes the selected special ability in
        // SkillObject type 19 (Ability), whose payload is a u32 ability id.
        if (skillObject is not SkillObjectAbility abilityObject)
        {
            Logger.Warn("LearnSpecialAbility: skill {0} has no SkillObjectAbility", skill?.Template?.Id ?? 0);
            return;
        }

        if (abilityObject.Ability > byte.MaxValue)
        {
            Logger.Warn("LearnSpecialAbility: invalid ability id {0}", abilityObject.Ability);
            return;
        }

        var ability = (AbilityType)(byte)abilityObject.Ability;
        if (!character.Abilities.LearnSpecialAbility(ability))
        {
            Logger.Warn("LearnSpecialAbility: unsupported special ability {0}", abilityObject.Ability);
            return;
        }

        // TARGET opcode 0x03F, body u8 ability. Dedicated owns this S->C notification.
        character.SendPacket(new SCSpecialAbilityLearnedPacket(ability));
    }
}
