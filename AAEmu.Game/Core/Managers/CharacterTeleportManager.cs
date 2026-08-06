using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

using PortalDestination = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Keeps the authoritative character transform, region membership and client teleport packet in
/// one operation. Resurrection, Escape and Recall used to update only one side of that state.
/// </summary>
public static class CharacterTeleportManager
{
    public sealed class Transition
    {
        public uint OldWorldId { get; init; }
        public uint OldInstanceId { get; init; }
        public WorldSpawnPosition Destination { get; init; }
        public uint InstanceId { get; init; }
    }

    /// <summary>
    /// Moves the server-side character first and locks ordinary movement until the target client
    /// acknowledges the transition with CSTeleportEnded.
    /// </summary>
    public static Transition Prepare(Character character, WorldSpawnPosition destination, uint instanceId = 0)
    {
        if (character == null || destination == null)
            return null;

        var oldWorldId = character.Transform.WorldId;
        var oldInstanceId = character.Transform.InstanceId;
        var oldZoneId = character.Transform.ZoneId;

        var normalized = new WorldSpawnPosition
        {
            WorldId = destination.WorldId == 0 ? oldWorldId : destination.WorldId,
            ZoneId = destination.ZoneId,
            X = destination.X,
            Y = destination.Y,
            Z = destination.Z,
            Roll = destination.Roll,
            Pitch = destination.Pitch,
            Yaw = destination.Yaw
        };

        if (normalized.ZoneId == 0)
            normalized.ZoneId = WorldManager.Instance.GetZoneId(normalized.WorldId, normalized.X, normalized.Y);

        var targetInstanceId = instanceId != 0
            ? instanceId
            : normalized.WorldId == oldWorldId
                ? oldInstanceId
                : WorldManager.DefaultInstanceId;

        // A local position relative to a mount/ship must never survive a world teleport.
        character.ForceDismount(AttachUnitReason.PrefabChanged);
        character.Transform.Parent = null;
        character.Transform.StickyParent = null;
        character.DisabledSetPosition = false;
        character.Transform.ApplyWorldSpawnPosition(normalized, targetInstanceId);
        character.Transform.ResetFinalizeTransform();
        WorldManager.Instance.AddVisibleObject(character);

        if (oldZoneId != normalized.ZoneId)
            character.OnZoneChange(oldZoneId, normalized.ZoneId);
        character.Quests?.OnPositionChanged();

        character.IsUnderWater = false;
        character.Breath = character.LungCapacity;
        character.DisabledSetPosition = true;

        return new Transition
        {
            OldWorldId = oldWorldId,
            OldInstanceId = oldInstanceId,
            Destination = normalized,
            InstanceId = targetInstanceId
        };
    }

    public static void Complete(Character character, Transition transition,
        TeleportReason reason = TeleportReason.MoveToLocation)
    {
        if (character == null || transition?.Destination == null)
            return;

        var destination = transition.Destination;
        if (transition.OldWorldId != destination.WorldId || transition.OldInstanceId != transition.InstanceId)
        {
            character.SendPacket(new SCLoadInstancePacket(
                transition.InstanceId,
                destination.ZoneId,
                destination.X,
                destination.Y,
                destination.Z,
                destination.Roll,
                destination.Pitch,
                destination.Yaw));
            return;
        }

        character.SendPacket(new SCTeleportUnitPacket(
            reason,
            ErrorMessageType.NoErrorMessage,
            destination.X,
            destination.Y,
            destination.Z,
            destination.Yaw));
    }

    public static bool Teleport(Character character, WorldSpawnPosition destination,
        TeleportReason reason = TeleportReason.MoveToLocation, uint instanceId = 0)
    {
        var transition = Prepare(character, destination, instanceId);
        if (transition == null)
            return false;
        Complete(character, transition, reason);
        return true;
    }

    public static WorldSpawnPosition FromPortal(PortalDestination portal, uint fallbackWorldId)
    {
        if (portal == null ||
            (portal.Id == 0 && portal.X == 0f && portal.Y == 0f && portal.Z == 0f))
            return null;

        return new WorldSpawnPosition
        {
            WorldId = portal.WorldId == 0 ? fallbackWorldId : portal.WorldId,
            ZoneId = portal.ZoneId,
            X = portal.X,
            Y = portal.Y,
            Z = portal.Z,
            Yaw = (portal.ZRot != 0f ? portal.ZRot : portal.Yaw) * (MathF.PI / 180f)
        };
    }
}
