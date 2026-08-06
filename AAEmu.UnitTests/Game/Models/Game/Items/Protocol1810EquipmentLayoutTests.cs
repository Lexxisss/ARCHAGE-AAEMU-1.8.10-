using AAEmu.Game.Models.Game.Items;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class Protocol1810EquipmentLayoutTests
{
    [Fact]
    public void StandardScUnitStateSlotsAreNotShifted()
    {
        Assert.Equal(0, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.Head));
        Assert.Equal(19, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.Face));
        Assert.Equal(24, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.Body));
        Assert.Equal(26, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.Backpack));
        Assert.Equal(27, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.Cosplay));
    }

    [Fact]
    public void LateNamedSlotsArePermutedAtWireBoundaryOnly()
    {
        Assert.Equal(31, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.CosplayLooks));
        Assert.Equal(32, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.RaceCosplay));
        Assert.Equal(33, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.RaceCosplayLooks));

        Assert.Equal(28, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.ProtocolSlot31));
        Assert.Equal(29, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.ProtocolSlot32));
        Assert.Equal(30, Protocol1810EquipmentLayout.ToWireSlot((int)EquipmentItemSlot.ProtocolSlot33));
    }

    [Fact]
    public void EquipmentIdsUsesTheSameTargetIndicesWithoutInvalidPrefix()
    {
        Assert.Equal(0, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.Head));
        Assert.Equal(19, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.Face));
        Assert.Equal(24, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.Body));
        Assert.Equal(27, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.Cosplay));
        Assert.Equal(31, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.CosplayLooks));
        Assert.Equal(33, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.RaceCosplayLooks));
        Assert.Equal(34, Protocol1810EquipmentLayout.ToEquipmentIdsIndex((int)EquipmentItemSlot.ProtocolSlot34));
    }

    [Fact]
    public void BuildEquipmentIdsKeepsHeadCosplayAndFinalSlotBound()
    {
        var items = new Item[Protocol1810EquipmentLayout.SlotCount];
        items[(int)EquipmentItemSlot.Head] = new Item { Id = 101 };
        items[(int)EquipmentItemSlot.Cosplay] = new Item { Id = 202 };
        items[(int)EquipmentItemSlot.CosplayLooks] = new Item { Id = 303 };
        items[(int)EquipmentItemSlot.ProtocolSlot34] = new Item { Id = 404 };

        var ids = Protocol1810EquipmentLayout.BuildEquipmentIds(items);

        Assert.Equal(101UL, ids[0]);
        Assert.Equal(202UL, ids[27]);
        Assert.Equal(303UL, ids[31]);
        Assert.Equal(404UL, ids[34]);
    }
}
