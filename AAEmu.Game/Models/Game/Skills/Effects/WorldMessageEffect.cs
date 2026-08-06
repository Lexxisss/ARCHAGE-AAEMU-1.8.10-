using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Data for the target client's WorldMessage plot effect.
/// The visible message is driven by SCPlotEventPacket and the client's matching database;
/// loading this template prevents the plot effect from being dropped as an unknown type.
/// </summary>
public sealed class WorldMessageEffect : EffectTemplate
{
    public bool ChatMessage { get; set; }
    public uint FactionScopeId { get; set; }
    public string IconKey { get; set; }
    public bool KillHero { get; set; }
    public int KillStreakCount { get; set; }
    public string Message { get; set; }
    public bool NameWithForeignWorld { get; set; }
    public bool ZoneGroupOnly { get; set; }
    public bool ZoneGroupWarState { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // Do not send a second guessed packet here: SCPlotEventPacket is broadcast by PlotNode
        // and the target client resolves this template from its own DB.
        Logger.Trace(
            "WorldMessageEffect: id={0}, skill={1}, caster={2}, target={3}, zoneOnly={4}, faction={5}, message={6}",
            Id,
            source?.Skill?.Template?.Id ?? 0,
            caster?.ObjId ?? 0,
            target?.ObjId ?? 0,
            ZoneGroupOnly,
            FactionScopeId,
            Message);
    }
}
