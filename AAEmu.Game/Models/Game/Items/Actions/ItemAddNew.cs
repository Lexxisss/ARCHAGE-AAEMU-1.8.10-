using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 action 16 (ChangeOwner): destination slot followed by the
/// common full item serializer. It shares the serializer used by Take/Remove.
/// </summary>
public class ItemAddNew : ItemTask
{
    private readonly Item _item;

    public ItemAddNew(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _item = item;
        _type = ItemAction.ChangeOwner; // 16
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        _item.Write(stream);
        return stream;
    }
}
