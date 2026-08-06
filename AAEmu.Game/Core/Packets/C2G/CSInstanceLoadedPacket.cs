using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInstanceLoadedPacket : GamePacket
{
    public CSInstanceLoadedPacket() : base(CSOffsets.CSInstanceLoadedPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Target 1.8.1 packet is empty. The vtable serializer at x2game.dll
        // 0x39687BF0 returns immediately. Any remaining bytes are decrypted AES
        // block padding, not packet fields.
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }

    public override void Execute()
    {
        var character = Connection.ActiveChar;
        if (character == null)
        {
            Logger.Warn("InstanceLoaded received without an active character.");
            return;
        }

        // The destination transform is prepared when SCLoadInstance is sent, but
        // visibility is deliberately detached until this acknowledgement. Declare
        // the local unit first; only then publish the destination region around it.
        Connection.SendPacket(new SCUnitStatePacket(character));

        character.Transform.ResetFinalizeTransform();
        WorldManager.Instance.AddVisibleObject(character);

        // Region.AddToCharacters intentionally suppresses legacy doodad creation
        // while WorldEntryReady is false. InstanceLoaded is the instance equivalent
        // of the initial CSWorldEntryReady signal, so publish the verified 1.8.1
        // doodad records after region membership has been rebuilt.
        var doodadCount = WorldManager.Instance.PublishProtocol1810CurrentRegionDoodads(character);
        Connection.WorldEntryReady = true;

        Connection.SendPacket(SCCooldownsPacket.ForCharacter(character));
        Connection.SendPacket(new SCDetailedTimeOfDayPacket(TimeManager.Instance.GetTime()));

        character.DisabledSetPosition = false;
        character.Quests?.RefreshQuestNotifier();

        Logger.Info(
            "InstanceLoaded completed: characterId={0}, worldId={1}, instanceId={2}, zoneId={3}, doodads={4}",
            character.Id,
            character.Transform.WorldId,
            character.Transform.InstanceId,
            character.Transform.ZoneId,
            doodadCount);
    }
}
