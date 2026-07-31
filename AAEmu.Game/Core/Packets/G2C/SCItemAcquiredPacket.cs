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
    private readonly bool _isNewItem;

    public SCItemAcquiredPacket(ItemTaskType taskType, Item item, int count, bool isNewItem = true)
        : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _taskType = taskType;
        _item = item;
        _count = Math.Max(1, count);
        _isNewItem = isNewItem;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)0);
        stream.Write((byte)_taskType);
        stream.Write((byte)1);

        // Empirically, Create/GainItem (used for genuinely new items) is silently dropped
        // by the client on this opcode - confirmed in-game: a brand new quest-reward item
        // never appeared, while a reward potion that already existed in the bag (taking
        // the AddStack/UpdateOnly branch below) stacked correctly. Until the real "new
        // item" sub-format is confirmed, use the proven-working AddStack/UpdateOnly pair
        // unconditionally; _isNewItem is kept on the packet for when that gets sorted out.
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

        stream.Write((byte)0);
        stream.Write(0u);
        stream.Write(0u);
        stream.Write(0x01000000u);

        return stream;
    }
}
