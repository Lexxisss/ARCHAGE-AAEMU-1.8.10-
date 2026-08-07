using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillExtraData1810Tests
{
    [Fact]
    public void DefaultStillWritesMandatoryZeroPresenceMask()
    {
        var stream = new PacketStream();

        SkillExtraData.Default.Write(stream);

        Assert.Equal(new byte[] { 0x00 }, stream.GetBytes());
    }

    [Fact]
    public void SparseFieldsUseRecoveredTargetWidths()
    {
        var stream = new PacketStream();
        var extra = new SkillExtraData
        {
            C = 0x11,
            E = 0x2233,
            P = 0x44556677,
            D = false
        };

        extra.Write(stream);

        Assert.Equal(
            new byte[] { 0x0F, 0x11, 0x33, 0x22, 0x77, 0x66, 0x55, 0x44, 0x00 },
            stream.GetBytes());
    }
}
