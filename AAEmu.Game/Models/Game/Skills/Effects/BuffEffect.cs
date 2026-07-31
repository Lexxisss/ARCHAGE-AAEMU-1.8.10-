using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class BuffEffect : EffectTemplate
{
    public int Chance { get; set; }
    public int InitialStackCount { get; set; }
    public int SourceAbilityLevelOverride { get; set; }
    public BuffTemplate Buff { get; set; }
    public override uint BuffId => Buff.Id;
    public override bool OnActionTime => Buff.Tick > 0;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is Unit trg)
        {
            var hitType = SkillHitType.Invalid;
            if ((source.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false)
                && (source.Skill?.SkillMissed(trg.ObjId) ?? false))
            {
                return;
            }
        }
        if (Rand.Next(0, 101) > Chance)
        {
            ((Unit)caster).ConditionChance = false;
            return;
        }
        else
        {
            ((Unit)caster).ConditionChance = true;
        }

        if (Buff.RequireBuffId > 0 && !target.Buffs.CheckBuff(Buff.RequireBuffId))
            return; // TODO send error?
        if (target.Buffs.CheckBuffImmune(Buff.Id))
            return; // TODO send error of immune?

        ushort sourceAbilityLevel = SourceAbilityLevelOverride > 0
            ? checked((ushort)SourceAbilityLevelOverride)
            : (ushort)1;
        if (SourceAbilityLevelOverride <= 0 && source?.SourceBuff != null)
        {
            sourceAbilityLevel = source.SourceBuff.SourceAbilityLevel;
        }
        else if (SourceAbilityLevelOverride <= 0 && caster is Character character)
        {
            if (source.Skill != null)
            {
                var template = source.Skill.Template;
                var abilityLevel = character.GetAbLevel((AbilityType)source.Skill.Template.AbilityId);
                if (template.LevelStep != 0)
                    sourceAbilityLevel = (ushort)((abilityLevel / template.LevelStep) * template.LevelStep);
                else
                    sourceAbilityLevel = (ushort)template.AbilityLevel;

                //Dont allow lower than minimum ablevel for skill or infinite debuffs can happen
                sourceAbilityLevel = (ushort)Math.Max(template.AbilityLevel, sourceAbilityLevel);
            }
            else if (source.Buff != null)
            {
                //not sure?
            }
        }
        else if (SourceAbilityLevelOverride <= 0 && source?.Skill != null)
        {
            sourceAbilityLevel = (ushort)Math.Max(1, source.Skill.Template.AbilityLevel);
        }

        // TODO Doesn't let the quest work Id=2488 "A Mother's Tale", 13, "Lilyut Hills", "Nuian Main"
        // Safeguard to prevent accidental flagging
        if (Buff.Kind == BuffKind.Bad && !caster.CanAttack(target) && caster != target)
            return;

        var originatingSkill = source?.Skill ?? source?.SourceBuff?.Skill;
        target.Buffs.AddBuff(new Buff(target, caster, casterObj, Buff, originatingSkill, time)
        {
            SourceLevel = source?.SourceBuff?.SourceLevel ?? (caster as Unit)?.Level ?? (byte)0,
            SourceAbilityLevel = sourceAbilityLevel,
            StackCount = Math.Max(1, InitialStackCount)
        });

        if (Buff.Kind == BuffKind.Bad && caster.GetRelationStateTo(target) == RelationState.Friendly
            && caster != target && !target.Buffs.CheckBuff((uint)BuffConstants.Retribution))
        {
            ((Unit)caster).SetCriminalState(true);
        }
    }
}
