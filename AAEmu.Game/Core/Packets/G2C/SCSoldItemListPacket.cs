using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// What the player has sold to this vendor and may still buy back.
/// </summary>
/// <remarks>
/// The buy-back side of the store window is filled from this and from nothing else - the ordinary
/// item-task messages that follow a sale do not populate it.
/// </remarks>
public class SCSoldItemListPacket : GamePacket
{
    /// <summary>The client reads no more entries than this.</summary>
    public const int MaxEntries = 16;

    private readonly List<Item> _items;

    public SCSoldItemListPacket(List<Item> items) : base(SCOffsets.SCSoldItemListPacket, 5)
    {
        // Sixteen, not twelve: the client's own buy-back cart holds more than the sell cart does,
        // and cutting the list short simply hid entries the player had sold.
        _items = new List<Item>(MaxEntries);
        _items.AddRange(items.GetRange(0, Math.Min(items.Count, MaxEntries)));
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_items.Count);
        foreach (var item in _items)
            stream.Write(item);
        return stream;
    }
}
