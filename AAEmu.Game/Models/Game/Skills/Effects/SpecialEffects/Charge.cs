using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// SpecialEffect linked with adding charges to a buff.
/// </summary>
public class Charge : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Charge;

    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
        int buffId, int minCharge, int maxCharge, int unused)
    {
        var owner = target ?? caster;
        if (owner == null || caster == null || buffId <= 0)
            return;

        var template = SkillManager.Instance.GetBuffTemplate((uint)buffId);
        if (template == null)
        {
            Logger.Warn("Special effects: Charge missing buff template {0}", buffId);
            return;
        }

        Logger.Debug("Special effects: Charge buffId={0}, min={1}, max={2}, owner={3}",
            buffId, minCharge, maxCharge, owner.ObjId);

        lock (owner.ChargeLock)
        {
            var oldBuff = owner.Buffs.GetEffectFromBuffId((uint)buffId);
            var chargeDelta = NextInclusive(minCharge, maxCharge);
            var oldCharge = oldBuff?.Charge ?? 0;
            var effectiveMax = template.MaxCharge > 0
                ? template.MaxCharge
                : template.InitMaxCharge > 0
                    ? template.InitMaxCharge
                    : int.MaxValue;
            var newCharge = (int)Math.Min((long)oldCharge + chargeDelta, effectiveMax);

            var newBuff = new Buff(owner, caster, casterObj, template, skill, time)
            {
                Charge = newCharge
            };
            owner.Buffs.AddBuff(newBuff, oldBuff?.Index ?? 0);
        }
    }

    private static int NextInclusive(int minimum, int maximum)
    {
        if (maximum < minimum)
            (minimum, maximum) = (maximum, minimum);
        if (minimum == maximum)
            return minimum;
        return maximum == int.MaxValue
            ? Rand.Next(minimum, maximum)
            : Rand.Next(minimum, maximum + 1);
    }
}
