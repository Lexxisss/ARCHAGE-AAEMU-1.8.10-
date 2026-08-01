using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeHousePayPacket : GamePacket
{
    public CSChangeHousePayPacket() : base(CSOffsets.CSChangeHousePayPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = (ushort)stream.ReadUInt32(); // the handle is 32 bits on the wire; ours is 16
        var moneyAmount = stream.ReadInt32();

        Logger.Debug("ChangeHousePay, Tl: {0}, MoneyAmount: {1}", tl, moneyAmount);
    }
}
