using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeSlaveEquipmentPacket : GamePacket
{
    public CSChangeSlaveEquipmentPacket() : base(CSOffsets.CSChangeSlaveEquipmentPacket, 5)
    {
    }

    /// <summary>
    /// Request to install or remove equipment on a ship or land vehicle.
    /// </summary>
    /// <remarks>
    /// Header: <c>ownerPersistentId:i64, tl:u16, dbSlaveId:u32, bts:bool, num:u8</c>, then
    /// <c>num</c> records the client clamps to three. The owner id is 64 bits; reading it as
    /// 32 left the handle, the db id and the flag all coming off the wrong bytes.
    ///
    /// The records themselves are not consumed yet - applying them needs the slot definitions
    /// and the ownership and requirement checks that go with them.
    /// </remarks>
    public override void Read(PacketStream stream)
    {
        var ownerPersistentId = stream.ReadInt64();
        var tl = stream.ReadUInt16();
        var dbSlaveId = stream.ReadUInt32();
        var bts = stream.ReadBoolean();
        var num = stream.LeftBytes > 0 ? stream.ReadByte() : (byte)0;

        Logger.Debug("ChangeSlaveEquipment, Owner: {0}, Tl: {1}, DbSlaveId: {2}, Bts: {3}, Num: {4}",
            ownerPersistentId, tl, dbSlaveId, bts, num);
    }
}
