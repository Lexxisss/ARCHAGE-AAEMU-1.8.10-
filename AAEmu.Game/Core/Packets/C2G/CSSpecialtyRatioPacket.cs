using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyRatioPacket : GamePacket
{
    public CSSpecialtyRatioPacket() : base(CSOffsets.CSSpecialtyRatioPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // target x2game.dll 0x399DCCC0 / dedicated 0x39C2DED0:
        // u16 client zone selector followed by u32 item id.
        var clientZoneGroupId = stream.ReadUInt16();
        var itemId = stream.ReadUInt32();

        // Dedicated handler 0x399B7360-0x399B750C does not trust the first field. It resolves
        // the character's current zone group and keys the response by (current zone, item id).
        var currentZoneGroupId = ZoneManager.Instance
            .GetZoneByKey(Connection.ActiveChar.Transform.ZoneId)?.GroupId ?? 0;
        if (currentZoneGroupId == 0 || currentZoneGroupId > ushort.MaxValue)
            return;

        var verifiedZoneGroupId = checked((ushort)currentZoneGroupId);
        var goods = SpecialtyManager.Instance.GetRatioGoods(verifiedZoneGroupId, itemId);
        var eventItemIds = SpecialtyManager.Instance.GetActiveEventItemIds(verifiedZoneGroupId, itemId);

        Connection.ActiveChar.SendPacket(new SCSpecialtyRatioPacket(
            verifiedZoneGroupId,
            itemId,
            true,
            true,
            goods,
            eventItemIds));

        Logger.Debug("CSSpecialtyRatio: clientZone={0}, verifiedZone={1}, item={2}, records={3}",
            clientZoneGroupId, verifiedZoneGroupId, itemId, goods.Count);
    }
}
