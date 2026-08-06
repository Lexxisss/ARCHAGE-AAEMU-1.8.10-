using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.World;

public class AreaTrigger
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public AreaShape Shape { get; set; }
    public Doodad Owner { get; set; }
    public Unit Caster { get; set; }

    /// <summary>
    /// A world-spawned doodad normally has no Unit caster. For neutral clouts whose target relation
    /// is Any/Others, use every entered unit as the technical buff source. This preserves the
    /// server buff lifecycle without inventing a fake unit and is required by permanent world
    /// auras such as the Nui Peace Zone.
    /// </summary>
    public bool AllowCasterlessTargets { get; set; }

    /// <summary>
    /// Units currently inside the shape.
    /// </summary>
    private List<Unit> Units { get; set; }

    /// <summary>
    /// Buff instances created by this trigger. Keeping the exact instance prevents one overlapping
    /// aura from removing a same-id buff that belongs to another area trigger.
    /// </summary>
    private Dictionary<uint, Buff> AppliedBuffs { get; }

    public uint SkillId { get; set; }
    public uint TlId { get; set; }
    public SkillTargetRelation TargetRelation { get; set; }
    public BuffTemplate InsideBuffTemplate { get; set; }
    public List<EffectTemplate> EffectPerTick { get; set; }
    public int TickRate { get; set; }
    private DateTime _lastTick = DateTime.MinValue;

    public AreaTrigger()
    {
        Units = new List<Unit>();
        AppliedBuffs = new Dictionary<uint, Buff>();
        EffectPerTick = new List<EffectTemplate>();
    }

    private void UpdateUnits()
    {
        if (!Owner?.IsVisible ?? true)
        {
            AreaTriggerManager.Instance.RemoveAreaTrigger(this);
            return;
        }

        var currentUnitsInShape = WorldManager.GetAroundByShape<Unit>(Owner, Shape) ?? new List<Unit>();

        // An empty area is a valid state. The old implementation removed the permanent trigger on
        // its first empty tick, so world auras disappeared before a player ever approached them.
        var leftUnits = Units
            .Where(oldUnit => currentUnitsInShape.All(newUnit => oldUnit.ObjId != newUnit.ObjId))
            .ToList();
        var newUnits = currentUnitsInShape
            .Where(newUnit => Units.All(oldUnit => newUnit.ObjId != oldUnit.ObjId))
            .ToList();

        foreach (var newUnit in newUnits)
            OnEnter(newUnit);

        foreach (var leftUnit in leftUnits)
            OnLeave(leftUnit);

        Units = currentUnitsInShape;
    }

    private bool IsRelationValid(Unit unit)
    {
        if (Caster != null)
            return SkillTargetingUtil.IsRelationValid(TargetRelation, Caster, unit);

        if (!AllowCasterlessTargets)
            return false;

        // Without a Unit source only source-independent relations are well-defined. In target data
        // Nui uses Others, meaning every unit other than the absent origin source.
        return TargetRelation is SkillTargetRelation.Any or SkillTargetRelation.Others;
    }

    private Unit GetEffectiveCaster(Unit unit)
    {
        if (Caster != null)
            return Caster;

        return AllowCasterlessTargets ? unit : null;
    }

    private void OnEnter(Unit unit)
    {
        if (unit == null || !IsRelationValid(unit))
            return;

        var effectiveCaster = GetEffectiveCaster(unit);
        if (effectiveCaster == null)
            return;

        if (InsideBuffTemplate != null)
        {
            if (InsideBuffTemplate.RequireBuffId > 0 && !unit.Buffs.CheckBuff(InsideBuffTemplate.RequireBuffId))
                return;
            if (unit.Buffs.CheckBuffImmune(InsideBuffTemplate.BuffId))
                return;

            var buff = new Buff(
                unit,
                effectiveCaster,
                new SkillCasterUnit(effectiveCaster.ObjId),
                InsideBuffTemplate,
                null,
                DateTime.UtcNow);

            unit.Buffs.AddBuff(buff);

            // AddBuff can reject a refresh before attaching the object. Track only an active buff
            // that was actually accepted by this trigger.
            if (buff.InUse || buff.State == EffectState.Acting)
            {
                AppliedBuffs[unit.ObjId] = buff;
                Logger.Debug(
                    "AreaTrigger enter: owner={0}/{1}, unit={2}, buff={3}, casterless={4}",
                    Owner?.TemplateId ?? 0,
                    Owner?.ObjId ?? 0,
                    unit.ObjId,
                    InsideBuffTemplate.BuffId,
                    Caster == null);
            }
        }
    }

    private void OnLeave(Unit unit)
    {
        if (unit == null)
            return;

        if (AppliedBuffs.TryGetValue(unit.ObjId, out var buff))
        {
            AppliedBuffs.Remove(unit.ObjId);
            buff?.Exit();

            Logger.Debug(
                "AreaTrigger leave: owner={0}/{1}, unit={2}, buff={3}",
                Owner?.TemplateId ?? 0,
                Owner?.ObjId ?? 0,
                unit.ObjId,
                buff?.Template?.BuffId ?? 0);
        }
    }

    public void OnDelete()
    {
        foreach (var buff in AppliedBuffs.Values.ToList())
            buff?.Exit();

        AppliedBuffs.Clear();
        Units.Clear();
    }

    private IEnumerable<Unit> GetEffectTargets()
    {
        if (Caster != null)
            return SkillTargetingUtil.FilterWithRelation(TargetRelation, Caster, Units);

        if (AllowCasterlessTargets &&
            TargetRelation is SkillTargetRelation.Any or SkillTargetRelation.Others)
            return Units;

        return Enumerable.Empty<Unit>();
    }

    private void ApplyEffects()
    {
        if (InsideBuffTemplate == null || EffectPerTick.Count == 0)
            return;

        foreach (var unit in GetEffectTargets())
        {
            var effectiveCaster = GetEffectiveCaster(unit);
            if (effectiveCaster == null)
                continue;

            foreach (var effect in EffectPerTick)
            {
                if (effect == null)
                    continue;
                if (effect is BuffEffect buffEffect && unit.Buffs.CheckBuff(buffEffect.BuffId))
                    continue;

                var sourceBuff = AppliedBuffs.TryGetValue(unit.ObjId, out var ownedBuff)
                    ? ownedBuff
                    : unit.Buffs.GetEffectFromBuffId(InsideBuffTemplate.BuffId);
                CastAction castAction = sourceBuff != null
                    ? new CastBuff(sourceBuff)
                    : new CastSkill(SkillId, 0);

                effect.Apply(
                    effectiveCaster,
                    new SkillCasterUnit(effectiveCaster.ObjId),
                    unit,
                    new SkillCastUnitTarget(unit.ObjId),
                    castAction,
                    new EffectSource(),
                    new SkillObject(),
                    DateTime.UtcNow);
            }
        }
    }

    // Called by AreaTriggerManager every 200 ms.
    public void Tick(TimeSpan delta)
    {
        UpdateUnits();
        if (TickRate > 0 && (DateTime.UtcNow - _lastTick).TotalMilliseconds > TickRate)
        {
            ApplyEffects();
            _lastTick = DateTime.UtcNow;
        }
    }
}
