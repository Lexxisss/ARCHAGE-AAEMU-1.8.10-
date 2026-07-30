using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Target 10.8 SC_DOODAD_COMPLETE_QUEST: Bc doodad object id + UInt32 quest id.</summary>
public sealed class SCDoodadCompleteQuestPacket : GamePacket
{
    private readonly uint _doodadObjId;
    private readonly uint _questId;

    public SCDoodadCompleteQuestPacket(uint doodadObjId, uint questId)
        : base(SCOffsets.SCDoodadCompleteQuestPacket, 5)
    {
        _doodadObjId = doodadObjId;
        _questId = questId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodadObjId);
        stream.Write(_questId);
        return stream;
    }
}
