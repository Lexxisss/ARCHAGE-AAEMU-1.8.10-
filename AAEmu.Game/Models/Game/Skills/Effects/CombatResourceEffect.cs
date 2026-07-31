using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public sealed class CombatResourceEffect : EffectTemplate
{
    public int Chance { get; set; }
    public uint CombatResourceId { get; set; }
    public int MinCombatResource { get; set; }
    public int MaxCombatResource { get; set; }
    public bool ResetRemainTime { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Unit targetUnit)
            return;

        // In this table zero means unconditional; positive values are percentages.
        if (Chance > 0 && Rand.Next(1, 101) > Chance)
            return;

        var resourceId = CombatResourceId;
        if (resourceId == 0 && source?.Skill?.Template != null)
            resourceId = SkillManager.Instance.ResolveCombatResourceId(source.Skill.Template);
        if (resourceId == 0)
            return;

        var min = Math.Min(MinCombatResource, MaxCombatResource);
        var max = Math.Max(MinCombatResource, MaxCombatResource);
        var amount = min == max ? min : Rand.Next(min, max + 1);
        targetUnit.AddCombatResource(resourceId, amount, ResetRemainTime);
    }
}
