using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 ItemAction.Remove payload: source slot type/index followed
/// by the full item serializer. Used by vendor sale/buyback handling.
/// </summary>
public class ItemRemove : ItemTask
{
    private readonly Item _item;
    private readonly SlotType _slotType;
    private readonly byte _slot;

    public ItemRemove(Item item)
    {
        _type = ItemAction.Remove; // 7
        _item = item;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
    }

    public ItemRemove(Item item, SlotType slotType, byte slot)
    {
        _type = ItemAction.Remove; // 7
        _item = item;
        _slotType = slotType;
        _slot = slot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_slotType);
        stream.Write(_slot);
        stream.Write(_item);
        return stream;
    }
}
