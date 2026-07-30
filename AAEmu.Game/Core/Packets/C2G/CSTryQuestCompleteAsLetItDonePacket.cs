using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public sealed class CSTryQuestCompleteAsLetItDonePacket : GamePacket
{
    public CSTryQuestCompleteAsLetItDonePacket() : base(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var questId = stream.ReadUInt32();
        var reportObjectId = stream.ReadBc();
        var selected = stream.ReadInt32();
        Connection.ActiveChar.Quests.CompleteTarget(questId, reportObjectId, 0, selected, true);
    }
}
