using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces the fire stage of a skill. Field order and the 10 ms wire-time
/// encoding are matched to the target x2game.dll serializer.
/// </summary>
public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;

    /// <summary>Delay before the server applies the effects, in milliseconds.</summary>
    public int ComputedDelay { get; set; }

    public SCSkillFiredPacket(
        uint id,
        ushort tl,
        SkillCaster caster,
        SkillCastTarget target,
        Skill skill,
        SkillObject skillObject)
        : base(SCOffsets.SCSkillFiredPacket, 5)
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
        // In this protocol generation the skill type is carried by the PISC
        // block near the end, while "sid" contains only the transient id.
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);
        stream.Write(_skillObject.InputDirection);

        stream.Write(ToWireTime(ComputedDelay));
        stream.Write(ToWireTime(_skill.Template.ChannelingTime));

        // Optional f/c/e/p block. Zero means that no optional fields follow.
        stream.Write((byte)0);
        stream.WritePisc(_id, _skill.Template.FireAnimId);
        stream.Write((byte)0); // trailing target flag
        return stream;
    }

    private static ushort ToWireTime(int milliseconds)
    {
        return (ushort)Math.Clamp(milliseconds / 10, 0, ushort.MaxValue);
    }
}
