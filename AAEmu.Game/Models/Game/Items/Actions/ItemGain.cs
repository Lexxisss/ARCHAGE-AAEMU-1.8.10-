using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Delivers the full item record for something the player just acquired (action 6).
///
/// Captures pair it with <see cref="ItemAdd"/>: a compact Create announcing the slot delta,
/// then this carrying everything needed to draw the item, both under the same task type.
/// We were only sending the Create, so the client knew a slot had changed but had no item
/// to render - the slot only filled in once the inventory UI was rebuilt from scratch.
/// </summary>
/// <remarks>
/// Body verified byte for byte against a live capture (op10B body 113): action, logType,
/// slotType, slot, then the standard 64-byte item record - templateId 23633, id 16841771,
/// count 1, createTime as a unix u64, madeUnitId 1025.
/// </remarks>
public class ItemGain : ItemTask
{
    private readonly Item _item;

    public ItemGain(Item item)
    {
        _type = ItemAction.Take;
        _item = item;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType); // type  : u8
        stream.Write((byte)_item.Slot);     // index : u8
        stream.Write(_item);                // full item record

        return stream;
    }
}
