using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces a change to a single inventory stack to the owner. The target 1.8.1.0 client
/// repurposed opcode 0x10B (SCItemTaskSuccessPacket) for this compact, single-item layout,
/// replacing the older variable-length task-list body.
/// </summary>
/// <remarks>
/// This is the AddStack body only - the same field order 5.0 uses for ItemCountUpdate
/// (slotType, slot, id, count, templateId), padded out to the fixed 45-byte item region.
/// It is the one layout confirmed in-game: it repaints the slot immediately.
///
/// It cannot introduce an item the client has never seen, because it carries no item body.
/// The Create counterpart, which would, has an unconfirmed layout - guessing at it only got
/// the packet dropped. Callers therefore send the 0x061 chunk first so the client already
/// holds the item, then use this purely to trigger the repaint.
/// </remarks>
public class SCItemAcquiredPacket : GamePacket
{
    private readonly ItemTaskType _taskType;
    private readonly Item _item;
    private readonly int _count;

    public SCItemAcquiredPacket(ItemTaskType taskType, Item item, int count)
        : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _taskType = taskType;
        _item = item;
        _count = Math.Max(1, count);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)0);
        stream.Write((byte)_taskType);
        stream.Write((byte)1);

        stream.Write((byte)ItemAction.AddStack);
        stream.Write((byte)ItemTaskLogType.UpdateOnly);
        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);

        stream.Write(_item.Id);
        stream.Write(_count);
        stream.Write(_item.TemplateId);

        stream.Write(_item.Grade);
        stream.Write((byte)0); // flags
        stream.Write((byte)_item.DetailType);
        stream.Write(new byte[26]);

        stream.Write((byte)0); // forceRemove count
        stream.Write(0u);      // type
        stream.Write(0u);      // lockItemSlotKey
        stream.Write(0x01000000u); // flags

        return stream;
    }
}
