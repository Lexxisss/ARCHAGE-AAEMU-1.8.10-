using System;
using System.Collections.Generic;
using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Item-acquisition notification (SC 0x0164).
/// Serializer recovered from x2game.dll 0x399E9AC0.
/// </summary>
public class SCItemAcquisitionPacket : GamePacket
{
    private const byte ItemAcquisitionType = 1;
    private const int MaxItems = byte.MaxValue;

    private readonly string _characterName;
    private readonly IReadOnlyList<Item> _items;

    public SCItemAcquisitionPacket(string characterName, IReadOnlyList<Item> items)
        : base(SCOffsets.SCItemAcquisitionPacket, 5)
    {
        _characterName = characterName ?? string.Empty;
        _items = items ?? Array.Empty<Item>();
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_items.Count > MaxItems)
            throw new InvalidOperationException($"SCItemAcquisitionPacket supports at most {MaxItems} items");
        if (Encoding.UTF8.GetByteCount(_characterName) > 128)
            throw new InvalidOperationException("SCItemAcquisitionPacket character name exceeds the client limit of 128 UTF-8 bytes");

        stream.Write(ItemAcquisitionType);       // type : u8; 1 dispatches OnItemAcquisition
        stream.Write(_characterName);            // charName : UTF-8, u16 length, max 128 in client
        stream.Write((byte)_items.Count);        // count : u8
        foreach (var item in _items)
            stream.Write(item);                  // full target item serializer

        return stream;
    }
}
