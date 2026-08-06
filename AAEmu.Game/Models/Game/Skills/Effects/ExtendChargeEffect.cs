using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Extends the numeric charge stored in a buff. The target database uses this for
/// shields, absorb lenses and other effects whose strength is calculated at cast time.
/// </summary>
public sealed class ExtendChargeEffect : EffectTemplate
{
    public uint ChargeBuffId { get; set; }
    public DamageType DamageType { get; set; }
    public float DpsIncMultiplier { get; set; }
    public float DpsMultiplier { get; set; }
    public int FixedMax { get; set; }
    public int FixedMin { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaEnd { get; set; }
    public int LevelVaStart { get; set; }
    public uint PercentDamageResourceTypeId { get; set; }
    public int PercentMax { get; set; }
    public int PercentMin { get; set; }
    public bool UseDpsCharge { get; set; }
    public bool UseFixedCharge { get; set; }
    public bool UseLevelCharge { get; set; }
    public bool UseMainhandWeapon { get; set; }
    public bool UseOffhandWeapon { get; set; }
    public bool UsePercentCharge { get; set; }
    public bool UseRangedWeapon { get; set; }
    public bool UseSourceHealth { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var owner = target ?? caster;
        if (owner == null || caster == null || ChargeBuffId == 0)
            return;

        var buffTemplate = SkillManager.Instance.GetBuffTemplate(ChargeBuffId);
        if (buffTemplate == null)
        {
            Logger.Warn("ExtendChargeEffect {0}: missing charge buff {1}", Id, ChargeBuffId);
            return;
        }

        var sourceUnit = caster as Unit;
        var targetUnit = owner as Unit;
        var chargeDelta = CalculateCharge(sourceUnit, targetUnit, source?.Skill);
        if (chargeDelta <= 0)
            return;

        lock (owner.ChargeLock)
        {
            var current = owner.Buffs.GetEffectFromBuffId(ChargeBuffId);
            var oldCharge = current?.Charge ?? 0;
            var effectiveMax = buffTemplate.MaxCharge > 0
                ? buffTemplate.MaxCharge
                : buffTemplate.InitMaxCharge > 0
                    ? buffTemplate.InitMaxCharge
                    : int.MaxValue;
            var newCharge = (int)Math.Min((long)oldCharge + chargeDelta, effectiveMax);

            var buff = new Buff(owner, caster, casterObj, buffTemplate, source?.Skill, time)
            {
                Charge = newCharge
            };
            owner.Buffs.AddBuff(buff, current?.Index ?? 0);

            Logger.Debug(
                "ExtendChargeEffect: id={0}, buff={1}, owner={2}, old={3}, delta={4}, new={5}, max={6}",
                Id,
                ChargeBuffId,
                owner.ObjId,
                oldCharge,
                chargeDelta,
                newCharge,
                effectiveMax);
        }
    }

    private int CalculateCharge(Unit source, Unit target, Skill skill)
    {
        double amount = 0;

        if (UseFixedCharge)
            amount += NextInclusive(FixedMin, FixedMax);

        if (UseLevelCharge && source != null)
        {
            var level = Math.Max((int)(skill?.Level ?? source.Level), 1);
            double levelFactor = LevelVaStart;
            if (LevelVaEnd != LevelVaStart)
                levelFactor += (level - 1) / 49f * (LevelVaEnd - LevelVaStart);
            if (levelFactor == 0)
                levelFactor = 1;
            amount += source.LevelDps * LevelMd * levelFactor;
        }

        if (UseDpsCharge && source != null)
        {
            var dps = ResolveDps(source);
            var dpsInc = DamageType switch
            {
                DamageType.Magic or DamageType.Heal => source.MDpsInc,
                DamageType.Ranged => source.RangedDpsInc,
                _ => source.DpsInc
            };
            amount += dps * 0.001d * DpsMultiplier;
            amount += dpsInc * 0.001d * DpsIncMultiplier;
        }

        if (UsePercentCharge)
        {
            var resourceOwner = UseSourceHealth ? source : target ?? source;
            if (resourceOwner != null)
            {
                // Target data uses this field as a health/mana selector. Known values are
                // 2 (health) and 4 (mana); unknown values fail to health rather than zeroing
                // a shield that otherwise has a valid plot branch.
                var resource = PercentDamageResourceTypeId == 4
                    ? resourceOwner.Mp
                    : resourceOwner.Hp;
                amount += resource * NextInclusive(PercentMin, PercentMax) / 100d;
            }
        }

        if (double.IsNaN(amount) || double.IsInfinity(amount))
            return 0;
        return Math.Max(0, (int)Math.Round(amount));
    }

    private double ResolveDps(Unit source)
    {
        if (UseRangedWeapon || DamageType == DamageType.Ranged)
            return source.RangedDps;
        if (DamageType is DamageType.Magic or DamageType.Heal)
            return source.MDps;
        if (UseOffhandWeapon && !UseMainhandWeapon)
            return source.OffhandDps;
        return source.Dps;
    }

    private static int NextInclusive(int minimum, int maximum)
    {
        if (maximum < minimum)
            (minimum, maximum) = (maximum, minimum);
        if (minimum == maximum)
            return minimum;
        if (maximum == int.MaxValue)
            return Rand.Next(minimum, maximum);
        return Rand.Next(minimum, maximum + 1);
    }
}
