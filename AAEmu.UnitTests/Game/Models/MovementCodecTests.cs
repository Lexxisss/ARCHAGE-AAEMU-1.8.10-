using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World.Transform;

using Xunit;

namespace AAEmu.UnitTests.Game.Models;

/// <summary>
/// The deterministic movement-codec vectors from the target verification pass.
/// </summary>
/// <remarks>
/// These are the numbers the client's own encoders and decoders produce, so they are the cheapest
/// way to catch a movement body drifting out of shape again.
/// </remarks>
public class MovementCodecTests
{
    /// <summary>
    /// Normalized 16-bit fields divide by the largest signed short, not the power of two above
    /// it, and round the halves away from zero.
    /// </summary>
    [Theory]
    [InlineData(30f, 60f, (short)16384)]   // actor velocity +30
    [InlineData(-30f, 60f, (short)-16383)] // actor velocity -30
    [InlineData(15f, 30f, (short)16384)]   // ship velocity +15
    [InlineData(25f, 50f, (short)16384)]   // generic velocity +25
    [InlineData(60f, 60f, (short)32767)]   // actor at full scale
    public void ToNormalizedShort_MatchesTargetVectors(float value, float scale, short expected)
    {
        Assert.Equal(expected, PacketStream.ToNormalizedShort(value / scale));
    }

    /// <summary>Anything past the scale is clamped rather than wrapped or thrown away.</summary>
    [Theory]
    [InlineData(5f, (short)32767)]
    [InlineData(-5f, (short)-32767)]
    public void ToNormalizedShort_Clamps(float normalized, short expected)
    {
        Assert.Equal(expected, PacketStream.ToNormalizedShort(normalized));
    }

    /// <summary>
    /// A turn about one axis becomes a vector along that axis whose length is the fraction of a
    /// full turn. Ninety degrees is a little over a quarter after quantization, which is the
    /// coarseness the byte allows.
    /// </summary>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(90f, 32)]
    public void MovementRotation_EncodesYawAsAxisAngleVector(float degrees, int expectedZ)
    {
        var pos = new PositionAndRotation(0f, 0f, 0f, 0f, 0f, degrees * MathF.PI / 180f);

        var (x, y, z) = pos.ToRollPitchYawSBytesMovement();

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(expectedZ, z);
    }

    /// <summary>
    /// Half a turn has no short way round, so either sign is the same rotation and which one
    /// comes out depends on the last bit of the cosine. Only the length is meaningful.
    /// </summary>
    [Fact]
    public void MovementRotation_HalfTurnGoesEitherWay()
    {
        var pos = new PositionAndRotation(0f, 0f, 0f, 0f, 0f, MathF.PI);

        var (x, y, z) = pos.ToRollPitchYawSBytesMovement();

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.InRange(Math.Abs((int)z), 63, 64);
    }

    /// <summary>
    /// Past half a turn the short way round is the other way, so the sign flips rather than the
    /// length growing towards the client's upper guard.
    /// </summary>
    [Fact]
    public void MovementRotation_TakesTheShortWayRound()
    {
        var pos = new PositionAndRotation(0f, 0f, 0f, 0f, 0f, 270f * MathF.PI / 180f);

        var (x, y, z) = pos.ToRollPitchYawSBytesMovement();

        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(-32, z);
    }

    /// <summary>What is encoded comes back as the same rotation.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(150f)]
    [InlineData(-120f)]
    public void MovementRotation_RoundTrips(float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var pos = new PositionAndRotation(0f, 0f, 0f, 0f, 0f, radians);

        var (x, y, z) = pos.ToRollPitchYawSBytesMovement();
        var decoded = PositionAndRotation.FromMovementRotation(x, y, z);

        // One byte holds 127 steps over a full turn, so a couple of degrees is the best it does.
        var decodedYaw = MathF.Atan2(
            2f * (decoded.W * decoded.Z + decoded.X * decoded.Y),
            1f - 2f * (decoded.Y * decoded.Y + decoded.Z * decoded.Z));

        var difference = MathF.Abs(WrapPi(decodedYaw - radians));
        Assert.True(difference < 0.03f, $"yaw {degrees} came back {difference} radians out");
    }

    /// <summary>A vector the client would refuse gives no rotation here either.</summary>
    [Theory]
    [InlineData((sbyte)0, (sbyte)0, (sbyte)0)]
    [InlineData((sbyte)127, (sbyte)127, (sbyte)127)]
    public void MovementRotation_OutOfRangeVectorIsIdentity(sbyte x, sbyte y, sbyte z)
    {
        Assert.Equal(Quaternion.Identity, PositionAndRotation.FromMovementRotation(x, y, z));
    }

    /// <summary>
    /// Height rides in twenty-two bits over its range, and the top of that range is not one of
    /// the values it can carry - it wraps to the bottom.
    /// </summary>
    [Theory]
    [InlineData(-100f)]
    [InlineData(0f)]
    [InlineData(100f)]
    [InlineData(4095.9f)]
    public void PackedPosition_HeightSurvivesTheRoundTrip(float z)
    {
        var packed = Helpers.ConvertPosition(0f, 0f, z);
        var (_, _, decodedZ) = Helpers.ConvertPosition(packed);

        Assert.True(MathF.Abs(decodedZ - z) < 0.01f, $"height {z} came back as {decodedZ}");
    }

    /// <summary>The top of the range is clamped instead of wrapping round to the bottom.</summary>
    [Fact]
    public void PackedPosition_ClampsTheUpperHeightEndpoint()
    {
        var packed = Helpers.ConvertPosition(0f, 0f, 5000f);
        var (_, _, decodedZ) = Helpers.ConvertPosition(packed);

        Assert.True(decodedZ > 4000f, $"height above the range came back as {decodedZ}");
    }

    private static float WrapPi(float radians)
    {
        while (radians > MathF.PI)
            radians -= 2f * MathF.PI;
        while (radians < -MathF.PI)
            radians += 2f * MathF.PI;
        return radians;
    }
}
