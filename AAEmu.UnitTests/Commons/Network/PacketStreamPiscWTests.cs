using AAEmu.Commons.Network;

using Xunit;

namespace AAEmu.UnitTests.Commons.Network;

public class PacketStreamPiscWTests
{
    [Fact]
    public void PiscWEncodesEighteenZeroValuesAsFiveGroups()
    {
        var stream = new PacketStream();
        stream.WritePiscW(18, new long[18]);

        Assert.Equal(23, stream.Count);
        Assert.All(stream.GetBytes(), value => Assert.Equal((byte)0, value));

        var decoded = new PacketStream(stream.GetBytes()).ReadPiscW(18);
        Assert.Equal(new long[18], decoded);
    }
}
