using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Clears per-event hit history so following HitOnce selectors may acquire the same units again.
/// </summary>
public sealed class TargetHistoryClearEffect : EffectTemplate
{
    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var state = source?.Skill?.ActivePlotState ?? (caster as Unit)?.ActivePlotState;
        if (state == null)
        {
            Logger.Warn("TargetHistoryClearEffect {0}: no active plot state, skill={1}, caster={2}",
                Id,
                source?.Skill?.Template?.Id ?? 0,
                caster?.ObjId ?? 0);
            return;
        }

        var count = state.HitObjects.Count;
        state.HitObjects.Clear();
        Logger.Trace("TargetHistoryClearEffect: id={0}, cleared={1}, skill={2}",
            Id,
            count,
            source?.Skill?.Template?.Id ?? 0);
    }
}
