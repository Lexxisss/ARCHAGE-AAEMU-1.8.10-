using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces the start of a slave/ship summon portal sequence.
/// </summary>
public class SCSlaveCreatedPacket : GamePacket
{
    private readonly uint _ownerObjId;
    private readonly ushort _tlId;
    private readonly uint _slaveObjId;
    private readonly long _creatorId;
    private readonly string _creatorName;

    public SCSlaveCreatedPacket(uint ownerObjId, ushort tlId, uint slaveObjId, long creatorId, string creatorName)
        : base(SCOffsets.SCSlaveCreatedPacket, 5)
    {
        _ownerObjId = ownerObjId;
        _tlId = tlId;
        _slaveObjId = slaveObjId;
        _creatorId = creatorId;
        _creatorName = creatorName;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Target 10.8 serializer 0x399D00E0:
        // bc, tl:u16, bc, type:i64 (creator persistent id), creatorName.
        stream.WriteBc(_ownerObjId);
        stream.Write(_tlId);
        stream.WriteBc(_slaveObjId);
        stream.Write(_creatorId);
        stream.Write(_creatorName);
        return stream;
    }
}
