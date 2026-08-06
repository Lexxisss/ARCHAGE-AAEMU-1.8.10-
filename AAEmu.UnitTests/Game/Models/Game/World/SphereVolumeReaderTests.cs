using System.Numerics;

using AAEmu.Game.Models.Game.World;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class SphereVolumeReaderTests
{
    /// <summary>
    /// Two harbour spheres as the 1.8.1.0 client's level design writes them: the Moored volume
    /// and Ezi's Divine Protection, sharing a point off the Solis Headlands pier.
    /// </summary>
    private const string TargetSample = @"area
    kind 1
    stype 2312
    pos ( x 1313.48, y 1111.46, z 100 )
    radius 500
area
    kind 1
    stype 2307
    pos ( x 1313.48, y 1111.46, z 100 )
    radius 500
";

    [Fact]
    public void Read_TargetSample_ReturnsBothVolumes()
    {
        var volumes = SphereVolumeReader.Read(TargetSample);

        Assert.Equal(2, volumes.Count);

        Assert.Equal(2312u, volumes[0].SphereId);
        Assert.Equal(1313.48f, volumes[0].Position.X, 2);
        Assert.Equal(1111.46f, volumes[0].Position.Y, 2);
        Assert.Equal(100f, volumes[0].Position.Z, 2);
        Assert.Equal(500f, volumes[0].Radius, 2);

        Assert.Equal(2307u, volumes[1].SphereId);
    }

    [Fact]
    public void Read_NegativeAndFractionalValues_AreParsedInvariantly()
    {
        var volumes = SphereVolumeReader.Read(@"area
    kind 1
    stype 1937
    pos ( x -911.692, y 1403.01, z 280.238 )
    radius 9.5
");

        var volume = Assert.Single(volumes);
        Assert.Equal(1937u, volume.SphereId);
        Assert.Equal(new Vector3(-911.692f, 1403.01f, 280.238f).X, volume.Position.X, 3);
        Assert.Equal(9.5f, volume.Radius, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("area\n    kind 1\n    stype 5\n")]
    public void Read_IncompleteInput_ReturnsNothing(string contents)
    {
        Assert.Empty(SphereVolumeReader.Read(contents));
    }

    [Fact]
    public void Read_MalformedBlock_DoesNotCostTheRestOfTheFile()
    {
        var volumes = SphereVolumeReader.Read(@"area
    kind 1
    stype not-a-number
    pos ( x 1, y 2, z 3 )
    radius 10
area
    kind 1
    stype 2309
    pos ( x 1914.9, y 1226.8, z 115.9 )
    radius 500
");

        var volume = Assert.Single(volumes);
        Assert.Equal(2309u, volume.SphereId);
    }

    [Fact]
    public void Read_ZeroRadius_IsRejected()
    {
        Assert.Empty(SphereVolumeReader.Read(@"area
    kind 1
    stype 2309
    pos ( x 1, y 2, z 3 )
    radius 0
"));
    }

    [Fact]
    public void Contains_MeasuresAgainstTheRadius()
    {
        var area = new SphereBuffArea
        {
            Position = new Vector3(1000f, 1000f, 100f),
            Radius = 500f
        };

        Assert.True(area.Contains(new Vector3(1000f, 1000f, 100f)));
        Assert.True(area.Contains(new Vector3(1400f, 1000f, 100f)));
        Assert.False(area.Contains(new Vector3(1600f, 1000f, 100f)));
    }
}
