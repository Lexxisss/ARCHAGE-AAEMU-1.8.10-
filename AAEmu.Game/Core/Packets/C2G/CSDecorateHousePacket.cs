using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDecorateHousePacket : GamePacket
{
    public CSDecorateHousePacket() : base(CSOffsets.CSDecorateHousePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // The whole request, byte for byte, because the reading below has been wrong twice and
        // guessing a third time is worse than looking.
        var raw = new byte[stream.Count - stream.Pos];
        for (var i = 0; i < raw.Length; i++)
            raw[i] = stream.ReadByte();
        Logger.Info("DecorateHouse raw {0} bytes: {1}", raw.Length, BitConverter.ToString(raw));
        stream.Pos -= raw.Length;

        // Two bytes, not four. Read as four it swallowed the start of the design and left every
        // coordinate behind it a denormal fraction - the building came out as twelve million and
        // the design as a hundred and thirty-four million. Two bytes give a building of one and a
        // design of a hundred and ninety-four, which are numbers this world has.
        //
        // The fifth field in this subsystem to be written four bytes wide and read two.
        var houseId = (uint)stream.ReadUInt16();
        var designId = stream.ReadUInt32();
        var x = stream.ReadSingle();
        var y = stream.ReadSingle();
        var z = stream.ReadSingle();
        var quatX = stream.ReadSingle();
        var quatY = stream.ReadSingle();
        var quatZ = stream.ReadSingle();
        var quatW = stream.ReadSingle();

        var parentObjId = stream.ReadBc();
        var itemId = stream.ReadUInt64();

        // X, Y, Z are all relative to the house
        var posVec = new Vector3(x, y, z);
        var quat = new Quaternion(quatX, quatY, quatZ, quatW);

        Logger.Debug("DecorateHouse, houseId: {0}, designId: {1}, x: {2}, y: {3}, z: {4}, rot {5}, objId: {6}, itemId: {7}",
            houseId, designId, x, y, z, quat, parentObjId, itemId);

        if (!HousingManager.Instance.DecorateHouse(Connection.ActiveChar, houseId, designId, posVec, quat, parentObjId, itemId))
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotDecorate);
            Logger.Warn("DecorateHouse, FAILED with houseId: {0}, designId: {1}, x: {2}, y: {3}, z: {4}, rot {5}, objId: {6}, itemId: {7}", houseId, designId, x, y, z, quat, parentObjId, itemId);
        }
    }
}
