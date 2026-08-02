using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellHouseCancelPacket : GamePacket
{
    public CSSellHouseCancelPacket() : base(CSOffsets.CSSellHouseCancelPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = (ushort)stream.ReadUInt32(); // the handle is 32 bits on the wire; ours is 16

        Logger.Debug("SellHouseCancel, Tl: {0}", tl);
        HousingManager.Instance.CancelForSale(tl, true);
    }
}
