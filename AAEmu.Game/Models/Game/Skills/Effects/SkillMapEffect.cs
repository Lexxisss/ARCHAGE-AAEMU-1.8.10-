using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Target-client map marker plot effect. Rendering is client-side after SCPlotEventPacket.
/// </summary>
public sealed class SkillMapEffect : EffectTemplate
{
    public int Radius { get; set; }
    public string TextureColorKey { get; set; }
    public string TextureKey { get; set; }
    public string TexturePath { get; set; }
    public bool UseFactionColor { get; set; }
    public bool UseUiEffect { get; set; }
    public int ViewTime { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace(
            "SkillMapEffect: id={0}, skill={1}, target={2}, radius={3}, viewTime={4}, texture={5}",
            Id,
            source?.Skill?.Template?.Id ?? 0,
            target?.ObjId ?? 0,
            Radius,
            ViewTime,
            TexturePath);
    }
}
