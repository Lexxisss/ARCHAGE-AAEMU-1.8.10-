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

    [Fact]
    public void AddStackUsesTemplateAndInt64Delta()
    {
        var stream = new PacketStream();
        new ItemCountUpdate(CreateItem(), -3).Write(stream);
        var bytes = stream.GetBytes();

        Assert.Equal(14, bytes.Length);
        Assert.Equal((byte)ItemAction.AddStack, bytes[0]);
        Assert.Equal(0x11223344U, BitConverter.ToUInt32(bytes, 2));
        Assert.Equal(-3L, BitConverter.ToInt64(bytes, 6));
    }

    /// <summary>
    /// A packet carrying no tasks at all measures 45 bytes, which pins the header at 3 and
    /// the trailing block at 42. A single action 5 is then 3 + 20 + 42 = 65.
    /// The old expectation of 45 for a single action 5 came from mistaking the zero-task
    /// packets for item ones, and left every 0x010B twenty bytes short on the wire.
    /// </summary>
    [Theory]
    [InlineData(0, 45)]
    [InlineData(1, 65)]
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
