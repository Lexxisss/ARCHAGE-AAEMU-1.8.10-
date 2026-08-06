using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSwapItemsPacket : GamePacket
{
    public CSSwapItemsPacket() : base(CSOffsets.CSSwapItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var fromItemId = stream.ReadUInt64(); // i1
        var toItemId = stream.ReadUInt64();   // i2

        var fromSlotType = (SlotType)stream.ReadByte(); // type
        var fromSlot = stream.ReadByte();           // index

        var toSlotType = (SlotType)stream.ReadByte();  // type
        var toSlot = stream.ReadByte();            // index

        // The target core serializer ends here (20 bytes). Captured 10.8 packets carry an
        // additional nine-byte transport/request tail. It is not part of the swap structure,
        // but it must be consumed so PacketMarshaler does not report a false partial decode.
        var tailLength = stream.LeftBytes;
        if (tailLength > 0)
            stream.ReadBytes(tailLength);

        Logger.Debug(
            "CSSwapItems 10.8: from={0}:{1}/{2} to={3}:{4}/{5}, tailLen={6}",
            fromSlotType, fromSlot, fromItemId, toSlotType, toSlot, toItemId, tailLength);

        Connection.ActiveChar.Inventory.SplitOrMoveItem(Models.Game.Items.Actions.ItemTaskType.SwapItems, fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
        // Connection.ActiveChar.Inventory.Move(fromItemId, fromSlotType, fromSlot, toItemId, toSlotType, toSlot);
    }
}
