using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCSpecialAbilityLearned1810Tests
{
    [Theory]
    [InlineData(AbilityType.Predator, 28)]
    [InlineData(AbilityType.Trooper, 29)]
    public void BodyIsExactlyOneAbilityByte(AbilityType ability, byte expected)
    {
        var packet = new SCSpecialAbilityLearnedPacket(ability);
        var stream = new PacketStream();

        packet.Write(stream);

        Assert.Equal(new[] { expected }, stream.GetBytes());
    }
}
