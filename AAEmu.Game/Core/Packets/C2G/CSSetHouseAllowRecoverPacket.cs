using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetHouseAllowRecoverPacket : GamePacket
    {
        public CSSetHouseAllowRecoverPacket() : base(CSOffsets.CSSetHouseAllowRecoverPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var houseId = (ushort)stream.ReadUInt32(); // the handle is 32 bits on the wire; ours is 16  // tl
            Logger.Debug("CSSetHouseAllowRecoverPacket, houseId: {0}", houseId);
        }
    }
}
