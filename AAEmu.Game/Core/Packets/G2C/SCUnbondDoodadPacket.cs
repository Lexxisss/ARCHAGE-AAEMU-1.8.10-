using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Removes a character's client-side bond from a doodad.
/// Target 1.8.1.0 wire layout (opcode 0x222):
/// BC(unitObjId), UInt64(characterIdentity), BC(doodadObjId).
/// </summary>
public class SCUnbondDoodadPacket : GamePacket
{
    private readonly uint _characterObjId;
    private readonly ulong _characterIdentity;
    private readonly uint _doodadObjId;

    public SCUnbondDoodadPacket(uint characterObjId, ulong characterIdentity, uint doodadObjId)
        : base(SCOffsets.SCUnbondDoodadPacket, 5)
    {
        _characterObjId = characterObjId;
        _characterIdentity = characterIdentity;
        _doodadObjId = doodadObjId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_characterObjId);
        stream.Write(_characterIdentity);
        stream.WriteBc(_doodadObjId);
        return stream;
    }
}
