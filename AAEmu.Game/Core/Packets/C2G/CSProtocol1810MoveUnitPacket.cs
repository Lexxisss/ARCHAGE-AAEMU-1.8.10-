using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 1.8/10.8 movement packet (opcode 0x0104).
/// The client stores each packed X/Y/Z coordinate in three bytes but inserts
/// one padding byte after X and after Y, producing an 11-byte position block.
/// Treating this packet as the legacy 0x00EA movement layout shifts every
/// following field and corrupts the authoritative server position.
/// </summary>
public sealed class CSProtocol1810MoveUnitPacket : GamePacket
{
    private const int BaseHeaderLength = 9; // bc objId + type + time + flags
    private const int OptionalScTypeAndPhaseLength = sizeof(uint) + sizeof(byte);
    private const int PaddedPositionLength = 11;
    private const int PackedPositionLength = 9;
    private const float MaxNormalHorizontalDelta = 250f;
    private const float MaxNormalVerticalDelta = 100f;

    private static int _movementProbeCount;

    private uint _objId;
    private MoveTypeEnum _moveType;
    private uint _time;
    private byte _flags;
    private uint _scType;
    private byte _phase;
    private float _x;
    private float _y;
    private float _z;
    private bool _valid;
    private bool _usedPaddedPosition;
    private byte[] _body = Array.Empty<byte>();

    /// <summary>
    /// The whole body, read as the variant its type byte names.
    /// </summary>
    /// <remarks>
    /// Only the player's own steps used to be decoded, and only as far as the position. Anything
    /// the player was driving - a cart, a ship, the animal under them - was logged as unsupported
    /// and dropped, which is why none of them moved: the client sends all of it here, and the
    /// handler that knew what to do with it sits on an opcode this client never sends.
    /// </remarks>
    private MoveType _movement;

    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public CSProtocol1810MoveUnitPacket() : base(CSOffsets.CSProtocol1810MoveUnitPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        _body = stream.LeftBytes > 0 ? stream.ReadBytes(stream.LeftBytes) : Array.Empty<byte>();
        if (_body.Length < BaseHeaderLength)
            return;

        try
        {
            var bodyStream = new PacketStream(_body);
            _objId = bodyStream.ReadBc();
            _moveType = (MoveTypeEnum)bodyStream.ReadByte();
            _time = bodyStream.ReadUInt32();
            _flags = bodyStream.ReadByte();

            if ((_flags & 0x10) != 0)
            {
                if (bodyStream.LeftBytes < OptionalScTypeAndPhaseLength)
                    return;

                _scType = bodyStream.ReadUInt32();
                _phase = bodyStream.ReadByte();
            }

            // ShipRequest is the short control-only variant:
            //   bc objId + type + time + flags + throttle + steering
            // It has no position block. The previous universal 18-byte minimum rejected every
            // helm input before Execute(), leaving both requests permanently at zero.
            if (_moveType == MoveTypeEnum.ShipRequest)
            {
                _movement = ReadVariantBody();
                _valid = _movement is ShipRequestMoveType;
                return;
            }

            var positionOffset = BaseHeaderLength;
            if ((_flags & 0x10) != 0)
                positionOffset += OptionalScTypeAndPhaseLength;

            var positionLength = PackedPositionLength;
            if (_body.Length >= positionOffset + PaddedPositionLength &&
                _body[positionOffset + 3] == 0 &&
                _body[positionOffset + 7] == 0)
            {
                var packedPosition = new byte[PackedPositionLength]
                {
                    _body[positionOffset],
                    _body[positionOffset + 1],
                    _body[positionOffset + 2],
                    _body[positionOffset + 4],
                    _body[positionOffset + 5],
                    _body[positionOffset + 6],
                    _body[positionOffset + 8],
                    _body[positionOffset + 9],
                    _body[positionOffset + 10]
                };

                (_x, _y, _z) = Helpers.ConvertPosition(packedPosition);
                _usedPaddedPosition = true;
                positionLength = PaddedPositionLength;
            }
            else if (_body.Length >= positionOffset + PackedPositionLength)
            {
                var packedPosition = new byte[PackedPositionLength];
                Buffer.BlockCopy(_body, positionOffset, packedPosition, 0, PackedPositionLength);
                (_x, _y, _z) = Helpers.ConvertPosition(packedPosition);
            }
            else
            {
                return;
            }

            _valid = float.IsFinite(_x) && float.IsFinite(_y) && float.IsFinite(_z);

            // The target Unit variant uses the same two pad bytes as the position probe above.
            // The generic UnitMoveType reader consumes nine bytes and shifts velocity, rotation
            // and deltaMovement by two. Parse the fixed portion explicitly so attached movement
            // and the helm fallback receive the actual signed input bytes.
            _movement = _moveType == MoveTypeEnum.Unit && _usedPaddedPosition
                ? ReadPaddedUnitBody(positionOffset + positionLength)
                : ReadVariantBody();
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to decode target 0x0104 movement body, length={0}", _body.Length);
            _valid = false;
        }
    }

    private UnitMoveType ReadPaddedUnitBody(int tailOffset)
    {
        // Fixed bytes after the 11-byte position:
        // velocity[3]:i16, rotation[3]:i8, deltaMovement[3]:i8,
        // stance:i8, alertness:i8, actorFlags:u16.
        const int fixedTailLength = 6 + 3 + 3 + 1 + 1 + 2;
        if (tailOffset < 0 || _body.Length < tailOffset + fixedTailLength)
            return null;

        var tail = new byte[_body.Length - tailOffset];
        Buffer.BlockCopy(_body, tailOffset, tail, 0, tail.Length);
        var stream = new PacketStream(tail);

        return new UnitMoveType
        {
            Type = MoveTypeEnum.Unit,
            Time = _time,
            Flags = _flags,
            ScType = _scType,
            Phase = _phase,
            X = _x,
            Y = _y,
            Z = _z,
            VelX = stream.ReadInt16(),
            VelY = stream.ReadInt16(),
            VelZ = stream.ReadInt16(),
            RotationX = stream.ReadSByte(),
            RotationY = stream.ReadSByte(),
            RotationZ = stream.ReadSByte(),
            DeltaMovement =
            [
                stream.ReadSByte(),
                stream.ReadSByte(),
                stream.ReadSByte()
            ],
            Stance = stream.ReadSByte(),
            Alertness = stream.ReadSByte(),
            ActorFlags = stream.ReadUInt16()
        };
    }

    /// <summary>
    /// Reads the body as the variant its type byte names, starting after the object id and that
    /// byte. Returns null when the body does not hold a complete one.
    /// </summary>
    private MoveType ReadVariantBody()
    {
        try
        {
            var stream = new PacketStream(_body);
            stream.ReadBc();
            var type = (MoveTypeEnum)stream.ReadByte();

            var movement = MoveType.GetType(type);
            movement.Read(stream);
            return movement;
        }
        catch (Exception exception)
        {
            // The body itself, so the layout can be worked out from a real one rather than
            // guessed at. A variant that does not parse is always a length disagreement, and the
            // only thing that settles those is the bytes.
            Logger.Warn(exception,
                "Failed to decode target 0x0104 {0} body, length={1}, flags=0x{2:X2}, body={3}",
                _moveType, _body.Length, _flags, Convert.ToHexString(_body));
            return null;
        }
    }

    public override void Execute()
    {
        var character = Connection.ActiveChar;
        if (!_valid || character == null)
            return;

        // DisabledSetPosition protects free-world coordinates while a teleport is in flight. It
        // must not suppress control-only ShipRequest packets or parent-local movement from a
        // character already attached to a vehicle; neither path changes the authoritative world
        // position directly. A stale teleport guard therefore cannot freeze the helm.
        var isAttachedLocalMovement =
            _objId == character.ObjId && _moveType == MoveTypeEnum.Unit && character.Transform.Parent != null;
        var isControlledMovement = _objId != character.ObjId || _moveType != MoveTypeEnum.Unit;
        if (character.DisabledSetPosition && !isAttachedLocalMovement && !isControlledMovement)
            return;

        // While attached to a helm, seat, ladder or other slave component the target client sends
        // the character's own Unit movement in parent-local coordinates. Comparing those small
        // values with world coordinates (15k, 15k, ...) produced a fake 22 km teleport and dropped
        // every steering attempt before ship physics could see it.
        if (_objId == character.ObjId && _moveType == MoveTypeEnum.Unit && character.Transform.Parent != null)
        {
            ExecuteAttachedCharacterMovement(character);
            return;
        }

        // Anything that is not the player's own free-world step is something they are driving or riding.
        if (_objId != character.ObjId || _moveType != MoveTypeEnum.Unit)
        {
            ExecuteControlledObject(character);
            return;
        }

        var world = WorldManager.Instance.GetWorld(character.Transform.WorldId);
        if (world == null)
            return;

        var regionX = (int)(_x / WorldManager.REGION_SIZE);
        var regionY = (int)(_y / WorldManager.REGION_SIZE);
        if (!world.ValidRegion(regionX, regionY))
        {
            Logger.Warn(
                "Rejected movement outside world bounds: characterId={0}, world={1}, pos=({2:F1},{3:F1},{4:F1}), region=({5},{6})",
                character.Id,
                character.Transform.WorldId,
                _x,
                _y,
                _z,
                regionX,
                regionY);
            return;
        }

        var oldPosition = character.Transform.World.Position;
        var deltaX = _x - oldPosition.X;
        var deltaY = _y - oldPosition.Y;
        var deltaZ = _z - oldPosition.Z;
        var horizontalDelta = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var firstWorldMovement = Connection.WorldEntryReady && !character.WorldEntryComplete;

        // Server teleports use their own packets/DisabledSetPosition boundary.
        // A normal 0x0104 step that jumps hundreds of metres is a decode error
        // and must never be persisted to the characters table.
        if (horizontalDelta > MaxNormalHorizontalDelta || MathF.Abs(deltaZ) > MaxNormalVerticalDelta)
        {
            Logger.Warn(
                "Rejected suspicious target movement: characterId={0}, old=({1:F1},{2:F1},{3:F1}), new=({4:F1},{5:F1},{6:F1}), horizontal={7:F1}, vertical={8:F1}, body={9}",
                character.Id,
                oldPosition.X,
                oldPosition.Y,
                oldPosition.Z,
                _x,
                _y,
                _z,
                horizontalDelta,
                deltaZ,
                Convert.ToHexString(_body));
            return;
        }

        var oldRegion = character.Region;
        var oldZone = character.Transform.ZoneId;

        // TARGET 1.8 Unit movement carries the actor orientation as one three-byte axis-angle
        // rotation vector. The old 0x0104 handler updated X/Y/Z but deliberately kept the previous
        // server rotation, so Area plot targets were projected from a stale heading. Glider Leap
        // event 8977 is exactly such a target (20 m in front of the caster): when the client had
        // turned since the last server-authored transform, the temporary controller steered toward
        // that stale bearing and then snapped back to the locally controlled heading.
        var rotation = character.Transform.Local.Rotation;
        if (_movement is UnitMoveType unitMovement)
        {
            var quaternion = PositionAndRotation.FromMovementRotation(
                unitMovement.RotationX,
                unitMovement.RotationY,
                unitMovement.RotationZ);
            rotation = PositionAndRotation.FromQuaternion(quaternion);
        }

        if (firstWorldMovement)
            character.IsVisible = true;

        // Use Character.SetPosition instead of directly mutating Transform.
        // This updates region membership, recalculates ZoneId and runs
        // OnZoneChange, so server combat distance and persistence use the same
        // authoritative coordinates as the client.
        character.SetPosition(_x, _y, _z, rotation.X, rotation.Y, rotation.Z);
        character.Transform.ResetFinalizeTransform();
        character.Quests?.OnPositionChanged();

        // A positional player controller (notably glider Leap) is simulated by the
        // owning client. Forward that exact controller-driven step to observers instead
        // of running a second server-side Leap simulation. The sender already rendered
        // the step locally, so it must not receive an echo.
        if (character.ActiveSkillController?.UsesClientMovement == true &&
            _movement is UnitMoveType controllerMovement)
        {
            character.BroadcastPacket(
                new SCOneUnitMovementPacket(character.ObjId, controllerMovement),
                false);
        }

        var regionChanged = oldRegion != character.Region;
        var zoneChanged = oldZone != character.Transform.ZoneId;
        var publishedNpcs = WorldManager.Instance.RefreshProtocol1810NearbyNpcs(
            character,
            force: firstWorldMovement || regionChanged || zoneChanged);

        if (firstWorldMovement)
        {
            character.WorldEntryComplete = true;
            TeamManager.Instance.UpdateAtLogin(character);
            character.Expedition?.OnCharacterLogin(character);
            character.UpdateGearBonuses(null, null);
        }

        if (System.Threading.Interlocked.Increment(ref _movementProbeCount) <= 20 || regionChanged || zoneChanged)
        {
            Logger.Info(
                "Target movement accepted: characterId={0}, objId={1}, time={2}, flags=0x{3:X2}, padded={4}, scType={5}, phase={6}, pos=({7:F1},{8:F1},{9:F1}), regionChanged={10}, zone={11}->{12}, npcSent={13}",
                character.Id,
                character.ObjId,
                _time,
                _flags,
                _usedPaddedPosition,
                _scType,
                _phase,
                _x,
                _y,
                _z,
                regionChanged,
                oldZone,
                character.Transform.ZoneId,
                publishedNpcs);
        }
    }

    private void ExecuteAttachedCharacterMovement(Character character)
    {
        if (_movement is not UnitMoveType movement || character.Transform.Parent == null)
            return;

        // Target 1.8 sends the driver's steering input in the attached character's Unit
        // movement instead of a separate ShipRequest body. DeltaMovement[1] is forward/back
        // throttle and DeltaMovement[0] is left/right steering. The old handler accepted the
        // local seat coordinates but discarded these three bytes, so BoatPhysicsManager always
        // saw zero input even though the player was correctly bound to the helm.
        if (character.AttachedPoint == AttachPointKind.Driver &&
            character.Transform.Parent.GameObject is Slave ship &&
            ship.Template?.IsABoat() == true &&
            movement.DeltaMovement is { Length: >= 2 })
        {
            ship.SteeringRequest = movement.DeltaMovement[0];
            ship.ThrottleRequest = movement.DeltaMovement[1];

            if (System.Threading.Interlocked.Increment(ref _movementProbeCount) <= 60)
            {
                Logger.Debug(
                    "Ship helm input accepted: characterId={0}, ship={1}/{2}, steering={3}, throttle={4}",
                    character.Id,
                    ship.TemplateId,
                    ship.ObjId,
                    ship.SteeringRequest,
                    ship.ThrottleRequest);
            }
        }

        var movementRotation = new Vector3(
            (float)MathUtil.ConvertDirectionToRadian(movement.RotationX),
            (float)MathUtil.ConvertDirectionToRadian(movement.RotationY),
            (float)MathUtil.ConvertDirectionToRadian(movement.RotationZ));

        if (character.Bonding != null &&
            character.Transform.Parent.GameObject is AAEmu.Game.Models.Game.DoodadObj.Doodad seatDoodad &&
            seatDoodad.Transform.Parent == null)
        {
            // Target 1.8 reports a character seated on a static doodad in WORLD coordinates,
            // even though the client expects the unit to remain attached to that doodad. The
            // generic attached path used to store these values directly in Local; detaching then
            // added the chair world position a second time and produced exactly doubled X/Y/Z.
            // Convert the incoming world position to the local position expected by Transform.
            var parentWorld = seatDoodad.Transform.World;
            var localPosition = new Vector3(movement.X, movement.Y, movement.Z) - parentWorld.Position;
            var inverseParentYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -parentWorld.Rotation.Z);
            localPosition = Vector3.Transform(localPosition, inverseParentYaw);

            // InternalDetachChild adds parent rotation on detach, so store relative rotation here.
            var localRotation = movementRotation - parentWorld.Rotation;

            character.Transform.Local.SetPosition(
                localPosition.X,
                localPosition.Y,
                localPosition.Z,
                localRotation.X,
                localRotation.Y,
                localRotation.Z);

            if (System.Threading.Interlocked.Increment(ref _movementProbeCount) <= 60)
            {
                Logger.Debug(
                    "Static doodad seated movement accepted: characterId={0}, doodad={1}, world=({2:F2},{3:F2},{4:F2}), local=({5:F2},{6:F2},{7:F2})",
                    character.Id,
                    seatDoodad.ObjId,
                    movement.X,
                    movement.Y,
                    movement.Z,
                    localPosition.X,
                    localPosition.Y,
                    localPosition.Z);
            }
        }
        else
        {
            character.Transform.Local.SetPosition(
                movement.X,
                movement.Y,
                movement.Z,
                movementRotation.X,
                movementRotation.Y,
                movementRotation.Z);
        }

        // Echo the exact client movement while attached. This acknowledgement is required for a
        // stable first-click seat; only the server-side stored transform is converted above.
        character.BroadcastPacket(new SCOneUnitMovementPacket(character.ObjId, movement), true);

        if (System.Threading.Interlocked.Increment(ref _movementProbeCount) <= 40)
        {
            Logger.Debug(
                "Attached movement accepted: characterId={0}, parent={1}/{2}, local=({3:F1},{4:F1},{5:F1}), type={6}",
                character.Id,
                character.Transform.Parent.GameObject?.GetType().Name ?? "Transform",
                character.Transform.Parent.GameObject?.ObjId ?? 0,
                movement.X,
                movement.Y,
                movement.Z,
                _moveType);
        }
    }

    /// <summary>
    /// Applies a step the player made on something else - a cart, a ship, the animal under them.
    /// </summary>
    /// <remarks>
    /// The client is authoritative for what it drives, exactly as it is for the player's own
    /// steps; the server stores the state and passes it on to everyone who can see it.
    /// </remarks>
    private void ExecuteControlledObject(Character character)
    {
        if (_movement == null)
            return;

        var targetUnit = WorldManager.Instance.GetBaseUnit(_objId);

        switch (_movement)
        {
            // Steering and throttle only - the ship's own position comes from the physics step,
            // not from here. Some client paths name the ship, while the attached-character path
            // keeps the character id in the common movement header. Resolve the authoritative
            // driver parent in either case, but never allow control without the driver binding.
            case ShipRequestMoveType shipRequest:
            {
                var ship = targetUnit as Slave;
                if (ship == null &&
                    character.AttachedPoint == AttachPointKind.Driver &&
                    character.Transform.Parent?.GameObject is Slave attachedShip)
                {
                    ship = attachedShip;
                }

                if (ship == null || ship.Template?.IsABoat() != true)
                {
                    Logger.Warn(
                        "Ship request has no controllable ship: characterId={0}, packetObjId={1}, target={2}",
                        character.Id, _objId, targetUnit?.GetType().Name ?? "<null>");
                    return;
                }

                if (!ship.AttachedCharacters.TryGetValue(AttachPointKind.Driver, out var driver) ||
                    !ReferenceEquals(driver, character))
                {
                    Logger.Warn(
                        "Rejected ship request without driver binding: characterId={0}, ship={1}/{2}, attachedPoint={3}",
                        character.Id, ship.TemplateId, ship.ObjId, character.AttachedPoint);
                    return;
                }

                ship.SteeringRequest = shipRequest.Steering;
                ship.ThrottleRequest = shipRequest.Throttle;
                if (character.Transform.Parent != ship.Transform)
                    character.Transform.Parent = ship.Transform;

                if (System.Threading.Interlocked.Increment(ref _movementProbeCount) <= 100)
                {
                    Logger.Info(
                        "Ship request accepted: characterId={0}, packetObjId={1}, ship={2}/{3}, steering={4}, throttle={5}, bodyLen={6}",
                        character.Id, _objId, ship.TemplateId, ship.ObjId,
                        ship.SteeringRequest, ship.ThrottleRequest, _body.Length);
                }

                break;
            }

            // Carts and cars. Their position is whatever the driver's client says it is.
            case VehicleMoveType vehicle:
                if (targetUnit is not Slave car)
                {
                    Logger.Warn("Movement for an object that is not here: characterId={0}, objId={1}, type={2}",
                        character.Id, _objId, _moveType);
                    return;
                }

                var (rotationX, rotationY, rotationZ) =
                    MathUtil.GetSlaveRotationInDegrees(vehicle.RotationX, vehicle.RotationY, vehicle.RotationZ);

                character.Transform.Parent = car.Transform;
                car.Transform.Local.SetPosition(vehicle.X, vehicle.Y, vehicle.Z, rotationX, rotationY, rotationZ);
                car.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vehicle), true);
                car.Transform.FinalizeTransform(); // carry the passengers along
                break;

            // The animal under the player. Its own walking step stands down while it is ridden;
            // this is what replaces it.
            case UnitMoveType actor when targetUnit is Mate mate:
                mate.Transform.Local.SetPosition(actor.X, actor.Y, actor.Z,
                    (float)MathUtil.ConvertDirectionToRadian(actor.RotationX),
                    (float)MathUtil.ConvertDirectionToRadian(actor.RotationY),
                    (float)MathUtil.ConvertDirectionToRadian(actor.RotationZ));

                mate.BroadcastPacket(new SCOneUnitMovementPacket(_objId, actor), true);
                mate.Transform.FinalizeTransform();

                // A mount earns while it is carrying somebody. This never ran before, because the
                // handler holding it is on an opcode this client does not send.
                if (actor.VelX != 0 || actor.VelY != 0)
                    mate.StartUpdateXp(character);
                else
                    mate.StopUpdateXp();
                break;

            default:
                Logger.Warn("Unhandled controlled movement: characterId={0}, objId={1}, type={2}, unit={3}",
                    character.Id, _objId, _moveType, targetUnit?.GetType().Name ?? "<null>");
                break;
        }
    }

    public override string Verbose()
    {
        return $" - target1810 obj={_objId} type={_moveType} pos=({_x:F1},{_y:F1},{_z:F1}) body={_body.Length}";
    }
}
