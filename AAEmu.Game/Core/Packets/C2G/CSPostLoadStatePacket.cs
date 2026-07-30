using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSPostLoadStatePacket : GamePacket
{
    public CSPostLoadStatePacket() : base(CSOffsets.CSPostLoadStatePacket, 5) { }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }
}
