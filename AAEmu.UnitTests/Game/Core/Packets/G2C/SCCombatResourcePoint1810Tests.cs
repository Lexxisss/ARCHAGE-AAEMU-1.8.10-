using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCCombatResourcePoint1810Tests
{
    [Fact]
    public void BodyMatchesRecoveredTargetWidthsAndOrder()
    {
        var packet = new SCCombatResourcePointPacket(0x00112233, 0x44556677, 0x0102030405060708L, 0xA1B2C3D4);
        var stream = new PacketStream();

        packet.Write(stream);

        Assert.Equal(new byte[]
        {
            0x33, 0x22, 0x11,
            0x77, 0x66, 0x55, 0x44,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0xD4, 0xC3, 0xB2, 0xA1
        }, stream.GetBytes());
    }
}
