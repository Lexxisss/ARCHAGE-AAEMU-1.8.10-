using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Target 10.8 SCQuestContextCompleted: UInt32 quest id + UInt32 component id.</summary>
public sealed class SCQuestContextCompletedPacket : GamePacket
{
    private readonly uint _questId;
    private readonly uint _componentId;

    public SCQuestContextCompletedPacket(uint questId, uint componentId)
        : base(SCOffsets.SCQuestContextCompletedPacket, 5)
    {
        _questId = questId;
        _componentId = componentId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_questId);
        stream.Write(_componentId);
        return stream;
    }
}
