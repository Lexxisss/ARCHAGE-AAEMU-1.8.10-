using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils.Mocks;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemTaskPacket1810Tests
{
    private static ItemMock CreateItem()
    {
        var template = new ItemTemplate { Id = 0x11223344, MaxCount = 100, BindType = ItemBindType.Normal };
        return new ItemMock(1, template, 7)
        {
            Id = 0x0102030405060708,
            SlotType = SlotType.Inventory,
            Slot = 9,
            Grade = 3
        };
    }

    [Fact]
    public void CreateActionUsesCompactTargetLayout()
    {
        var stream = new PacketStream();
        new ItemAdd(CreateItem(), -2).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(20, bytes.Length);
        Assert.Equal((byte)ItemAction.Create, bytes[0]);
        // Create pairs with MoveItem; UpdateOnly is a pairing the client does not act on.
        Assert.Equal((byte)ItemTaskLogType.MoveItem, bytes[1]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(9, bytes[3]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 4));
        // The amount is a signed delta, not the slot's new total.
        Assert.Equal(-2, BitConverter.ToInt32(bytes, 12));
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 16));
    }

    /// <summary>
    /// A stack count change is action 5 with a signed delta against a slot, not action 4.
    /// Action 4 does exist, but it carries neither a slot nor an item id - it is a currency
    /// path and can never adjust an inventory slot.
    /// </summary>
    [Fact]
    public void CountUpdateIsAStackDeltaAgainstItsSlot()
    {
        var stream = new PacketStream();
        new ItemCountUpdate(CreateItem(), -3).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(20, bytes.Length);
        Assert.Equal((byte)ItemAction.Create, bytes[0]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(9, bytes[3]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 4));
        Assert.Equal(-3, BitConverter.ToInt32(bytes, 12));
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 16));
    }

    /// <summary>
    /// The body is a 3-byte header plus a 22-byte trailing block, so a packet with no tasks
    /// is 25 and a single action 5 is 25 + 20 = 45.
    ///
    /// The earlier expectations of 45 and 65 came from measured records that carry a
    /// constant 20-byte transport envelope around this body. That envelope appeared
    /// identically on every length measured, which is what made 42 look like the size of
    /// the trailing block.
    /// </summary>
    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 45)]
    public void ItemTaskPacketMatchesMeasuredBodyLength(int taskCount, int expectedLength)
    {
        var tasks = new List<ItemTask>();
        for (var i = 0; i < taskCount; i++)
            tasks.Add(new ItemAdd(CreateItem(), 1));

        var packet = new SCItemTaskSuccessPacket(ItemTaskType.Loot, tasks, new List<ulong>());
        var stream = new PacketStream();

        packet.Write(stream);

        Assert.Equal(expectedLength, stream.Count);
    }

    /// <summary>
    /// Action 6 pairs with GainItem and carries slotType, slot and the standard 64-byte item
    /// record, starting with the template id.
    /// </summary>
    [Fact]
    public void GainActionCarriesTheFullItemRecord()
    {
        var stream = new PacketStream();
        new ItemGain(CreateItem()).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal((byte)ItemAction.Take, bytes[0]);
        Assert.Equal((byte)ItemTaskLogType.GainItem, bytes[1]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(9, bytes[3]);
        // The item record starts with its template id, not its instance id.
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 8));
    }


    /// <summary>
    /// SwapSlot is used only when both client slot objects already exist. Its target body is
    /// four slot bytes, two instance ids and a 32-bit flags field after the action header.
    /// </summary>
    [Fact]
    public void SwapSlotUsesTargetActionNineLayout()
    {
        var stream = new PacketStream();
        new ItemMove(
            SlotType.Inventory, 4, 0x0102030405060708UL,
            SlotType.Equipment, 7, 0x1112131415161718UL).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(26, bytes.Length);
        Assert.Equal((byte)ItemAction.SwapSlot, bytes[0]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(4, bytes[3]);
        Assert.Equal((byte)SlotType.Equipment, bytes[4]);
        Assert.Equal(7, bytes[5]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 6));
        Assert.Equal(0x1112131415161718UL, BitConverter.ToUInt64(bytes, 14));
        Assert.Equal(0, BitConverter.ToInt32(bytes, 22));
    }

    /// <summary>
    /// Emptying a slot is action 8. Action 7 shares both its serializer and its model helper
    /// with action 6, so it only overwrites the item record of a slot that already exists -
    /// it never unlinks or destroys one, which is what left the emptied slot drawn grey.
    /// </summary>
    [Fact]
    public void RemoveUsesThePhysicalRemovalAction()
    {
        var stream = new PacketStream();
        new ItemRemove(CreateItem()).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(36, bytes.Length);
        Assert.Equal((byte)ItemAction.StoreRemove, bytes[0]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(9, bytes[3]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 4));
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 24));
    }

    /// <summary>
    /// Action 13 parses, but the client's task dispatcher maps it straight to "not applied",
    /// so it can never clear a slot. Kept only to pin the layout.
    /// </summary>
    [Fact]
    public void SeizeContainsOnlyInstanceIdAfterActionHeader()
    {
        var stream = new PacketStream();
        new ItemRemoveSlot(CreateItem()).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(10, bytes.Length);
        Assert.Equal((byte)ItemAction.Seize, bytes[0]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 2));
    }

    [Fact]
    public void StoreRemoveUsesDedicatedActionEightLayout()
    {
        var stream = new PacketStream();
        new ItemStoreRemove(CreateItem(), SlotType.Inventory, 9, 3, DateTime.MinValue, 4, 5).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(36, bytes.Length);
        Assert.Equal((byte)ItemAction.StoreRemove, bytes[0]);
        Assert.Equal((byte)SlotType.Inventory, bytes[2]);
        Assert.Equal(9, bytes[3]);
        Assert.Equal(0x0102030405060708UL, BitConverter.ToUInt64(bytes, 4));
        Assert.Equal(3, BitConverter.ToInt32(bytes, 12));
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 24));
        Assert.Equal(4U, BitConverter.ToUInt32(bytes, 28));
        Assert.Equal(5U, BitConverter.ToUInt32(bytes, 32));
    }
}
