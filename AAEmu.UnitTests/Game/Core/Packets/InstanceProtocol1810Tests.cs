using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets;

public class InstanceProtocol1810Tests
{
    [Fact]
    public void ClientInstanceLoadedAndNaviPortalUseTargetOpcodes()
    {
        Assert.Equal((ushort)0x125, CSOffsets.CSInstanceLoadedPacket);
        Assert.Equal((ushort)0x133, CSOffsets.CSNaviOpenPortalPacket);
    }

    [Fact]
    public void ProcessingInstanceWritesZoneAndStateAsUInt32()
    {
        var packet = new SCProcessingInstancePacket(183, ProcessingInstanceState.UsingIndunTicket);
        var encoded = new PacketStream();

        packet.Write(encoded);

        Assert.Equal(8, encoded.Count);
        var stream = new PacketStream(encoded.GetBytes());
        Assert.Equal(183u, stream.ReadUInt32());
        Assert.Equal(1u, stream.ReadUInt32());
        Assert.Equal(0, stream.LeftBytes);
    }

    [Fact]
    public void LoadInstanceUsesTwoUInt32AndSixFloats()
    {
        var packet = new SCLoadInstancePacket(50, 260, 1f, 2f, 3f, 4f, 5f, 6f);
        var encoded = new PacketStream();

        packet.Write(encoded);

        Assert.Equal(32, encoded.Count);
        var stream = new PacketStream(encoded.GetBytes());
        Assert.Equal(50u, stream.ReadUInt32());
        Assert.Equal(260u, stream.ReadUInt32());
        Assert.Equal(1f, stream.ReadSingle());
        Assert.Equal(2f, stream.ReadSingle());
        Assert.Equal(3f, stream.ReadSingle());
        Assert.Equal(4f, stream.ReadSingle());
        Assert.Equal(5f, stream.ReadSingle());
        Assert.Equal(6f, stream.ReadSingle());
        Assert.Equal(0, stream.LeftBytes);
    }
}
