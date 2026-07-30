using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces a single item move/swap to the owner, using the compact 1.8.1.0
/// layout for opcode 0x10B (SCItemTaskSuccessPacket). See SCItemAcquiredPacket
/// for the sibling "item created/updated" layout on the same opcode.
/// </summary>
public class SCItemMovedPacket : GamePacket
{
    private readonly ItemTaskType _taskType;
    private readonly SlotType _fromSlotType;
    private readonly byte _fromSlot;
    private readonly ulong _fromItemId;
    private readonly SlotType _toSlotType;
    private readonly byte _toSlot;
    private readonly ulong _toItemId;

    public SCItemMovedPacket(
        ItemTaskType taskType,
        SlotType fromSlotType,
        byte fromSlot,
        ulong fromItemId,
        SlotType toSlotType,
        byte toSlot,
        ulong toItemId)
        : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _taskType = taskType;
        _fromSlotType = fromSlotType;
        _fromSlot = fromSlot;
        _fromItemId = fromItemId;
        _toSlotType = toSlotType;
        _toSlot = toSlot;
        _toItemId = toItemId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var logType = _fromSlotType == _toSlotType ? ItemTaskLogType.SwapItem : ItemTaskLogType.MoveItem;

        stream.Write((byte)0);
        stream.Write((byte)_taskType);
        stream.Write((byte)1);

        stream.Write((byte)ItemAction.UpdateDetail);
        stream.Write((byte)logType);
        stream.Write((byte)_fromSlotType);
        stream.Write(_fromSlot);
        stream.Write((byte)_toSlotType);
        stream.Write(_toSlot);
        stream.Write(_fromItemId);
        stream.Write(_toItemId);

        stream.Write(new byte[42]);
        stream.Write(0x01000000u);

        return stream;
    }
}
