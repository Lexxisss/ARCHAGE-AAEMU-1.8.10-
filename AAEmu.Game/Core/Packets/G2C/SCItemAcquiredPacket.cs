using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces a single created/updated item to the owner. The target 1.8.1.0
/// client repurposed opcode 0x10B (SCItemTaskSuccessPacket) for this compact,
/// single-item layout, replacing the older variable-length task-list body.
/// </summary>
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

        stream.Write((byte)ItemAction.Create);
        stream.Write((byte)ItemTaskLogType.MoveItem);
        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        stream.Write(_item.Id);
        stream.Write(_count);
        stream.Write(_item.TemplateId);

        stream.Write(_item.Grade);
        stream.Write((byte)0); // flags
        stream.Write((byte)_item.DetailType);
        stream.Write(new byte[26]);

        stream.Write((byte)0);
        stream.Write(0u);
        stream.Write(0u);
        stream.Write(0x01000000u);

        return stream;
    }
}
