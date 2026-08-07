using System;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FxGroupAnim : SpecialEffectAction
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
        // No standalone visual packet belongs here. Target x2game.dll 0x39638450 resolves
        // FxGroupAnim from the local special_effects row selected by SCPlotEvent.eventId.
        // These value1..value4 fields are therefore not serialized by this action. PlotNode
        // supplies the event id, source/target PlotObjects and targetUnitIds in SCPlotEventPacket.
    }
}
