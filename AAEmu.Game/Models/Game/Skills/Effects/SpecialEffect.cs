using System;
using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpecialEffect : EffectTemplate
{
    private static readonly ConcurrentDictionary<SpecialType, Type> ActionTypes = new();
    private static readonly ConcurrentDictionary<SpecialType, byte> MissingActionTypes = new();

    public SpecialType SpecialEffectTypeId { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
    public int Value3 { get; set; }
    public int Value4 { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (source == null)
            return;

        Logger.ConditionalTrace(
            "SpecialEffect: id={0}, type={1}, values=[{2},{3},{4},{5}], skill={6}, caster={7}, target={8}",
            Id,
            SpecialEffectTypeId,
            Value1,
            Value2,
            Value3,
            Value4,
            source.Skill?.Template?.Id ?? 0,
            caster?.ObjId ?? 0,
            target?.ObjId ?? 0);

        var action = CreateAction();
        if (action == null)
            return;

        action.Execute(caster, casterObj, target, targetObj, castObj, source.Skill, skillObject, time,
            Value1, Value2, Value3, Value4);

        var repeatCount = Math.Max(source.Skill?.Template?.EffectRepeatCount ?? 1, 1);
        var repeatTick = Math.Max(source.Skill?.Template?.EffectRepeatTick ?? 0, 0);
        for (var index = 1; index < repeatCount; index++)
        {
            var repeatAction = CreateAction();
            if (repeatAction == null)
                break;

            TaskManager.Instance.Schedule(
                new SpecialEffectRepeatTask(repeatAction, caster, casterObj, target, targetObj, castObj,
                    source.Skill, skillObject, time, Value1, Value2, Value3, Value4),
                TimeSpan.FromMilliseconds((long)repeatTick * index));
        }
    }

    private SpecialEffectAction CreateAction()
    {
        if (!ActionTypes.TryGetValue(SpecialEffectTypeId, out var actionType))
        {
            actionType = typeof(SpecialEffect).Assembly.GetType(
                "AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects." + SpecialEffectTypeId);
            if (actionType != null)
                ActionTypes.TryAdd(SpecialEffectTypeId, actionType);
        }

        if (actionType == null || !typeof(SpecialEffectAction).IsAssignableFrom(actionType))
        {
            if (MissingActionTypes.TryAdd(SpecialEffectTypeId, 0))
                Logger.Warn(
                    "Unsupported special effect action: effect={0}, type={1}, values=[{2},{3},{4},{5}]",
                    Id,
                    SpecialEffectTypeId,
                    Value1,
                    Value2,
                    Value3,
                    Value4);
            return null;
        }

        return Activator.CreateInstance(actionType) as SpecialEffectAction;
    }
}
