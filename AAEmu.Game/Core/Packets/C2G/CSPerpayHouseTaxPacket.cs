using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPerpayHouseTaxPacket : GamePacket
    {
        public CSPerpayHouseTaxPacket() : base(CSOffsets.CSPerpayHouseTaxPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = (ushort)stream.ReadUInt32(); // the handle is 32 bits on the wire; ours is 16
            var ausp = stream.ReadBoolean();

            Logger.Debug("CSPerpayHouseTaxPacket, Tl: {0}, ausp: {1}", tl, ausp);

            //TODO HousingManager.Instance.HouseTaxInfo(Connection, tl, ausp);
        }
    }
}
