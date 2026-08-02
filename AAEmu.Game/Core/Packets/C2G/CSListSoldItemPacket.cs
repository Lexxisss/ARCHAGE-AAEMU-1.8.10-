using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListSoldItemPacket : GamePacket
{
    public CSListSoldItemPacket() : base(CSOffsets.CSListSoldItemPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Six bytes: the vendor and the object being interacted with. Only the first was being
        // read, which left the rest of the body behind.
        var npcObjId = stream.ReadBc();
        var interactionObjId = stream.ReadBc();

        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null || !npc.Template.Merchant)
        {
            Logger.Debug("ListSoldItem: {0} is not a merchant (interaction {1})", npcObjId, interactionObjId);
            return;
        }
        Connection.ActiveChar.BuyBackItems.ReNumberSlots();
        Connection.SendPacket(new SCSoldItemListPacket(Connection.ActiveChar.BuyBackItems.Items));
    }
}
