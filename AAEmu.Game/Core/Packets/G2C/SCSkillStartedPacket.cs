using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces the cast-start stage of a skill. Field order is recovered from
/// the target x2game.dll serializer, including its 10 ms time representation.
/// </summary>
public class SCSkillStartedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Debug;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly Skill _skill;
    private readonly SkillObject _skillObject;

    /// <summary>Cast time after the caster's modifiers, in milliseconds.</summary>
    public int CastTime { get; set; }

    /// <summary>Unmodified cast time from the skill template, in milliseconds.</summary>
    public int BaseCastTime { get; set; }

    /// <summary>Optional f/c/e/p/d block; defaults to empty.</summary>
    public SkillExtraData ExtraData { get; set; } = SkillExtraData.Default;

    public SCSkillStartedPacket(
        uint id,
        ushort tl,
        SkillCaster caster,
        SkillCastTarget target,
        Skill skill,
        SkillObject skillObject)
        : base(SCOffsets.SCSkillStartedPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);
        stream.Write(_skillObject.InputDirection);

        // Real cast time first, then the template's unmodified value - the client uses the
        // pair to drive the cast bar. Writing the same number twice hid any haste/slow.
        stream.Write(ToWireTime(CastTime));
        stream.Write(ToWireTime(BaseCastTime > 0 ? BaseCastTime : CastTime));
        stream.Write(false);   // castSynergy
        // Sparse f/c/e/p/d block, serialized last. Empty here, which is the mask alone.
        ExtraData.Write(stream);
        return stream;
    }

    private static ushort ToWireTime(int milliseconds)
    {
        return (ushort)Math.Clamp(milliseconds / 10, 0, ushort.MaxValue);
    }

    public override string Verbose()
    {
        return $" - Id {_id}, TlId {_tl}, Caster {_caster.ObjId}, Target {_target.ObjId}, Skill {_skill.Template.Id}";
    }
}
