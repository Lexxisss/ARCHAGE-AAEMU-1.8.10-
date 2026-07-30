using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffCreatedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Debug;
    private readonly Buff _buff;

    public SCBuffCreatedPacket(Buff buff) : base(SCOffsets.SCBuffCreatedPacket, 5)
    {
        _buff = buff;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Target 10.8.1.0 layout recovered from x2game.dll:
        // SkillCaster, source type/id (u64), owner BC, runtime buff id, BuffData.
        stream.Write(_buff.SkillCaster);
        stream.Write((ulong)(_buff.Caster?.Id ?? 0)); // type/source persistent id
        stream.WriteBc(_buff.Owner.ObjId);            // bc/owner object id
        stream.Write(_buff.Index);                    // optional runtime buff id

        // BuffData: t, l, a, PISC data, s, stack.
        stream.Write(_buff.Template.BuffId);
        stream.Write((byte)(_buff.Caster?.Level ?? 0));
        stream.Write(_buff.AbLevel);
        _buff.WriteData(stream);
        stream.Write(_buff.Skill?.Template.Id ?? 0u);
        stream.Write(_buff.Stack);
        return stream;
    }

    public override string Verbose()
    {
        return $" - Buff {_buff.Template.BuffId}:{_buff.Index}, Caster {_buff.Caster?.ObjId ?? 0}, Owner {_buff.Owner.ObjId}, SourceSkill {_buff.Skill?.Template.Id ?? 0}";
    }
}
