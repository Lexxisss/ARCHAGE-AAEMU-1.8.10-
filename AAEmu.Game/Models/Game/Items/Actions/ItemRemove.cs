using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Action 8: physically removes an item from a slot.
/// </summary>
/// <remarks>
/// This class used to announce action 7. Action 7 is not a removal at all - it shares its
/// serializer and its model helper with action 6, so both simply overwrite the full item
/// record of a slot that already exists. Nothing on that path unlinks or destroys the slot
/// object, which is exactly the grey ghost we could not clear.
///
/// Worse, we announced action 7 while writing this 36-byte body. The client reads 68 bytes
/// for action 7, so it swallowed 32 bytes of whatever followed and every field after the
/// action byte landed on garbage.
///
/// Action 8 is the only path that looks the item up, drops it from the slot and id
/// registries, emits the negative stack change and destroys the client-side object. The
/// lookup is driven by slot and id; the trailing metadata only keeps the UI and log
/// consistent.
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
        : this(item, slotType, slot, item.Count)
    {
    }

    /// <summary>
    /// Overload for callers that have already decremented the item, where <c>item.Count</c>
    /// no longer holds the stack being removed. The client emits this as the negative stack
    /// change, so a zero here leaves the count display stale even though the slot clears.
    /// </summary>
    public ItemRemove(Item item, SlotType slotType, byte slot, int stack)
    {
        _type = ItemAction.StoreRemove; // 8
        _item = item;
        _slotType = slotType;
        _slot = slot;
        _itemCount = stack;
        _removeReservationTime = DateTime.UtcNow;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_slotType);        // slotType              : u8
        stream.Write(_slot);                  // slotIndex             : u8
        stream.Write(_item.Id);               // id                    : u64, must be non-zero
        stream.Write(_itemCount);             // stack                 : i32
        stream.Write(_removeReservationTime); // removeReservationTime : i64
        stream.Write(_item.TemplateId);       // type                  : u32
        stream.Write(0u);                     // dbSlaveId             : u32
        stream.Write(0u);                     // type                  : u32

        return stream;
    }
}
