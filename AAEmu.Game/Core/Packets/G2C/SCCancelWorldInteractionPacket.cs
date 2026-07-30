using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_WORLD_INTERACTION_CANCELED (0x0224).
/// Exact wire layout: BC interactedUnitId + UInt32 interactedDoodadId.
/// </summary>
public class SCCancelWorldInteractionPacket : GamePacket
{
    private readonly uint _interactedUnitId;
    private readonly uint _interactedDoodadId;

    public SCCancelWorldInteractionPacket(uint interactedUnitId, uint interactedDoodadId)
        : base(SCOffsets.SCCancelWorldInteractionPacket, 5)
    {
        _interactedUnitId = interactedUnitId;
        _interactedDoodadId = interactedDoodadId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_interactedUnitId);
        stream.Write(_interactedDoodadId);
        return stream;
    }
}
