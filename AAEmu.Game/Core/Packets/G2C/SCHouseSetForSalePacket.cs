using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseSetForSalePacket : GamePacket
{
    private readonly uint _tl;
    private readonly long _moneyAmount;
    private readonly long _sellToPlayerId;
    private readonly string _sellToName;
    private readonly string _houseName;

    public SCHouseSetForSalePacket(uint tl, long moneyAmount, long sellToPlayerId, string sellToName, string houseName) : base(SCOffsets.SCHouseSetForSalePacket, 5)
    {
        _tl = tl;
        _moneyAmount = moneyAmount;
        _sellToPlayerId = sellToPlayerId;
        _sellToName = sellToName;
        _houseName = houseName;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write(_moneyAmount);
        stream.Write(_sellToPlayerId);
        stream.Write(_sellToName);
        stream.Write(_houseName);
        return stream;
    }
}
