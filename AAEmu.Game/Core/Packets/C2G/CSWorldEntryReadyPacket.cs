using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSWorldEntryReadyPacket : GamePacket
{
    public CSWorldEntryReadyPacket() : base(CSOffsets.CSWorldEntryReadyPacket, 5) { }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }

    public override void Execute()
    {
        Connection.WorldEntryReady = true;
        Connection.SendPacket(new SCDetailedTimeOfDayPacket(TimeManager.Instance.GetTime()));

        var character = Connection.ActiveChar;
        var doodadCount = WorldManager.Instance.PublishProtocol1810CurrentRegionDoodads(character);

        // Quest history and active contexts were sent during character selection,
        // before the client built the journal name/chapter index. At world-ready
        // only refresh map/NPC markers; re-sending the state here is too late.
        if (character != null)
            character.Quests.RefreshQuestNotifier();

        Logger.Info(
            "World-ready signal received for characterId={0}; publishedDoodads={1}; questStateSynced={2}",
            character?.Id ?? 0,
            doodadCount,
            character != null);
    }
}
