using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellBackpackGoodsPacket : GamePacket
{
    public CSSellBackpackGoodsPacket() : base(CSOffsets.CSSellBackpackGoodsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // target x2game.dll 0x399D12B0: two BC object references
        var firstObjId = stream.ReadBc();
        var secondObjId = stream.ReadBc();
        var npcObjId = SpecialtyManager.Instance.ResolveSpecialtyNpcObjId(firstObjId, secondObjId);
        if (npcObjId == 0)
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return;
        }

        var basePrice = SpecialtyManager.Instance.SellSpecialty(Connection.ActiveChar, npcObjId);
        Logger.Debug("CSSellBackpackGoods: first={0}, second={1}, npc={2}, basePrice={3}",
            firstObjId, secondObjId, npcObjId, basePrice);
    }
}
