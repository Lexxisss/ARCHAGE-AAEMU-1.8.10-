using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Target 10.8 wire: UInt32 quest id, UInt24 field0, UInt24 field1, Int32 selected reward.</summary>
public sealed class CSCompleteQuestContextPacket : GamePacket
{
    public CSCompleteQuestContextPacket() : base(CSOffsets.CSCompleteQuestContextPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var questId = stream.ReadUInt32();
        var reportField0 = stream.ReadBc();
        var reportField1 = stream.ReadBc();
        var selected = stream.ReadInt32();
        Connection.ActiveChar.Quests.CompleteTarget(questId, reportField0, reportField1, selected, false);
    }
}
