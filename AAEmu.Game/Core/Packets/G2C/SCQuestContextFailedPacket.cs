using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Target 10.8 wire: UInt32 quest id and UInt8 error.</summary>
public sealed class SCQuestContextFailedPacket : GamePacket
{
    private readonly uint _questId;
    private readonly byte _error;

    public SCQuestContextFailedPacket(uint questId, byte error)
        : base(SCOffsets.SCQuestContextFailedPacket, 5)
    {
        _questId = questId;
        _error = error;
    }

    public SCQuestContextFailedPacket(uint questId, QuestStatusFailed error)
        : this(questId, (byte)error)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_questId);
        stream.Write(_error);
        return stream;
    }
}
