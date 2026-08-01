using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Request to turn an already placed building.
/// </summary>
/// <remarks>
/// Payload is 11 bytes: <c>bc:3, zRot:f32, height:f32</c>.
///
/// The client refuses to send this at all when the player does not own the building or stands
/// too far from it, so a request arriving here has already passed those checks on its side.
/// That is not a reason to skip them server-side, only a reason not to expect them to fail
/// often. A rotation the server refuses is reported through the ordinary error message with
/// <see cref="ErrorMessageType.HouseCannotRotate"/>.
/// </remarks>
public class CSRotateHousePacket : GamePacket
{
    public CSRotateHousePacket() : base(CSOffsets.CSRotateHousePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var zRot = stream.ReadSingle();
        var height = stream.ReadSingle();

        Logger.Debug("RotateHouse, ObjId: {0}, ZRot: {1}, Height: {2}", objId, zRot, height);

        HousingManager.Instance.RotateHouse(Connection, objId, zRot, height);
    }
}
