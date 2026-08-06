using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Compatibility name for the target client's SCUnbondDoodadPacket (opcode 0x222).
/// Target 1.8.1.0 wire layout: BC(unitObjId), UInt64(characterIdentity), BC(doodadObjId).
/// </summary>
public class SCDetachFromDoodadPacket : GamePacket
{
    private readonly uint _characterObjId;
    private readonly ulong _characterIdentity;
    private readonly uint _doodadObjId;

    public SCDetachFromDoodadPacket(uint characterObjId, ulong characterIdentity, uint doodadObjId)
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
