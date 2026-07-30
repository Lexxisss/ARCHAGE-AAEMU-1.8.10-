using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_DOODAD_FIRST_INTERACTION_SYNC (0x00F5).
/// </summary>
public class SCDoodadFirstInteractionSyncPacket : GamePacket
{
    private readonly uint _doodadObjId;
    private readonly ulong _firstInteractionId;

    public SCDoodadFirstInteractionSyncPacket(uint doodadObjId, ulong firstInteractionId)
        : base(SCOffsets.SCDoodadFirstInteractionSyncPacket, 5)
    {
        _doodadObjId = doodadObjId;
        _firstInteractionId = firstInteractionId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodadObjId);
        stream.Write(_firstInteractionId);
        return stream;
    }
}
