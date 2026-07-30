using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemPacket1810Tests
{
    [Fact]
    public void EmptyEquipmentItemMatchesCompiledTargetLength()
    {
        var item = new EquipItem
        {
            TemplateId = 0x5B5B,
            Id = 0x0000000100FC0018,
            Count = 1,
            WorldId = 1
        };
        var stream = new PacketStream();

        item.Write(stream);

        Assert.Equal(112, stream.Count);
    }
}
