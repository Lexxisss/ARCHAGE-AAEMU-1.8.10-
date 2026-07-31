using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Action 7: takes an item out of a slot.
/// </summary>
/// <remarks>
/// The body is compact, not the full item record. Writing the full record here was our own
/// guess - "the mirror of action 6" - and the client ignored it, leaving the emptied slot
/// drawn as a grey ghost. This layout is taken from a working 5.0 server instead: slot, id,
/// the amount being taken, a removal timestamp, two reserved words and the template id.
/// </remarks>
public class ItemRemove : ItemTask
{
    private readonly Item _item;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly int _itemCount;
    private readonly DateTime _removeReservationTime;

    public ItemRemove(Item item)
        : this(item, item.SlotType, (byte)item.Slot)
    {
    }

    /// <summary>
    /// Overload for the slot an item is leaving, where the item object has already been
    /// moved and no longer carries the slot being cleared.
    /// </summary>
    public ItemRemove(Item item, SlotType slotType, byte slot)
    {
        _type = ItemAction.Remove; // 7
        _item = item;
        _slotType = slotType;
        _slot = slot;
        _itemCount = item.Count;
        _removeReservationTime = DateTime.UtcNow;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_slotType);        // type
        stream.Write(_slot);                  // index
        stream.Write(_item.Id);               // id
        stream.Write(_itemCount);             // stack
        stream.Write(_removeReservationTime); // removeReservationTime
        stream.Write(0u);                     // type
        stream.Write(0u);                     // dbSlaveId
        stream.Write(_item.TemplateId);       // type

        return stream;
    }
}
