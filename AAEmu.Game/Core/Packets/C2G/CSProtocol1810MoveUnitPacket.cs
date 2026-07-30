using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units.Movements;
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

    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public CSProtocol1810MoveUnitPacket() : base(CSOffsets.CSProtocol1810MoveUnitPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        _body = stream.LeftBytes > 0 ? stream.ReadBytes(stream.LeftBytes) : Array.Empty<byte>();
        if (_body.Length < BaseHeaderLength + PackedPositionLength)
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

            var positionOffset = BaseHeaderLength;
            if ((_flags & 0x10) != 0)
                positionOffset += OptionalScTypeAndPhaseLength;

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
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to decode target 0x0104 movement body, length={0}", _body.Length);
            _valid = false;
        }
    }

    public override void Execute()
    {
        var character = Connection.ActiveChar;
        if (!_valid || character == null || character.DisabledSetPosition)
            return;

        // The current restoration only accepts authoritative movement for the
        // active character. Vehicle, mate and slave control will be restored
        // separately from their exact 10.8 movement variants.
        if (_objId != character.ObjId || _moveType != MoveTypeEnum.Unit)
        {
            Logger.Warn(
                "Ignored unsupported target movement owner/type: characterId={0}, packetObjId={1}, type={2}",
                character.Id,
                _objId,
                _moveType);
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
        var rotation = character.Transform.Local.Rotation;

        if (firstWorldMovement)
            character.IsVisible = true;

        // Use Character.SetPosition instead of directly mutating Transform.
        // This updates region membership, recalculates ZoneId and runs
        // OnZoneChange, so server combat distance and persistence use the same
        // authoritative coordinates as the client.
        character.SetPosition(_x, _y, _z, rotation.X, rotation.Y, rotation.Z);
        character.Transform.ResetFinalizeTransform();
        character.Quests?.OnPositionChanged();

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

    public override string Verbose()
    {
        return $" - target1810 obj={_objId} type={_moveType} pos=({_x:F1},{_y:F1},{_z:F1}) body={_body.Length}";
    }
}
