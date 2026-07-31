using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 action 14: action owner, slot type/index and item iid.
/// </summary>
public class ItemRemoveCrafting : ItemTask
{
    private readonly byte _actionOwnerType;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly ulong _id;

    public ItemRemoveCrafting(Item item, byte actionOwnerType = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        _actionOwnerType = actionOwnerType;
        _slotType = item.SlotType;
        _slot = checked((byte)item.Slot);
        _id = item.Id;
        _type = ItemAction.RemoveCrafting; // 14
    }

    public ItemRemoveCrafting(ulong id)
    {
        _actionOwnerType = 0;
        _slotType = SlotType.None;
        _slot = 0;
        _id = id;
        _type = ItemAction.RemoveCrafting; // 14
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_actionOwnerType);
        stream.Write((byte)_slotType);
        stream.Write(_slot);
        stream.Write(_id);
        return stream;
    }
}
