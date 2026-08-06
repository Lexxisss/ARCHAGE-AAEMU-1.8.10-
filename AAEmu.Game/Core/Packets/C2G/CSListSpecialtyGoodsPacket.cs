using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListSpecialtyGoodsPacket : GamePacket
{
    public CSListSpecialtyGoodsPacket() : base(CSOffsets.CSListSpecialtyGoodsPacket, 5)
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

        var goods = SpecialtyManager.Instance.GetGoodsForNpc(Connection.ActiveChar, npcObjId);
        if (goods.Count == 0)
        {
            Connection.ActiveChar.SendPacket(new SCSpecialtyGoodsPacket(true, true, Array.Empty<AAEmu.Game.Models.Game.Trading.SpecialtyGoods>()));
            return;
        }

        var chunkCount = (goods.Count + SCSpecialtyGoodsPacket.MaxGoods - 1) / SCSpecialtyGoodsPacket.MaxGoods;
        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunk = goods
                .Skip(chunkIndex * SCSpecialtyGoodsPacket.MaxGoods)
                .Take(SCSpecialtyGoodsPacket.MaxGoods);
            Connection.ActiveChar.SendPacket(new SCSpecialtyGoodsPacket(
                chunkIndex == 0,
                chunkIndex == chunkCount - 1,
                chunk));
        }
    }
}
