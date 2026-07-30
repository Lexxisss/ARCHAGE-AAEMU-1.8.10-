using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_DOODAD_OWNERSHIP_TIME_RESET (0x0227).
/// </summary>
public class SCDoodadOwnershipTimeResetPacket : GamePacket
{
    private readonly uint _doodadObjId;

    public SCDoodadOwnershipTimeResetPacket(uint doodadObjId)
        : base(SCOffsets.SCDoodadOwnershipTimeResetPacket, 5)
    {
        _doodadObjId = doodadObjId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodadObjId);
        return stream;
    }
}
