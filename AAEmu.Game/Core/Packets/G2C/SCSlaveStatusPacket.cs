using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Ship / land-vehicle state.
/// </summary>
/// <remarks>
/// The handler has a mixed gate: if the unit is not in the object registry the skill state is
/// dropped, but the metadata still reaches the slave manager. For the local owner with a
/// non-zero db id it is what fixes the current controlled slave, so the owner field has to be
/// right or the player never takes control of their own ship.
///
/// Two faults were shifting this packet. The skill state carries three collections - skills,
/// tags and charges - and only two were being written, so everything after it was read from
/// the wrong bytes. And the owner is a 64-bit persistent character id which the handler
/// compares against the local persistent id; we were sending a 32-bit world object id, which
/// is both four bytes short and a different number entirely.
/// </remarks>
public class SCSlaveStatusPacket : GamePacket
{
    private readonly int _skillCount;
    private readonly int _tagCount;
    private readonly int _chargeCount;
    private readonly Slave _slave;

    public SCSlaveStatusPacket(Slave slave) : base(SCOffsets.SCSlaveStatusPacket, 5)
    {
        _slave = slave;
        _skillCount = slave.Skills?.Count ?? 0;
        _tagCount = 0;
        _chargeCount = 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_slave.ObjId);                 // unitId    : compressed
        stream.Write(_slave.TlId);                    // tl        : u16
        stream.Write(_slave.SummoningItem?.Id ?? 0ul); // slaveType : i64

        // SkillState: three separate collections, each a count followed by that many triples.
        stream.Write(_skillCount);
        if (_skillCount > 0)
        {
            foreach (var skill in _slave.Skills)
            {
                stream.Write(skill); // skillId
                stream.Write(0u);    // skill-state value/flags, semantics not yet named
                stream.Write(0u);    // skill-state auxiliary value, semantics not yet named
            }
        }

        stream.Write(_tagCount);
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u);
            stream.Write(0u);
            stream.Write(0u);
        }

        stream.Write(_chargeCount);
        for (var i = 0; i < _chargeCount; i++)
        {
            stream.Write(0u);
            stream.Write(0u);
            stream.Write(0u);
        }

        stream.Write(_slave.Summoner?.Name ?? string.Empty); // creatorName        : string, max 128
        stream.Write((long)(_slave.Summoner?.Id ?? 0));      // ownerPersistentId  : i64
        stream.Write(_slave.Id);                             // dbSlaveId          : u32

        return stream;
    }
}
