using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSPostLoadObjectStatePacket : GamePacket
{
    public CSPostLoadObjectStatePacket() : base(CSOffsets.CSPostLoadObjectStatePacket, 5) { }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }
}
