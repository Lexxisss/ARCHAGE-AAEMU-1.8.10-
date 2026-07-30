using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 10.8 world-permission boundary (opcode 0x0192). The symbol name in
/// the working reference differs, but this existing handler is the confirmed
/// point at which the typed world bootstrap must be sent.
/// </summary>
public class CSRequestPermissionToPlayCinemaForDirectingModePacket : GamePacket
{
    public CSRequestPermissionToPlayCinemaForDirectingModePacket() : base(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingModePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }

    public override void Execute()
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Connection.State = GameState.World;
        Connection.WorldEntryReady = false;
        Connection.Protocol1810VisibleNpcObjIds.Clear();
        Connection.Protocol1810NpcVisibilityAnchorValid = false;
        RecoverInvalidSavedPosition(character);
        character.DisabledSetPosition = false;
        character.IsOnline = true;
        character.IsVisible = false;
        character.WorldEntryComplete = false;

        // Register ownership so the first CSMoveUnit can resolve the local
        // object, but do not publish region visibility before 0x0006.
        WorldManager.Instance.AddObject(character);

        // Confirmed target order after C2S 0x0192. These three channel joins
        // produce exactly Shout, Region and Ally acknowledgements (0x01E5).
        ChatManager.Instance.GetZoneChat(character.Transform.ZoneId).JoinChannel(character);
        ChatManager.Instance.GetNationChat(character.Race).JoinChannel(character);
        ChatManager.Instance.GetFactionChat(character.Faction.MotherId).JoinChannel(character);

        character.SendPacket(new SCUnitPointsPacket(
            character.ObjId, character.Hp, character.Mp, character.HighAbilityRsc));
        character.SendPacket(new SCUnitStatePacket(character));
        character.SendPacket(new SCUnitVisualOptionsPacket(character.ObjId, character.VisualOptions));
        character.SendPacket(new SCDetailedTimeOfDayPacket(TimeManager.Instance.GetTime()));
        character.SendPacket(new SCCharacterGamePointsPacket(character));
        character.SendPacket(new SCCharacterLaborPowerChangedPacket(0, 0, 0, 0));
        character.SendPacket(new SCUnitOpenEquipInfoPacket(character.ObjId, true));

        // The verified donor publishes NPCs here, immediately after the self
        // bootstrap, by scanning the world within 150 metres. Do not depend on
        // Region.AddToCharacters: migrated spawn data can leave valid NPCs in
        // WorldManager while region publication is not yet attached.
        var publishedNpcs = WorldManager.Instance.PublishProtocol1810NearbyNpcs(character);

        Logger.Info(
            "Target world bootstrap sent after 0x0192: characterId={0}, objId={1}, nearbyNpcs={2}",
            character.Id,
            character.ObjId,
            publishedNpcs);
    }

    private static void RecoverInvalidSavedPosition(AAEmu.Game.Models.Game.Char.Character character)
    {
        var position = character.Transform.World.Position;
        var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
        var finite = float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
        var validRegion = finite && world != null && WorldManager.Instance.GetRegion(character) != null;
        var validHeight = world != null && position.Z > -1000f && position.Z < world.MaxHeight + 1000f;
        var plausibleTerrainHeight = true;
        if (validRegion && world?.HeightMaps != null)
        {
            try
            {
                var terrainHeight = world.GetHeight(position.X, position.Y);
                plausibleTerrainHeight = position.Z < terrainHeight + 1000f;
            }
            catch
            {
                // A missing/incomplete height map must not block world entry.
                plausibleTerrainHeight = true;
            }
        }

        if (validRegion && validHeight && plausibleTerrainHeight)
            return;

        var oldWorld = character.Transform.WorldId;
        var oldX = position.X;
        var oldY = position.Y;
        var oldZ = position.Z;
        var template = CharacterManager.Instance.GetTemplate((byte)character.Race, (byte)character.Gender);
        character.Transform.ApplyWorldSpawnPosition(template.SpawnPosition);
        character.Region = null;

        Logger.Warn(
            "Recovered invalid saved character position: characterId={0}, oldWorld={1}, old=({2:F1},{3:F1},{4:F1}), newWorld={5}, new=({6:F1},{7:F1},{8:F1})",
            character.Id,
            oldWorld,
            oldX,
            oldY,
            oldZ,
            character.Transform.WorldId,
            character.Transform.World.Position.X,
            character.Transform.World.Position.Y,
            character.Transform.World.Position.Z);
    }
}
