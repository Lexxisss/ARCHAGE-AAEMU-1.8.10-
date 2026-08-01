using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Confirms a building's new rotation to everyone who can see it.
/// </summary>
public class SCHouseRotatedPacket : GamePacket
{
    private readonly uint _objId;
    private readonly float _zRot;

    public SCHouseRotatedPacket(uint objId, float zRot) : base(SCOffsets.SCHouseRotatedPacket, 5)
    {
        _objId = objId;
        _zRot = zRot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId); // bc   : 3 bytes
        stream.Write(_zRot);    // zRot : f32
        return stream;
    }
}
