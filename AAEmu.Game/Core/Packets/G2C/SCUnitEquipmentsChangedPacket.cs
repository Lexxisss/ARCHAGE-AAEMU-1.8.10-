using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

using NLog;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Replaces the equipment of the named slots on a unit that already exists client-side.
/// </summary>
/// <remarks>
/// Not a character message: the handler looks the id up in the object registry and never asks
/// what kind of unit came back, so a mount or a ship is served by the same path. Beyond swapping
/// the item records it drives the appearance and model propagation, which is what makes worn
/// gear visible rather than merely listed.
///
/// It is a delta. Slots not named here keep whatever they held, so a full picture has to name
/// every slot that matters. An unknown unit id is dropped in silence and never replayed, so this
/// must not go out before the unit has been introduced.
///
/// Each record is a signed slot byte and the compact item; one mask closes the message, and bit
/// i belongs to record i rather than to the slot it names. That mask used to be a 32-bit value
/// built from the items' own flag bytes, which is neither its width nor its meaning: the client
/// keeps a single bit per slot beside the item, and what the bit stands for was not recovered,
/// so it goes out clear. The transform flag was missing entirely.
/// </remarks>
public class SCUnitEquipmentsChangedPacket : GamePacket
{
    /// <summary>The client reads no more than this many records, and no slot beyond them.</summary>
    public const int MaxRecords = 35;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly uint _objectId;
    private readonly (byte slot, Item item)[] _items;
    private readonly bool _isCharTransform;

    public SCUnitEquipmentsChangedPacket(uint objectId, (byte slot, Item item)[] items, bool isCharTransform = false)
        : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
    {
        _objectId = objectId;
        _items = items;
        _isCharTransform = isCharTransform;
    }

    public SCUnitEquipmentsChangedPacket(uint objectId, byte slot, Item item)
        : this(objectId, [(slot, item)])
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Nothing here is range checked on the way in: the slot goes straight into pointer
        // arithmetic over the unit's equipment array, and a count past the readable maximum
        // leaves the client applying records that were never written.
        var records = new List<(byte slot, Item item)>(Math.Min(_items.Length, MaxRecords));
        foreach (var (slot, item) in _items)
        {
            if (slot >= MaxRecords)
            {
                Logger.Warn($"SCUnitEquipmentsChanged: slot {slot} is past the client's last one, dropping it");
                continue;
            }

            if (records.Count == MaxRecords)
            {
                Logger.Warn($"SCUnitEquipmentsChanged: over {MaxRecords} records for objId {_objectId}, dropping the rest");
                break;
            }

            records.Add((slot, item));
        }

        stream.WriteBc(_objectId);         // uid             : bc24
        stream.Write((byte)records.Count); // num             : u8, at most 35
        stream.Write(_isCharTransform);    // isCharTransform : bool

        foreach (var (slot, item) in records)
        {
            stream.Write((sbyte)slot);     // equipSlot : i8, sign extended by the client

            if (item == null)
                stream.Write(0);           // an empty slot is the bare zero template id
            else
                stream.Write(item);
        }

        stream.Write(0UL);                 // flagsMask : u64, one bit per record

        return stream;
    }
}
