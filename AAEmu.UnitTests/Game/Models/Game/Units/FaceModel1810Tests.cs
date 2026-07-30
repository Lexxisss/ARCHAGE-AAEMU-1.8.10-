using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class FaceModel1810Tests
{
    [Fact]
    public void TargetVisualAndWingFieldsRoundTripAfterModifiers()
    {
        var face = new FaceModel
        {
            VisualRace = 3,
            VisualGender = 1,
            BaseRace = 4,
            BaseGender = 2,
            WingColor = 0x11223344,
            WingScale = 7,
            WingOffsetX = -5,
            WingOffsetY = 100,
            WingOffsetZ = 9
        };
        var stream = new PacketStream();
        face.Write(stream);

        var decoded = new FaceModel();
        decoded.Read(new PacketStream(stream.GetBytes()));

        Assert.Equal(face.VisualRace, decoded.VisualRace);
        Assert.Equal(face.VisualGender, decoded.VisualGender);
        Assert.Equal(face.BaseRace, decoded.BaseRace);
        Assert.Equal(face.BaseGender, decoded.BaseGender);
        Assert.Equal(face.WingColor, decoded.WingColor);
        Assert.Equal(face.WingScale, decoded.WingScale);
        Assert.Equal(face.WingOffsetX, decoded.WingOffsetX);
        Assert.Equal(face.WingOffsetY, decoded.WingOffsetY);
        Assert.Equal(face.WingOffsetZ, decoded.WingOffsetZ);
    }
}
