using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResurrectCharacterPacket : GamePacket
{
    public CSResurrectCharacterPacket() : base(CSOffsets.CSResurrectCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Target 10.8 sends one bool followed by twelve currently-reserved bytes. Consume the
        // complete body so packet diagnostics and the next packet remain aligned.
        var inPlace = stream.ReadBoolean();
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Debug("ResurrectCharacter: character={0}, inPlace={1}", character.Id, inPlace);

        float x;
        float y;
        float z;
        float yaw;
        CharacterTeleportManager.Transition transition = null;

        if (inPlace)
        {
            x = character.Transform.World.Position.X;
            y = character.Transform.World.Position.Y;
            z = character.Transform.World.Position.Z;
            yaw = character.Transform.World.Rotation.Z;

            character.Hp = (int)(character.MaxHp * (character.ResurrectHpPercent / 100.0f));
            character.Mp = (int)(character.MaxMp * (character.ResurrectMpPercent / 100.0f));
            character.ResurrectHpPercent = 1;
            character.ResurrectMpPercent = 1;
        }
        else
        {
            var portal = PortalManager.Instance.GetClosestReturnPortal(character);
            var destination = CharacterTeleportManager.FromPortal(portal, character.Transform.WorldId);
            if (destination != null)
            {
                transition = CharacterTeleportManager.Prepare(character, destination);
                x = destination.X;
                y = destination.Y;
                z = destination.Z;
                yaw = destination.Yaw;
            }
            else
            {
                Logger.Warn(
                    "ResurrectCharacter: no return portal for character={0}, world={1}, zone={2}; resurrecting at corpse",
                    character.Id, character.Transform.WorldId, character.Transform.ZoneId);
                x = character.Transform.World.Position.X;
                y = character.Transform.World.Position.Y;
                z = character.Transform.World.Position.Z;
                yaw = character.Transform.World.Rotation.Z;
            }

            character.Hp = Math.Max(1, (int)(character.MaxHp * 0.1f));
            character.Mp = Math.Max(1, (int)(character.MaxMp * 0.1f));
        }

        character.PostUpdateCurrentHp(character, 0, character.Hp, KillReason.Unknown);
        character.BroadcastPacket(new SCCharacterResurrectedPacket(character.ObjId, x, y, z, yaw), true);
        if (transition != null)
            CharacterTeleportManager.Complete(character, transition, TeleportReason.MoveToLocation);
        character.BroadcastPacket(
            new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp, character.HighAbilityRsc), true);

        character.IsUnderWater = false;
        character.Breath = character.LungCapacity;
    }
}
