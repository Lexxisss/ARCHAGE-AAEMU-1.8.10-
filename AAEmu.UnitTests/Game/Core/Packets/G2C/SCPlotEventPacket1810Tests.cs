using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Plots;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCPlotEventPacket1810Tests
{
    [Fact]
    public void CastingAndChannelingTimesUseUInt16TenMillisecondUnits()
    {
        var packet = new SCPlotEventPacket(
            0x1234,
            0x11223344,
            0x55667788,
            new PlotObject(0x010203),
            new PlotObject(0x040506),
            0x070809,
            1230,
            0x0A0B0C,
            4560,
            PlotEventFlags.ConditionOk,
            targetUnitIds: [0x0D0E0Fu],
            inputDirection: 7);

        var encoded = new PacketStream();
        packet.Write(encoded);

        // Two UNIT PlotObjects and no target-unit/value arrays make this layout fixed.
        Assert.Equal(42, encoded.Count);

        var stream = new PacketStream(encoded.GetBytes());
        Assert.Equal((ushort)0x1234, stream.ReadUInt16());
        Assert.Equal(0x11223344u, stream.ReadUInt32());
        Assert.Equal(0x55667788u, stream.ReadUInt32());

        Assert.Equal((byte)PlotObjectType.UNIT, stream.ReadByte());
        Assert.Equal(0x010203u, stream.ReadBc());
        Assert.Equal((byte)PlotObjectType.UNIT, stream.ReadByte());
        Assert.Equal(0x040506u, stream.ReadBc());

        Assert.Equal(0ul, stream.ReadUInt64());
        Assert.Equal(0x070809u, stream.ReadBc());
        Assert.Equal((ushort)123, stream.ReadUInt16());
        Assert.Equal(0x0A0B0Cu, stream.ReadBc());
        Assert.Equal((ushort)456, stream.ReadUInt16());

        Assert.Equal((byte)1, stream.ReadByte()); // targetUnitCount
        Assert.Equal(0x0D0E0Fu, stream.ReadBc());
        Assert.Equal((byte)PlotEventFlags.ConditionOk, stream.ReadByte());
        Assert.Equal((byte)7, stream.ReadByte());
        Assert.Equal(0, stream.LeftBytes);
    }
}
