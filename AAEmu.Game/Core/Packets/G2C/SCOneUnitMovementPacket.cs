using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Serializes SCOneUnitMovementPacket. The initial-state overload is used
/// immediately after SCUnitState so the client receives the unit's real
/// position and orientation instead of a hard-coded movement body.
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
        stream.WriteBc(_id);
        stream.Write((byte)_type.Type);
        stream.Write(_type);
        return stream;
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
