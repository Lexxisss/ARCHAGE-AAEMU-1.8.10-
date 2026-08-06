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
/// Each record is converted from AAEmu's server slot numbering to the target 1.8.1.0 wire slot,
/// then written as a signed slot byte and item record. One mask closes the message, and bit
/// i belongs to record i rather than to the slot it names. That mask used to be a 32-bit value
/// built from the items' own flag bytes, which is neither its width nor its meaning: the client
/// keeps a single bit per slot beside the item, and what the bit stands for was not recovered,
/// so it goes out clear. The transform flag was missing entirely.
/// </remarks>
public class SCUnitEquipmentsChangedPacket : GamePacket
{
    /// <summary>The target serializer clamps one packet to 0x22 (34) records.</summary>
    public const int MaxRecords = 0x22;

    /// <summary>Valid unit wire slot indices remain 0..34 (35 addressable slots).</summary>
    public const int MaxWireSlots = 35;

    /// <summary>The slave branch has the same 34-record cap.</summary>
    public const int SlaveMaxRecords = 0x22;

    /// <summary>Valid slave wire slot indices remain 0..34.</summary>
    public const int SlaveWireSlots = 35;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly uint _objectId;
    private readonly (byte slot, Item item)[] _items;
    private readonly bool _isCharTransform;
    private readonly bool _useSlaveProtocolSlots;

    public SCUnitEquipmentsChangedPacket(
        uint objectId,
        (byte slot, Item item)[] items,
        bool isCharTransform = false,
        bool useSlaveProtocolSlots = false)
        : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
    {
        _objectId = objectId;
        _items = items;
        _isCharTransform = isCharTransform;
        _useSlaveProtocolSlots = useSlaveProtocolSlots;
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
        var maxRecords = _useSlaveProtocolSlots ? SlaveMaxRecords : MaxRecords;
        var maxWireSlots = _useSlaveProtocolSlots ? SlaveWireSlots : MaxWireSlots;
        var records = new List<(byte slot, Item item)>(Math.Min(_items.Length, maxRecords));
        foreach (var (serverSlot, item) in _items)
        {
            if (serverSlot >= maxWireSlots)
            {
                Logger.Warn(
                    $"SCUnitEquipmentsChanged: slot {serverSlot} is outside the " +
                    $"{(_useSlaveProtocolSlots ? "slave 0..34" : "unit 0..34")} range, dropping it");
                continue;
            }

            if (records.Count == maxRecords)
            {
                Logger.Warn($"SCUnitEquipmentsChanged: over {maxRecords} records for objId {_objectId}, dropping the rest");
                break;
            }

            // EquipmentSlave slots are already target wire slots. Character/NPC/mate slots
            // still require the target 35-slot late-range permutation.
            var wireSlot = _useSlaveProtocolSlots
                ? serverSlot
                : checked((byte)Protocol1810EquipmentLayout.ToWireSlot(serverSlot));
            records.Add((wireSlot, item));
        }

        stream.WriteBc(_objectId);         // uid             : bc24
        stream.Write((byte)records.Count); // num             : u8
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
