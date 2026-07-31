using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 ItemAction.Seize payload. The client resolves the occupied
/// slot from the instance id; slot type/index are not present on the wire.
/// </summary>
public class ItemRemoveSlot : ItemTask
{
    private readonly ulong _itemId;

    public ItemRemoveSlot(Item item)
    {
        _type = ItemAction.Seize; // 13
        _itemId = item.Id;
    }

    public ItemRemoveSlot(ulong itemId, SlotType slotType, byte slot)
    {
        _type = ItemAction.Seize; // 13
        _itemId = itemId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_itemId);
        return stream;
    }
}
