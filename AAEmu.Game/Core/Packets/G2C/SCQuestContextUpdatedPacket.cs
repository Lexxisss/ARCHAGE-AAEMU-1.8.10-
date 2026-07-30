using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Target 10.8: QuestContextCore followed by ten unsigned PISC target values.</summary>
public sealed class SCQuestContextUpdatedPacket : GamePacket
{
    private readonly Quest _quest;

    public SCQuestContextUpdatedPacket(Quest quest, uint componentId)
        : base(SCOffsets.SCQuestContextUpdatedPacket, 5)
    {
        _quest = quest;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_quest);
        stream.WritePiscW(Quest.ClientObjectiveCount, _quest.GetClientObjectiveTargets().Select(x => (long)Math.Max(0, x)).ToArray());
        return stream;
    }
}
