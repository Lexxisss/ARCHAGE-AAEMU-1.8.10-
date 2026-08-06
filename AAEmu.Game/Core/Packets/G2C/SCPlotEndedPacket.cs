using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlotEndedPacket : GamePacket
{
    private readonly ushort _tl;

    public SCPlotEndedPacket(ushort tl) : base(SCOffsets.SCPlotEndedPacket, 5)
    {
        _tl = tl;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Target x2game.dll 0x399DD570: SCPlotEnded 0x0331 contains only tl:u16.
        stream.Write(_tl);

        return stream;
    }
}
