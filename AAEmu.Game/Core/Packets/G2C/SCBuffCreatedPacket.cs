using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffCreatedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;
    private readonly Buff _buff;

    public SCBuffCreatedPacket(Buff buff) : base(SCOffsets.SCBuffCreatedPacket, 5)
    {
        _buff = buff;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Target 1.8.1.0 layout recovered from x2game.dll:
        // SkillCaster, source type/id (u64), owner BC, runtime buff id, BuffData.
        stream.Write(_buff.SkillCaster);
        stream.Write((ulong)(_buff.Caster?.Id ?? 0)); // type/source persistent id
        stream.WriteBc(_buff.Owner.ObjId);            // bc/owner object id
        stream.Write(_buff.Index);                    // optional runtime buff id

        // Target BuffData order: templateId, sourceLevel, sourceAbilityLevel, sourceSkillId, stackCount, then PISC
        // (charged, total duration / 10, elapsed time / 10, tick / 10).
        stream.Write(_buff.Template.BuffId);
        stream.Write(_buff.SourceLevel);
        stream.Write(_buff.SourceAbilityLevel);
        stream.Write(_buff.SourceSkillId);
        stream.Write(_buff.StackCount);
        _buff.WriteData(stream);
        return stream;
    }

    public override string Verbose()
    {
        return $" - Buff {_buff.Template.BuffId}:{_buff.Index}, Caster {_buff.Caster?.ObjId ?? 0}, Owner {_buff.Owner.ObjId}, SourceSkill {_buff.Skill?.Template.Id ?? 0}";
    }
}
