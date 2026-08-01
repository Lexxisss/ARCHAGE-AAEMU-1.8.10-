using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBuyHousePacket : GamePacket
{
    public CSBuyHousePacket() : base(CSOffsets.CSBuyHousePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = (ushort)stream.ReadUInt32(); // the handle is 32 bits on the wire; ours is 16
        var moneyAmount = (uint)stream.ReadInt64(); // 64 bits on the wire

        HousingManager.Instance.BuyHouse(tl, moneyAmount, Connection.ActiveChar);
    }
}
