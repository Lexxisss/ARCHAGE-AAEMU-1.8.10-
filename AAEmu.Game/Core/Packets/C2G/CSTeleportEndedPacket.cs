using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTeleportEndedPacket : GamePacket
{
    public CSTeleportEndedPacket() : base(CSOffsets.CSTeleportEndedPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Target 10.8 wire layout, confirmed by the client acknowledgement:
        //   WorldPos pos = x:i64, y:i64, z:f32
        //   Quat     ori = x:f32, y:f32, z:f32, w:f32
        //   footer       = 9 bytes
        // X/Y are not ordinary integers. They use the ArcheAge fixed-point form
        // produced by Helpers.ConvertLongX/ConvertLongY: coordinate * 4096 << 32.
        var rawX = stream.ReadInt64();
        var rawY = stream.ReadInt64();
        var z = stream.ReadSingle();

        var oriX = stream.ReadSingle();
        var oriY = stream.ReadSingle();
        var oriZ = stream.ReadSingle();
        var oriW = stream.ReadSingle();

        var footerLength = Math.Min(9, stream.LeftBytes);
        var footer = footerLength > 0 ? stream.ReadBytes(footerLength) : Array.Empty<byte>();
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var x = Helpers.ConvertLongX(rawX);
        var y = Helpers.ConvertLongY(rawY);
        var positionIsValid = float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
                              Math.Abs(x) < 100000f && Math.Abs(y) < 100000f &&
                              z >= Helpers.MinPackedHeight - 1000f && z < 10000f;

        if (!positionIsValid)
        {
            Logger.Error(
                "TeleportEnded rejected invalid acknowledgement: characterId={0}, raw=({1},{2},{3}), decoded=({4},{5},{6})",
                character.Id, rawX, rawY, z, x, y, z);
            character.DisabledSetPosition = false;
            return;
        }

        // The acknowledgement is the first authoritative client packet after the
        // loading transition. Commit it to the server as well. This also makes the
        // handler self-correcting if an earlier command/effect only moved the client.
        // Explicitly clear both attachment links before applying the world position;
        // otherwise a ship's next FinalizeTransform can add its world delta again.
        character.Transform.Parent = null;
        character.Transform.StickyParent = null;

        var oldZoneId = character.Transform.ZoneId;
        var worldId = character.Transform.WorldId;
        var instanceId = character.Transform.InstanceId;
        var zoneId = WorldManager.Instance.GetZoneId(worldId, x, y);
        var currentRotation = character.Transform.World.Rotation;

        character.Transform.ApplyWorldSpawnPosition(new WorldSpawnPosition
        {
            WorldId = worldId,
            ZoneId = zoneId,
            X = x,
            Y = y,
            Z = z,
            Roll = currentRotation.X,
            Pitch = currentRotation.Y,
            Yaw = currentRotation.Z
        }, instanceId);

        var quaternion = new Quaternion(oriX, oriY, oriZ, oriW);
        if (float.IsFinite(quaternion.X) && float.IsFinite(quaternion.Y) &&
            float.IsFinite(quaternion.Z) && float.IsFinite(quaternion.W) &&
            quaternion.LengthSquared() > 0.0001f)
        {
            character.Transform.Local.ApplyFromQuaternion(Quaternion.Normalize(quaternion));
        }

        character.Transform.ResetFinalizeTransform();
        WorldManager.Instance.AddVisibleObject(character);

        if (oldZoneId != zoneId)
            character.OnZoneChange(oldZoneId, zoneId);
        character.Quests?.OnPositionChanged();

        character.DisabledSetPosition = false;

        Logger.Info(
            "TeleportEnded committed: characterId={0}, pos=({1:F1},{2:F1},{3:F1}), zone={4}, ori=({5:F4},{6:F4},{7:F4},{8:F4}), footer={9}",
            character.Id, x, y, z, zoneId, oriX, oriY, oriZ, oriW, BitConverter.ToString(footer));

        // Region membership and the visibility scan now use the acknowledged
        // destination, so NPCs and doodads are selected from the new location.
        WorldManager.ResendVisibleObjectsToCharacter(character);
    }
}
