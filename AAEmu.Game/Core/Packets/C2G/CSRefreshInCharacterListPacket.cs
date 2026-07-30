using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRefreshInCharacterListPacket : GamePacket
{
    public CSRefreshInCharacterListPacket() : base(CSOffsets.CSRefreshInCharacterListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("RefreshInCharacterList");
        // The client names this response SC_PACKET_RACE_CONGESTION.  Older
        // AAEmu sources also called the same wire packet
        // SCRefreshInCharacterListPacket, but that alias has no independent
        // opcode in 10.8.1.0.
        Connection.SendPacket(new SCRaceCongestionPacket());
    }
}
