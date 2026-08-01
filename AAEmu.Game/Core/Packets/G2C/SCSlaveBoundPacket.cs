using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Binds a ship or land vehicle to its master.
/// </summary>
/// <remarks>
/// The handler compares the master id against the local character's persistent id and, when
/// they match, updates the player's own-slave state as well - so this is what decides whether
/// the player is treated as the owner of what they just bound.
///
/// The master id is a 64-bit persistent character id and is followed by the master's world id.
/// We were writing a 32-bit value and omitting the world byte, which left the packet five
/// bytes short and the compact slave id read from the wrong place.
/// </remarks>
public class SCSlaveBoundPacket : GamePacket
{
    private readonly long _masterPersistentId;
    private readonly byte _masterWorldId;
    private readonly uint _slaveObjId;

    public SCSlaveBoundPacket(long masterPersistentId, uint slaveObjId, byte masterWorldId = 0)
        : base(SCOffsets.SCSlaveBoundPacket, 5)
    {
        _masterPersistentId = masterPersistentId;
        _masterWorldId = masterWorldId;
        _slaveObjId = slaveObjId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_masterPersistentId); // masterPersistentId : i64
        stream.Write(_masterWorldId);      // masterWorldId      : u8
        stream.WriteBc(_slaveObjId);       // slaveUnitId        : compact
        return stream;
    }
}
