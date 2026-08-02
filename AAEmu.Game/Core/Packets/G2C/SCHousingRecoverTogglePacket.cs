using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHousingRecoverTogglePacket : GamePacket
{
    private readonly uint _tl;
    private readonly bool _allowRecover;

    public SCHousingRecoverTogglePacket(uint tl, bool allowRecover) : base(SCOffsets.SCHousingRecoverTogglePacket, 5)
    {
        _tl = tl;
        _allowRecover = allowRecover;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write(_allowRecover);
        return stream;
    }
}
