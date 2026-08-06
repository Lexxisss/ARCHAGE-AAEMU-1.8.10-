using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Sends one chunk of the specialty buyer list.
/// Target x2game.dll: factory 0x39345320, opcode 0x0018, reader 0x399D0950.
/// </summary>
public class SCSpecialtyGoodsPacket : GamePacket
{
    public const int MaxGoods = 20;
    public const int MaxEventItemIds = 50;

    private readonly bool _isBegin;
    private readonly bool _isEnd;
    private readonly IReadOnlyList<SpecialtyGoods> _goods;
    private readonly IReadOnlyList<uint> _eventItemIds;

    public SCSpecialtyGoodsPacket(bool isBegin, bool isEnd, IEnumerable<SpecialtyGoods> goods,
        IEnumerable<uint> eventItemIds = null)
        : base(SCOffsets.SCSpecialtyGoodsPacket, 5)
    {
        _isBegin = isBegin;
        _isEnd = isEnd;
        _goods = (goods ?? Array.Empty<SpecialtyGoods>()).Take(MaxGoods).ToArray();
        _eventItemIds = (eventItemIds ?? Array.Empty<uint>()).Take(MaxEventItemIds).ToArray();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // x2game.dll 0x399D0965-0x399D09CE: u32 goods count, u32 event count, bool begin, bool end.
        stream.Write((uint)_goods.Count);
        stream.Write((uint)_eventItemIds.Count);
        stream.Write(_isBegin);
        stream.Write(_isEnd);

        foreach (var goods in _goods)
            WriteGoodsRecord(stream, goods);

        foreach (var itemId in _eventItemIds)
            stream.Write(itemId); // x2game.dll 0x399D0A72-0x399D0A8C: u32, max 50

        return stream;
    }
    internal static void WriteGoodsRecord(PacketStream stream, SpecialtyGoods goods)
    {
        // Shared target record reader: x2game.dll 0x39A9D010.
        // The last byte is item grade. Dedicated resolves it through 0x39CA0970 before
        // constructing the same record at 0x39824340-0x39824429.
        stream.Write(goods.ItemId);        // +0x00 u32
        stream.Write(goods.CurrentAmount); // +0x08 i64
        stream.Write(goods.BaseAmount);    // +0x10 i64
        stream.Write(goods.Ratio);         // +0x18 u32
        stream.Write(goods.Stock);         // +0x1C u32
        stream.Write(goods.CanProduce);    // +0x20 bool
        stream.Write(goods.Currency);      // +0x24 i8 on wire
        stream.Write(goods.Grade);         // +0x28 u8 item grade
    }

}
