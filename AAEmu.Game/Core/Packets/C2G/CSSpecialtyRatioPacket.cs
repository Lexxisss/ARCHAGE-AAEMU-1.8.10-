using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyRatioPacket : GamePacket
{
    public CSSpecialtyRatioPacket() : base(CSOffsets.CSSpecialtyRatioPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // target x2game.dll 0x399DCCC0: u16 followed by u32.
        var type = stream.ReadUInt16();
        var id = stream.ReadUInt32();

        // The matching SC 0x0100 has a larger conditional body which is not yet fully
        // reconstructed. Do not send the old one-int placeholder packet.
        Logger.Debug("CSSpecialtyRatio: type={0}, id={1}", type, id);
    }
}
