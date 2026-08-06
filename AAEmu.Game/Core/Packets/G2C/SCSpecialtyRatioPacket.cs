using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Returns the current specialty entry for one item in the player's current zone group.
/// Target x2game.dll: factory 0x393452A0, opcode 0x0100, reader 0x399E9BF0.
/// </summary>
public class SCSpecialtyRatioPacket : GamePacket
{
    public const int MaxGoods = 20;
    public const int MaxEventItemIds = 50;

    private readonly ushort _zoneGroupId;
    private readonly uint _itemId;
    private readonly bool _isBegin;
    private readonly bool _isEnd;
    private readonly IReadOnlyList<SpecialtyGoods> _goods;
    private readonly IReadOnlyList<uint> _eventItemIds;

    public SCSpecialtyRatioPacket(ushort zoneGroupId, uint itemId, bool isBegin, bool isEnd,
        IEnumerable<SpecialtyGoods> goods, IEnumerable<uint> eventItemIds = null)
        : base(SCOffsets.SCSpecialtyRatioPacket, 5)
    {
        _zoneGroupId = zoneGroupId;
        _itemId = itemId;
        _isBegin = isBegin;
        _isEnd = isEnd;
        _goods = (goods ?? Array.Empty<SpecialtyGoods>()).Take(MaxGoods).ToArray();
        _eventItemIds = (eventItemIds ?? Array.Empty<uint>()).Take(MaxEventItemIds).ToArray();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // x2game.dll 0x399E9C05-0x399E9CA4:
        // u16 zoneGroupId, u32 itemId, u32 goodsCount, u32 eventCount, bool begin, bool end.
        stream.Write(_zoneGroupId);
        stream.Write(_itemId);
        stream.Write((uint)_goods.Count);
        stream.Write((uint)_eventItemIds.Count);
        stream.Write(_isBegin);
        stream.Write(_isEnd);

        // x2game.dll 0x399E9CAA-0x399E9D16: at most 20 shared specialty records.
        foreach (var goods in _goods)
            SCSpecialtyGoodsPacket.WriteGoodsRecord(stream, goods);

        // x2game.dll 0x399E9D16-0x399E9D81: at most 50 affected event item ids.
        foreach (var eventItemId in _eventItemIds)
            stream.Write(eventItemId);

        return stream;
    }
}
