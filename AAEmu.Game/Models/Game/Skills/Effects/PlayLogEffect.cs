using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public sealed class PlayLogEffect : EffectTemplate
{
    public string Message { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info("PlayLogEffect: id={0}, skill={1}, caster={2}, target={3}, message={4}",
            Id,
            source?.Skill?.Template?.Id ?? 0,
            caster?.ObjId ?? 0,
            target?.ObjId ?? 0,
            Message);
    }
}
