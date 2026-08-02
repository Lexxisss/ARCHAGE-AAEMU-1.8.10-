using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseBuildPayChangedPacket : GamePacket
{
    private readonly uint _tl;
    private readonly int _moneyAmount;

    public SCHouseBuildPayChangedPacket(uint tl, int moneyAmount) : base(SCOffsets.SCHouseBuildPayChangedPacket, 5)
    {
        _tl = tl;
        _moneyAmount = moneyAmount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write(_moneyAmount);
        return stream;
    }
}
