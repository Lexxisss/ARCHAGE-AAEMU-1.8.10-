using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 action 8. This is the compact item removal emitted by the
/// store-sale path. Client-supplied metadata is never used to calculate the
/// refund; it is carried only as protocol context after server validation.
/// </summary>
public sealed class ItemStoreRemove : ItemTask
{
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly ulong _itemId;
    private readonly int _stack;
    private readonly DateTime _removeReservationTime;
    private readonly uint _templateId;
    private readonly uint _dbSlaveId;
    private readonly uint _type2;

    public ItemStoreRemove(
        Item item,
        SlotType slotType,
        byte slot,
        int stack,
        DateTime removeReservationTime,
        uint dbSlaveId = 0,
        uint type2 = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (stack <= 0)
            throw new ArgumentOutOfRangeException(nameof(stack));

        _type = ItemAction.StoreRemove;
        _slotType = slotType;
        _slot = slot;
        _itemId = item.Id;
        _stack = stack;
        _removeReservationTime = removeReservationTime;
        _templateId = item.TemplateId;
        _dbSlaveId = dbSlaveId;
        _type2 = type2;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_slotType);       // type : u8
        stream.Write(_slot);                 // index : u8
        stream.Write(_itemId);               // id : u64
        stream.Write(_stack);                // stack : i32
        stream.Write(_removeReservationTime);// removeReservationTime : datetime/i64
        stream.Write(_templateId);           // type : u32
        stream.Write(_dbSlaveId);            // dbSlaveId : u32
        stream.Write(_type2);                // type : u32
        return stream;
    }
}
