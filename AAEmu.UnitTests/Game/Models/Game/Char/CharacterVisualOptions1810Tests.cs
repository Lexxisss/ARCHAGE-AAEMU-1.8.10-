using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterVisualOptions1810Tests
{
    [Fact]
    public void NpcPresentationWritesHelmetCosplayAndPrimaryVisualByte()
    {
        var options = new CharacterVisualOptions(0x2A)
        {
            Helmet = true,
            Cosplay = true,
            CosplayVisual = 0
        };
        var stream = new PacketStream();

        options.Write(stream);

        Assert.Equal(new byte[] { 0x2A, 1, 1, 0 }, stream.GetBytes());
    }

    [Fact]
    public void NpcPresentationWritesSecondaryCostumeVisualByte()
    {
        var options = new CharacterVisualOptions(0x2A)
        {
            Helmet = true,
            Cosplay = true,
            CosplayVisual = 1
        };
        var stream = new PacketStream();

        options.Write(stream);

        Assert.Equal(new byte[] { 0x2A, 1, 1, 1 }, stream.GetBytes());
    }
}
