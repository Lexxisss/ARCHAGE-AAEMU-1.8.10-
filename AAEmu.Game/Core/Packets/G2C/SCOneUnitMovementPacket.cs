using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Serializes SCOneUnitMovementPacket. The initial-state overload is used
/// immediately after SCUnitState so the client receives the unit's real
/// position and orientation instead of a hard-coded movement body.
/// Unit movement (NPCs) uses a dedicated 1.8.1.0 layout: the position is
/// padded to 11 bytes and the optional actor tail (fall velocity, grapple/
/// climb data, etc.) that the general MoveType.Write() tail supports is not
/// present on this wire form.
/// </summary>
public class SCOneUnitMovementPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private readonly uint _id;
    private readonly MoveType _type;

    public SCOneUnitMovementPacket(uint id, MoveType type)
        : base(SCOffsets.SCOneUnitMovementPacket, 1)
    {
        _id = id;
        _type = type ?? throw new ArgumentNullException(nameof(type));
    }

    public SCOneUnitMovementPacket(Unit unit)
        : this(unit?.ObjId ?? throw new ArgumentNullException(nameof(unit)), CreateInitialMovement(unit))
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_type is UnitMoveType unitMoveType)
        {
            WriteUnitMovementBody(stream, unitMoveType);
            return stream;
        }

        stream.WriteBc(_id);
        stream.Write((byte)_type.Type);
        stream.Write(_type);
        return stream;
    }

    private void WriteUnitMovementBody(PacketStream stream, UnitMoveType unitMoveType)
    {
        stream.WriteBc(_id);
        stream.Write((byte)unitMoveType.Type);
        stream.Write(unitMoveType.Time);
        stream.Write(unitMoveType.Flags);
        if ((unitMoveType.Flags & 0x10) == 0x10)
        {
            stream.Write(unitMoveType.ScType);
            stream.Write(unitMoveType.Phase);
        }

        WritePaddedPosition(stream, unitMoveType.X, unitMoveType.Y, unitMoveType.Z);
        stream.Write(unitMoveType.VelX);
        stream.Write(unitMoveType.VelY);
        stream.Write(unitMoveType.VelZ);
        stream.Write(unitMoveType.RotationX);
        stream.Write(unitMoveType.RotationY);
        stream.Write(unitMoveType.RotationZ);

        var delta = unitMoveType.DeltaMovement ?? new sbyte[3];
        stream.Write(delta.Length > 0 ? delta[0] : (sbyte)0);
        stream.Write(delta.Length > 1 ? delta[1] : (sbyte)0);
        stream.Write(delta.Length > 2 ? delta[2] : (sbyte)0);
        stream.Write((byte)unitMoveType.Stance);
        stream.Write((byte)unitMoveType.Alertness);
        stream.Write(unitMoveType.ActorFlags);
    }

    // Same 9-byte position encoding as PacketStream.WritePosition, but with a
    // zero pad byte inserted after the X block and after the Y block, matching
    // the 11-byte position field observed for unit movement on this client.
    private static void WritePaddedPosition(PacketStream stream, float x, float y, float z)
    {
        var pos = Helpers.ConvertPosition(x, y, z);
        stream.Write(pos[0]);
        stream.Write(pos[1]);
        stream.Write(pos[2]);
        stream.Write((byte)0);
        stream.Write(pos[3]);
        stream.Write(pos[4]);
        stream.Write(pos[5]);
        stream.Write((byte)0);
        stream.Write(pos[6]);
        stream.Write(pos[7]);
        stream.Write(pos[8]);
    }

    public override string Verbose()
    {
        return " - " + (_type?.Type.ToString() ?? "none") + " " +
               (WorldManager.Instance.GetGameObject(_id)?.DebugName() ?? "(" + _id + ")");
    }

    private static UnitMoveType CreateInitialMovement(Unit unit)
    {
        var movement = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        var local = unit.Transform.Local;
        var (rotationX, rotationY, rotationZ) = local.ToRollPitchYawSBytesMovement();

        movement.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        movement.Flags = 4;
        movement.X = local.Position.X;
        movement.Y = local.Position.Y;
        movement.Z = local.Position.Z;
        movement.VelX = 0;
        movement.VelY = 0;
        movement.VelZ = 0;
        movement.RotationX = rotationX;
        movement.RotationY = rotationY;
        movement.RotationZ = rotationZ;
        movement.DeltaMovement = new sbyte[3];
        movement.Stance = 1;    // idle
        movement.Alertness = 0; // idle
        movement.ActorFlags = 3;
        return movement;
    }
}
